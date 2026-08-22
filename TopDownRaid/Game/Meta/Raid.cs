using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Combat;
using Game.Core;
using Game.Entities;
using Game.Graphics;
using Game.Items;
using Game.World;

namespace Game.Meta;

public enum RaidOutcome { None, Extracted, Killed, TimedOut }

/// <summary>Shared, raid-independent assets (built once at start-up).</summary>
public sealed class RaidAssets
{
    public required SpritePair Floor;
    public required SpritePair GrassFloor;
    public required Dictionary<(PropKind, bool), SpritePair> Props;
    public required Dictionary<ItemType, SpritePair> Icons;
    public required Dictionary<ItemType, SpritePair> AttachArt;
    public required ParticleSystem Particles;
    public required GraphicsDevice Device;
}

/// <summary>
/// One deployment: world + lights + enemies + projectiles + pickups + player + camera for a single map. Created from
/// the profile's loadout, ends when the player extracts (stand in a zone for HoldSeconds), dies, or the timer runs out.
/// </summary>
public sealed class Raid
{
    public MapDef Map { get; }
    public GameWorld World { get; } = new();
    public LightManager Lights { get; } = new();
    public ProjectileSystem Projectiles { get; }
    public GrenadeSystem Grenades { get; }
    public PickupManager Pickups { get; }
    public Player Player { get; }
    public EnemyManager Enemies { get; }
    public Camera2D Camera { get; } = new();
    public GameContext Ctx { get; }
    public RaidOutcome Outcome { get; private set; } = RaidOutcome.None;
    public float Time { get; private set; }
    public float TimeLeft => MathF.Max(0f, Map.RaidMinutes * 60f - Time);
    public ExtractZone? CurrentZone { get; private set; }
    public float ExtractProgress { get; private set; }          // 0..1 while standing in a zone
    public int KillsThisRaid => Enemies.Kills;
    public int GoldThisRaid => Player.Gold;
    public bool BotMode { get => Player.BotMode; set => Player.BotMode = value; }

    private readonly RaidAssets _a;
    private readonly ParticleSystem _particles;
    private readonly List<(PointLight light, Vector2 pos, float phase, float baseIntensity, float emberAcc)> _fires = new();
    private readonly List<(PointLight light, float phase, bool flickery)> _lamps = new();
    private readonly List<(ExtractZone zone, PointLight light, SpritePair marker, float acc)> _extractFx = new();
    private float _hazeAcc, _endDelay;

    public Raid(RaidAssets assets, MapDef map, Loadout loadout, InputState input)
    {
        _a = assets; _particles = assets.Particles; Map = map;
        var gd = assets.Device;
        var spawn = new Vector2(map.Size * 0.5f);
        World.Generate(map, spawn);
        Pickups = new PickupManager(assets.Icons, _particles);
        Projectiles = new ProjectileSystem(World, _particles, Lights);
        Grenades = new GrenadeSystem(World, _particles, Lights, assets.Icons[ItemType.Grenade]);
        Player = new Player(gd, spawn, Lights, loadout, assets.AttachArt) { RespawnEnabled = false };
        Enemies = new EnemyManager(gd, Lights, assets.AttachArt) { MaxAlive = map.MaxAlive, SpawnMinDistance = map.SpawnMinDistance, GunnerChance = map.GunnerChance };
        Ctx = new GameContext { World = World, Particles = _particles, Lights = Lights, Projectiles = Projectiles, Grenades = Grenades, Pickups = Pickups, Camera = Camera, Input = input, Player = Player, Enemies = Enemies };

        // a crate broken by bullets or a blast spills its (rolled-once) contents on the floor
        void Spill(Crate c)
        {
            var contents = c.EnsureContents();
            Pickups.SpawnBurst(c.Center, new List<ItemStack>(contents.NonEmpty()), 200f);
            contents.Clear();
            _particles.Puff(c.Center, new Vector2(0, -1), 10, new Vector3(0.5f, 0.42f, 0.3f), 80f, 16f, 0.9f);
            _particles.Sparks(c.Center, new Vector2(0, -1), 12, new Vector3(1.4f, 1.2f, 0.6f), 160f, 3.1f, 0.5f);
        }
        Projectiles.CrateBroken += Spill;
        Grenades.CrateBroken += Spill;

        // ---- lighting: lamps, fire barrels, extraction flares ---------------------------------------------------
        int li = 0;
        foreach (var lp in World.Lamps)
        {
            bool cold = Rng.Chance(map.ColdLampChance);
            var l = Lights.Add(new PointLight
            {
                Position = lp, Height = cold ? 260f : 220f, Radius = cold ? 860f : 760f, Intensity = cold ? 1.35f : 1.55f,
                Color = cold ? new Vector3(0.72f, 0.82f, 1.0f) : new Vector3(1.0f, 0.74f, 0.42f),
            });
            _lamps.Add((l, li * 1.7f, li % 3 == 1)); li++;
        }
        foreach (var c in World.Crates)
        {
            if (c.Kind != PropKind.FireBarrel) continue;
            var l = Lights.Add(new PointLight { Position = c.Center, Height = 55f, Radius = 360f, Intensity = 1.3f, Color = new Vector3(1.0f, 0.55f, 0.22f) });
            _fires.Add((l, c.Center, Rng.Float() * 10f, 1.3f, 0f));
        }
        foreach (var z in World.Extracts)
        {
            var l = Lights.Add(new PointLight { Position = z.Center, Height = 40f, Radius = 300f, Intensity = 0.9f, Color = new Vector3(0.35f, 1.0f, 0.45f) });
            _extractFx.Add((z, l, PropArt.CreateExtractMarker(gd, z.Area.Width, z.Area.Height), Rng.Float()));
        }

        // ---- first enemies -----------------------------------------------------------------------------------
        bool nearSpawn = float.TryParse(Environment.GetEnvironmentVariable("GAME1_SPAWN_DIST"), out var sdist);   // debug ring
        for (int i = 0; i < map.StartEnemies; i++)
        {
            var pos = nearSpawn ? spawn + MathUtil.FromAngle(i * MathHelper.TwoPi / map.StartEnemies + 0.6f) * sdist : World.RandomClearPoint(spawn, 700f, 26f);
            Enemies.Spawn(Rng.Chance(map.GunnerChance) ? EnemyDef.Gunner : EnemyDef.Brawler, pos, Ctx);
        }
        if (Environment.GetEnvironmentVariable("GAME1_SPAWN_AT_EXTRACT") == "1" && World.Extracts.Count > 0)   // debug: test the extraction flow
        { Player.Position = Player.SpawnPoint = World.Extracts[0].Center; spawn = Player.Position; }
        Camera.SnapTo(spawn, new Vector2(1280, 720));
    }

    // ================================================================================================= update
    public void Update(float dt, InputState input, Vector2 viewport, bool paused)
    {
        if (paused) { Camera.Follow(Camera.Position, dt, viewport, World.Size); return; }
        Time += dt; Ctx.Time = Time;
        Camera.ApplyScroll(input.ScrollDelta);

        if (BotMode) foreach (var e in Enemies.Alive) e.Aggro();
        Player.Update(dt, Ctx);
        Enemies.Update(dt, Ctx);
        Projectiles.Update(dt, Enemies.HittableCharacters(Player));
        Grenades.Update(dt, Enemies.HittableCharacters(Player), Enemies);
        Pickups.Update(dt, World);
        UpdateEnvironment(dt);
        Lights.Update(dt);
        foreach (var c in World.Crates) c.HitFlash = MathF.Max(0f, c.HitFlash - dt * 6f);

        // ---- extraction ------------------------------------------------------------------------------------
        CurrentZone = Player.IsAlive ? World.ExtractAt(Player.Position) : null;
        if (CurrentZone != null) ExtractProgress = MathF.Min(1f, ExtractProgress + dt / CurrentZone.HoldSeconds);
        else ExtractProgress = MathF.Max(0f, ExtractProgress - dt * 0.6f);

        // ---- outcome ---------------------------------------------------------------------------------------
        if (Outcome == RaidOutcome.None)
        {
            if (ExtractProgress >= 1f) { Outcome = RaidOutcome.Extracted; Player.ShowToast($"Extracted via {CurrentZone!.Name}"); }
            else if (!Player.IsAlive) { _endDelay += dt; if (_endDelay > 3f) Outcome = RaidOutcome.Killed; }
            else if (TimeLeft <= 0f) Outcome = RaidOutcome.TimedOut;
        }

        var target = Player.IsAlive ? Player.Position + Player.Velocity * 0.22f : Player.Position;
        Camera.Follow(target, dt, viewport, World.Size);
    }

    /// <summary>Fire barrels flicker and throw embers/smoke, lamps buzz, extract flares smoke green, haze drifts.</summary>
    private void UpdateEnvironment(float dt)
    {
        for (int i = 0; i < _fires.Count; i++)
        {
            var f = _fires[i];
            float n = TextureFactory.Noise(Time * 9f + f.phase, f.phase * 3.1f, 0) * 0.6f + TextureFactory.Noise(Time * 23f, f.phase, 0) * 0.4f;
            f.light.Intensity = f.baseIntensity * (0.75f + 0.5f * n);
            f.light.Radius = 340f + 50f * n;
            f.emberAcc += 22f * dt;
            while (f.emberAcc >= 1f)
            {
                f.emberAcc -= 1f;
                var pos = f.pos + Rng.InCircle(9f);
                if (Rng.Chance(0.75f))
                    _particles.Emit(new Particle { Position = pos, Velocity = new Vector2(Rng.Signed(20f), -70f - Rng.Float() * 60f), Lifetime = 0.5f + Rng.Float() * 0.7f,
                        Size = 6f + Rng.Float() * 8f, Aspect = 1.6f, Rotation = -MathHelper.PiOver2 + Rng.Signed(0.4f), Color = new Vector3(1.6f, 0.75f, 0.25f), Drag = 1.2f, Gravity = -60f, Emissive = true });
                else
                    _particles.Emit(new Particle { Position = pos, Velocity = new Vector2(Rng.Signed(15f), -35f), Lifetime = 2.2f + Rng.Float() * 1.5f,
                        Size = 14f + Rng.Float() * 12f, Aspect = 1f, Rotation = Rng.Angle(), Spin = Rng.Signed(1f), Color = new Vector3(0.28f, 0.27f, 0.26f), Drag = 0.6f, Gravity = -12f, Emissive = false });
            }
            _fires[i] = f;
        }
        foreach (var (light, phase, flickery) in _lamps)
        {
            if (!flickery) continue;
            float n = TextureFactory.Noise(Time * 14f + phase, phase, 0);
            light.Intensity = n > 0.82f ? 0.4f : 1.55f;
        }
        for (int i = 0; i < _extractFx.Count; i++)
        {
            var fx = _extractFx[i];
            fx.light.Intensity = 0.8f + 0.3f * MathF.Sin(Time * 3f + i);                         // slow pulse
            fx.acc += 10f * dt;
            while (fx.acc >= 1f)
            {
                fx.acc -= 1f;
                _particles.Emit(new Particle { Position = fx.zone.Center + Rng.InCircle(14f), Velocity = new Vector2(Rng.Signed(12f), -40f - Rng.Float() * 30f),
                    Lifetime = 2.5f + Rng.Float() * 1.5f, Size = 16f + Rng.Float() * 14f, Aspect = 1f, Rotation = Rng.Angle(), Spin = Rng.Signed(0.8f),
                    Color = new Vector3(0.30f, 0.95f, 0.45f), Drag = 0.5f, Gravity = -14f, Emissive = true });
            }
            _extractFx[i] = fx;
        }
        _hazeAcc += 12f * dt;
        var vis = Camera.VisibleWorld;
        while (_hazeAcc >= 1f)
        {
            _hazeAcc -= 1f;
            var pos = new Vector2(Rng.Range(vis.Min.X, vis.Max.X), Rng.Range(vis.Min.Y, vis.Max.Y));
            _particles.Emit(new Particle { Position = pos, Velocity = new Vector2(18f + Rng.Signed(10f), -6f + Rng.Signed(6f)), Lifetime = 3f + Rng.Float() * 3f,
                Size = 30f + Rng.Float() * 40f, Aspect = 1f, Rotation = Rng.Angle(), Spin = Rng.Signed(0.3f), Color = new Vector3(0.22f, 0.22f, 0.24f), Drag = 0f, Gravity = 0f, Emissive = false });
        }
    }

    // ================================================================================================= draw
    /// <summary>Called twice per frame by the pipeline (albedo pass, normal pass). Painter's order, back to front.</summary>
    public void DrawScene(SceneBatch batch, RectangleF visible)
    {
        batch.DrawTiled(Map.Floor == FloorKind.Grass ? _a.GrassFloor : _a.Floor, World.Bounds);
        foreach (var (zone, _, marker, _) in _extractFx)
            if (visible.Inflate(200).Intersects(zone.Area)) batch.Draw(marker, zone.Center, 1f);
        foreach (var (pos, kind) in World.Decor)
            if (visible.Inflate(64).Contains(pos)) batch.Draw(_a.Props[(kind, false)], pos, 1f);
        Pickups.Draw(batch);
        foreach (var c in World.Crates)
        {
            if (!visible.Inflate(180).Intersects(c.Bounds)) continue;
            Color tint = Color.White;
            if (c.Lootable) tint = c.Opened ? new Color(120, 110, 100) : new Color(255, 240, 200);
            if (c.HitFlash > 0f) tint = Color.Lerp(tint, Color.White, c.HitFlash);
            var dr = c.Bounds; int inf = PropDefs.DrawInflate(c.Kind);           // tree canopies overhang their trunk
            if (inf > 0) dr.Inflate(inf, inf);
            batch.DrawRect(_a.Props[(c.Kind, c.Vertical)], dr, tint);
        }
        Grenades.Draw(batch);
        Enemies.Draw(batch, visible);
        Player.Draw(batch);
    }

    public IReadOnlyList<PointLight> ActiveLights(RectangleF visible) => Lights.GetActive(visible.Min - new Vector2(200), visible.Max + new Vector2(200));
}
