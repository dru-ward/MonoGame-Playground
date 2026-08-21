using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Combat;
using Game.Core;
using Game.Graphics;
using Game.Items;

namespace Game.Entities;

public enum EnemyKind { Brawler, Gunner }
public enum EnemyState { Idle, Chase, Attack, Dead }

/// <summary>Per-kind tuning.</summary>
public sealed record EnemyDef(EnemyKind Kind, string Name, float Health, float Speed, float AggroRange, float LoseRange,
                              float AttackRange, float AttackDamage, float AttackInterval, float WindUp, int ScoreValue,
                              CharacterStyle Style, LootTable Loot, WeaponDef? Weapon)
{
    public static readonly EnemyDef Brawler = new(EnemyKind.Brawler, "Brawler", 60f, 250f, 560f, 900f,
        AttackRange: 6f, AttackDamage: 14f, AttackInterval: 0.9f, WindUp: 0.3f, ScoreValue: 50,
        CharacterStyle.Brawler, LootTable.BrawlerBody, null);

    public static readonly EnemyDef Gunner = new(EnemyKind.Gunner, "Gunner", 45f, 205f, 680f, 1000f,
        AttackRange: 470f, AttackDamage: 8f, AttackInterval: 0.55f, WindUp: 0f, ScoreValue: 80,
        CharacterStyle.Gunner, LootTable.GunnerBody, WeaponDef.EnemyPistol);   // actual gun is rolled from WeaponDef.EnemyPool
}

/// <summary>
/// An AI character. Idle: wanders around its spawn. Chase: approaches the player once inside AggroRange (or when
/// shot). Attack: Brawlers wind up and hit at contact; Gunners keep a preferred distance, strafe and shoot when
/// they have line of sight. Drops loot on death and fades out.
/// </summary>
public sealed class Enemy : Character
{
    public EnemyDef Def { get; }
    public EnemyState State { get; private set; } = EnemyState.Idle;
    /// <summary>What this body carries. Filled on death; the player searches it with E.</summary>
    public Inventory Loot { get; } = new();
    public bool Looted;                              // opened at least once (fades soon after)
    public const float CorpseLifetime = 90f;
    public bool ReadyToRemove => !IsAlive && (Looted ? DeadTimer > _lootedAt + 2.5f : DeadTimer > CorpseLifetime);
    public override float CorpseAlpha => !IsAlive && Looted ? MathHelper.Clamp(1f - (DeadTimer - _lootedAt) / 2.5f, 0f, 1f) : (!IsAlive && DeadTimer > CorpseLifetime - 3f ? MathHelper.Clamp((CorpseLifetime - DeadTimer) / 3f, 0f, 1f) : 1f);
    private float _lootedAt;
    public WeaponDef? WeaponDef => _weapon?.Def;
    public string BodyName => Def.Name.ToUpperInvariant() + (_weapon != null ? $" ({_weapon.Def.Name.ToUpperInvariant()})" : "");

    private readonly Vector2 _home;
    private Vector2 _wanderTarget;
    private float _wanderTimer, _attackTimer, _windUp, _strafeDir = 1f, _strafeTimer, _alertTimer;
    private readonly Weapon? _weapon;
    private readonly LightManager _lights;
    private readonly PointLight? _torchLight;

    private float _swingTimer;                     // bat follow-through after a hit

    public Enemy(EnemyDef def, Vector2 pos, CharacterRig rig, LightManager lights, Dictionary<ItemType, SpritePair>? attachArt = null)
    {
        Def = def; Faction = Faction.Enemy; Position = pos; _home = pos; _wanderTarget = pos;
        MaxHealth = Health = def.Health; Rig = rig; HeldWeapon = def.Style.Weapon; _lights = lights;
        Facing = MoveFacing = BodyFacing = Rng.Angle();
        if (def.Weapon != null)
        {
            // gunners roll a random gun; the arm layer and the engagement range follow it
            var wdef = def.Kind == EnemyKind.Gunner ? Rng.Pick(Combat.WeaponDef.EnemyPool) : def.Weapon;
            _weapon = new Weapon(wdef) { AmmoInMag = wdef.MagSize };
            HeldWeapon = wdef.Held;
            // some gunners carry a torch (you see their cone coming) or a random other attachment; it drops with the gun
            if (Rng.Chance(0.35f)) _weapon.TryAttach(ItemType.Torch, out _);
            else if (Rng.Chance(0.35f)) _weapon.TryAttach(Rng.Pick(new[] { ItemType.Optic, ItemType.Compensator, ItemType.Suppressor, ItemType.Grip, ItemType.Laser }), out _);
            if (_weapon.HasTorch)
                _torchLight = lights.Add(new PointLight { Height = 45f, Radius = 700f, Intensity = 1.4f, Color = new Vector3(1f, 0.96f, 0.85f), ConeOuterDeg = 24f, ConeInnerDeg = 10f });
            if (attachArt != null)
                foreach (var kv in _weapon.Attachments)
                    if (AttachPoints.Get(HeldWeapon, kv.Key) is { } local && attachArt.TryGetValue(kv.Value, out var art)) ArmOverlays.Add((art, local));
        }
    }

    /// <summary>Ranged engagement distance: the weapon's, else the def's.</summary>
    public float AttackRange => _weapon?.Def.Range ?? Def.AttackRange;

    /// <summary>Marks the body as searched (starts the fade once the player closes the loot screen).</summary>
    public void MarkLooted() { if (!Looted) { Looted = true; _lootedAt = DeadTimer; } }

    /// <summary>Fills the corpse inventory: the carried gun (player-grade) + 1-3 mags for it + personal effects.</summary>
    private void FillLoot()
    {
        if (_weapon != null)
        {
            var pg = _weapon.Def.PlayerGrade;
            if (pg.GunItem is { } gun && Rng.Chance(0.9f)) Loot.Add(gun, 1);
            if (pg.Mag is { } mag) Loot.Add(mag, Rng.Int(1, 4));
            foreach (var a in _weapon.Attachments.Values) Loot.Add(a, 1);
        }
        foreach (var s in Def.Loot.Roll()) Loot.Add(s.Type, s.Count);
    }

    /// <summary>Forces pursuit for a while even beyond LoseRange (being shot from afar, bot mode).</summary>
    public void Aggro() { if (!IsAlive) return; _alertTimer = 6f; if (State == EnemyState.Idle) State = EnemyState.Chase; }
    public void CalmDown() { if (IsAlive) State = EnemyState.Idle; }

    public override void TakeDamage(float amount, Vector2 hitDir, Vector2 hitPos)
    {
        base.TakeDamage(amount, hitDir, hitPos);
        Aggro();
        Velocity += hitDir * 60f;                                     // knock-back
    }

    protected override void OnDeath() { State = EnemyState.Dead; Velocity = Vector2.Zero; FillLoot(); if (_torchLight != null) _torchLight.Enabled = false; }

    public void Update(float dt, GameContext ctx)
    {
        TickCommon(dt);
        if (!IsAlive) return;
        var player = ctx.Player;
        var toPlayer = player.Position - Position; float dist = toPlayer.Length();
        var dirToPlayer = dist > 1e-3f ? toPlayer / dist : Vector2.UnitX;
        bool playerAlive = player.IsAlive;
        _alertTimer = MathF.Max(0f, _alertTimer - dt);
        _weapon?.Update(dt);
        _swingTimer = MathF.Max(0f, _swingTimer - dt);

        // ---- state transitions ---------------------------------------------------------------------------
        switch (State)
        {
            case EnemyState.Idle:
                if (playerAlive && dist < Def.AggroRange && (dist < 200f || ctx.World.HasLineOfSight(Position, player.Position))) State = EnemyState.Chase;
                break;
            case EnemyState.Chase:
            case EnemyState.Attack:
                if (!playerAlive || (dist > Def.LoseRange && _alertTimer <= 0f)) { State = EnemyState.Idle; _windUp = 0f; break; }
                bool inRange = Def.Kind == EnemyKind.Brawler
                    ? dist < Radius + player.Radius + Def.AttackRange
                    : dist < AttackRange && ctx.World.HasLineOfSight(Position, player.Position);
                State = inRange ? EnemyState.Attack : EnemyState.Chase;
                break;
        }

        // ---- behaviour ------------------------------------------------------------------------------------
        Vector2 wanted = Vector2.Zero;
        switch (State)
        {
            case EnemyState.Idle:
                _wanderTimer -= dt;
                if (_wanderTimer <= 0f || (_wanderTarget - Position).Length() < 20f)
                {
                    _wanderTimer = 2f + Rng.Float() * 3f;
                    _wanderTarget = Rng.Chance(0.35f) ? Position : _home + Rng.InCircle(260f);
                }
                var toW = _wanderTarget - Position;
                if (toW.Length() > 12f) { wanted = MathUtil.SafeNormalize(toW) * Def.Speed * 0.35f; Facing = MathUtil.LerpAngle(Facing, MathUtil.ToAngle(toW), MathUtil.Damp(4f, dt)); }
                break;

            case EnemyState.Chase:
                wanted = SteerToward(player.Position, ctx) * Def.Speed;
                Facing = MathUtil.LerpAngle(Facing, MathUtil.ToAngle(dirToPlayer), MathUtil.Damp(10f, dt));
                if (Def.Kind == EnemyKind.Gunner && dist < AttackRange * 1.3f && _weapon != null && ctx.World.HasLineOfSight(Position, player.Position))
                    TryShoot(dirToPlayer, ctx);   // fire on the move when close enough
                break;

            case EnemyState.Attack:
                Facing = MathUtil.LerpAngle(Facing, MathUtil.ToAngle(dirToPlayer), MathUtil.Damp(14f, dt));
                if (Def.Kind == EnemyKind.Brawler)
                {
                    _attackTimer -= dt;
                    if (_attackTimer <= 0f)
                    {
                        _windUp += dt;
                        if (_windUp >= Def.WindUp)
                        {
                            _windUp = 0f; _attackTimer = Def.AttackInterval;
                            _swingTimer = 0.18f;
                            if (dist < Radius + player.Radius + Def.AttackRange + 10f)
                            {
                                player.TakeDamage(Def.AttackDamage, dirToPlayer, player.Position);
                                ctx.Particles.Sparks(player.Position - dirToPlayer * player.Radius * 0.5f, -dirToPlayer, 6, new Vector3(1.2f, 1.1f, 0.9f), 200f, 1.5f, 0.25f);
                                RecoilKick = 6f;
                            }
                        }
                    }
                    wanted = dirToPlayer * Def.Speed * 0.35f;                          // press in slightly
                }
                else
                {
                    // Gunner: hold a preferred band (170..320 px), strafe sideways, shoot.
                    _strafeTimer -= dt;
                    if (_strafeTimer <= 0f) { _strafeTimer = 1f + Rng.Float() * 1.5f; _strafeDir = Rng.Chance(0.5f) ? 1f : -1f; }
                    var side = new Vector2(-dirToPlayer.Y, dirToPlayer.X) * _strafeDir;
                    float far = AttackRange * 0.68f, near = AttackRange * 0.36f;
                    Vector2 radial = dist > far ? dirToPlayer : dist < near ? -dirToPlayer : Vector2.Zero;
                    wanted = MathUtil.SafeNormalize(radial * 0.8f + side * 0.6f) * Def.Speed * 0.8f;
                    TryShoot(dirToPlayer, ctx);
                }
                break;
        }

        // ---- arm-layer animation ---------------------------------------------------------------------------
        Vector2 armsTarget = Vector2.Zero; float angleTarget = 0f;
        if (Def.Kind == EnemyKind.Brawler)
        {
            // wind-up pulls the bat back over the shoulder, the swing snaps it forward through the target
            angleTarget = -1.1f * WindUpProgress + 0.9f * (_swingTimer / 0.18f);
            armsTarget = new Vector2(-2f, 0f) * WindUpProgress;
            if (State == EnemyState.Chase) { angleTarget += -0.35f; armsTarget += new Vector2(-1f, 1f); }   // carrying the bat while running
        }
        else if (_weapon != null && _weapon.IsReloading)
        {
            float k = MathF.Sin((1f - _weapon.ReloadTimer / _weapon.Def.ReloadTime) * MathF.PI);
            armsTarget = new Vector2(-4f, 4f) * k; angleTarget = -0.35f * k;
        }
        else if (State == EnemyState.Idle) { angleTarget = 0.25f; armsTarget = new Vector2(-2f, 1f); }   // relaxed low-ready
        ArmsOffset = Vector2.Lerp(ArmsOffset, armsTarget, MathUtil.Damp(_swingTimer > 0f ? 40f : 14f, dt));
        ArmsAngle = MathHelper.Lerp(ArmsAngle, angleTarget, MathUtil.Damp(_swingTimer > 0f ? 40f : 14f, dt));

        // ---- integrate + collide -----------------------------------------------------------------------
        Velocity = MathUtil.Approach(Velocity, wanted, 1800f * dt);
        Position += Velocity * dt;
        ctx.World.ResolveCircle(ref Position, ref Velocity, Radius);
        if (_torchLight != null) { _torchLight.Position = MuzzleWorld(); _torchLight.Direction = MathUtil.FromAngle(Facing + ArmsAngle); }
    }

    /// <summary>Direct pursuit with a simple obstacle sidestep: if a crate blocks the ray, steer around its side.</summary>
    private Vector2 SteerToward(Vector2 target, GameContext ctx)
    {
        var dir = MathUtil.SafeNormalize(target - Position);
        var probe = Position + dir * (Radius + 60f);
        if (ctx.World.CastSegment(Position, probe, out _, out var n, out var crate) && crate != null)
        {
            // slide along the blocking face; pick the side that is closer to the target
            var slide = new Vector2(-n.Y, n.X);
            if (Vector2.Dot(slide, target - Position) < 0f) slide = -slide;
            dir = MathUtil.SafeNormalize(dir * 0.3f + slide * 0.9f);
        }
        return dir;
    }

    private void TryShoot(Vector2 dirToPlayer, GameContext ctx)
    {
        if (_weapon == null) return;
        _weapon.FinishReloadIfDue(999);
        if (_weapon.AmmoInMag <= 0 && !_weapon.IsReloading) { _weapon.BeginReload(999); return; }
        if (_weapon.TryFire(true, Facing, false, infiniteAmmo: false, out float ang))
        {
            var muzzle = MuzzleWorld(); var dir = MathUtil.FromAngle(ang);
            for (int i = 0; i < _weapon.Def.Pellets; i++)
            {
                var pd = MathUtil.FromAngle(_weapon.PelletAngle(ang, i));
                ctx.Projectiles.Spawn(muzzle, pd * _weapon.Def.BulletSpeed, _weapon.Def.Damage, _weapon.Def.MaxRicochets, Faction.Enemy, _weapon.Def.Tracer, _weapon.Def.Pellets > 1 ? 0.5f : 1.6f);
            }
            ctx.Particles.Sparks(muzzle, dir, 4, new Vector3(1.4f, 0.8f, 0.4f), 300f, 0.8f, 0.15f);
            _lights.Flash(muzzle, new Vector3(1f, 0.7f, 0.4f), 220f, 1.6f, 0.08f, 40f);
            RecoilKick = 4f;
        }
    }

    public Vector2 MuzzleWorld() => WeaponLocalToWorld(CharacterArt.MuzzleLocal(HeldWeapon));

    /// <summary>0..1 wind-up progress (HUD shows a warning ring on brawlers about to strike).</summary>
    public float WindUpProgress => Def.WindUp > 0f ? MathHelper.Clamp(_windUp / Def.WindUp, 0f, 1f) : 0f;
}
