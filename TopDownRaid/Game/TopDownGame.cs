// =====================================================================================================================
//  TopDownGame.cs — host / state machine.
//
//  Menu → Hideout (stash + loadout) → Map select → Raid → Summary → Hideout …
//
//  Folder map
//    Core/       Camera2D, InputState, MathUtil/Rng
//    Graphics/   RenderPipeline (deferred passes), GraphicsStates, SceneBatch, ParticleSystem, LightManager,
//                TextureFactory, ShapeSprite, CharacterArt, PropArt, PixelFont
//    World/      GameWorld (props, collision, segment casts, extracts), Collision, MapDef
//    Entities/   Character, Player, Enemy (+EnemyDef), EnemyManager, GameContext
//    Combat/     WeaponDef/Weapon, ProjectileSystem (bullets, ricochet, damage)
//    Items/      ItemDef/ItemStack, Inventory, LootTable, PickupManager, ItemArt
//    Meta/       Profile (stash/loadout/stats, JSON save), Raid (one deployment)
//    UI/         Hud, InventoryScreen, MetaScreens (menu/stash/map/summary), UiDraw
//
//  Controls (raid): WASD move · Shift sprint · Mouse aim · LMB fire · R reload · Q swap · T torch/laser toggle
//                   E search body / open crate / take floor item · Tab/I inventory (LMB use, drag move, RMB inspect)
//                   1-5 hotbar · Wheel zoom · Space pause · Esc = abandon raid (counts as MIA)
//  Debug: F1 wireframe · F2 scissor · F3 bloom · F4 light mode · F5-F10 buffer views · F11 HUD text · F12 screenshot
//  Env: GAME1_SCREENSHOT, GAME1_SHOT_DELAY, GAME1_VIEW, GAME1_ZOOM, GAME1_BOT=1, GAME1_SPAWN_DIST, GAME1_UI=inv|loot,
//       GAME1_STATE=menu|stash|map|raid|summary (start there), GAME1_MAP=<id>, GAME1_NOSAVE=1
// =====================================================================================================================
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Game.Core;
using Game.Entities;
using Game.Graphics;
using Game.Items;
using Game.Meta;
using Game.UI;
using Game.World;

namespace Game;

public enum GameState { Menu, Stash, MapSelect, Raid, Summary }

public sealed class TopDownGame : Microsoft.Xna.Framework.Game
{
    private readonly GraphicsDeviceManager _graphics;
    private RenderPipeline _pipeline = null!;
    private ParticleSystem _particles = null!;
    private RaidAssets _assets = null!;
    private PixelFont _font = null!;
    private Profile _profile = null!;
    private readonly InputState _input = new();

    private Hud _hud = null!;
    private InventoryScreen _inventoryScreen = null!;
    private MenuScreen _menu = null!;
    private StashScreen _stash = null!;
    private MapSelectScreen _mapSelect = null!;
    private SummaryScreen _summary = null!;

    private GameState _state = GameState.Menu;
    private Raid? _raid;
    private bool _paused;
    private float _time;
    private int _frames; private double _fpsTimer; private int _fps;

    // headless / debug knobs
    private readonly string? _autoShot = Environment.GetEnvironmentVariable("GAME1_SCREENSHOT");
    private readonly bool _bot = Environment.GetEnvironmentVariable("GAME1_BOT") == "1";
    private readonly double _shotDelay = double.TryParse(Environment.GetEnvironmentVariable("GAME1_SHOT_DELAY"), out var sd) ? sd : 3.0;
    private readonly string? _uiDemo = Environment.GetEnvironmentVariable("GAME1_UI");
    private readonly bool _noSave = Environment.GetEnvironmentVariable("GAME1_NOSAVE") == "1";
    private double _runTime; private bool _shotRequested; private string? _shotPath; private bool _uiDemoDone;

    public TopDownGame()
    {
        int rw = 1280, rh = 720;
        if (Environment.GetEnvironmentVariable("GAME1_RES") is { } res && res.Split('x') is { Length: 2 } parts && int.TryParse(parts[0], out var pw) && int.TryParse(parts[1], out var ph)) { rw = pw; rh = ph; }
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = rw, PreferredBackBufferHeight = rh,
            GraphicsProfile = GraphicsProfile.HiDef, PreferMultiSampling = false, SynchronizeWithVerticalRetrace = true,
        };
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.AllowUserResizing = true;
        Window.Title = "TopDown Raid";
    }

    // ================================================================================================= load
    protected override void LoadContent()
    {
        var gd = GraphicsDevice;
        _pipeline = new RenderPipeline(gd, Content);
        if (int.TryParse(Environment.GetEnvironmentVariable("GAME1_VIEW"), out int v) && v is >= 1 and <= 6) _pipeline.View = (RenderPipeline.DebugView)v;

        _particles = new ParticleSystem(gd, TextureFactory.CreateParticle(gd, 64));
        _font = new PixelFont(gd);
        var floor = new SpritePair(TextureFactory.CreateAsphaltAlbedo(gd, 512), TextureFactory.CreateAsphaltNormal(gd, 512));
        var grass = new SpritePair(TextureFactory.CreateGrassAlbedo(gd, 512), TextureFactory.CreateGrassNormal(gd, 512));
        var icons = ItemArt.CreateAll(gd);
        _assets = new RaidAssets { Floor = floor, GrassFloor = grass, Props = PropArt.CreateAll(gd), Icons = icons, AttachArt = AttachmentArt.CreateAll(gd), Particles = _particles, Device = gd };

        _hud = new Hud(_font, _pipeline.Pixel, icons);
        _inventoryScreen = new InventoryScreen(_font, _pipeline.Pixel, icons);
        _menu = new MenuScreen(_font, _pipeline.Pixel, icons, floor.Albedo);
        _stash = new StashScreen(_font, _pipeline.Pixel, icons, floor.Albedo);
        _mapSelect = new MapSelectScreen(_font, _pipeline.Pixel, icons, floor.Albedo);
        _summary = new SummaryScreen(_font, _pipeline.Pixel, icons, floor.Albedo);

        _profile = _noSave ? NewProfile() : Profile.Load();
        if (Environment.GetEnvironmentVariable("GAME1_MAP") is { } mapId) _profile.SelectedMapId = mapId;

        // start state (debug): default menu; GAME1_STATE=raid deploys straight away with an auto loadout
        switch (Environment.GetEnvironmentVariable("GAME1_STATE"))
        {
            case "stash": _state = GameState.Stash; break;
            case "map": _state = GameState.MapSelect; break;
            case "raid": if (!_profile.Loadout.HasWeapon) AutoLoadout(); StartRaid(); break;
            case "summary":
                _summary.Outcome = RaidOutcome.Extracted; _summary.MapName = "SCRAPYARD"; _summary.Kills = 5; _summary.Gold = 23; _summary.Duration = 312;
                _summary.Brought.Add(new ItemStack(ItemType.RifleMag, 3)); _summary.Weapons.Add(ItemType.GunRifle); _state = GameState.Summary; break;
            default: _state = GameState.Menu; break;
        }
    }

    private static Profile NewProfile() { var p = new Profile(); p.GiveStarterKit(); return p; }

    /// <summary>Debug helper: first gun + all mags + a bandage from the stash.</summary>
    private void AutoLoadout()
    {
        for (int i = 0; i < _profile.Stash.Count; i++) if (!_profile.Stash[i].IsEmpty && _profile.Stash[i].Def.IsWeapon) { _profile.MoveToLoadout(i); break; }
        for (int i = 0; i < _profile.Stash.Count; i++) if (!_profile.Stash[i].IsEmpty && _profile.Stash[i].Def.Category == ItemCategory.Magazine) _profile.MoveToLoadout(i);
        for (int i = 0; i < _profile.Stash.Count; i++) if (!_profile.Stash[i].IsEmpty && _profile.Stash[i].Type == ItemType.Bandage) { _profile.MoveToLoadout(i); break; }
        for (int i = 0; i < _profile.Stash.Count; i++) if (!_profile.Stash[i].IsEmpty && (_profile.Stash[i].Def.IsGear || _profile.Stash[i].Def.IsAttachment || _profile.Stash[i].Type == ItemType.Grenade)) _profile.MoveToLoadout(i);
        var lo = _profile.Loadout;
        for (int i = 0; i < lo.Bag.Count; i++) { if (lo.Bag[i].IsEmpty) continue; if (lo.Bag[i].Def.IsAttachment) lo.AttachFromBag(i, 0); else if (lo.Bag[i].Def.IsGear) lo.WearFromBag(i); }
    }

    // ================================================================================================= state changes
    private void StartRaid()
    {
        _profile.EnsureMinimumLoadout();
        var map = MapDef.ById(_profile.SelectedMapId);
        _raid = new Raid(_assets, map, _profile.Loadout, _input);
        _raid.Player.LootRequested += src => _inventoryScreen.OpenWith(_raid.Player, src);
        _pipeline.Ambient = new Vector4(map.Ambient, 0f);
        _pipeline.SetGrade(map.Daylight);
        if (float.TryParse(Environment.GetEnvironmentVariable("GAME1_ZOOM"), out float z)) _raid.Camera.TargetZoom = _raid.Camera.Zoom = z;
        if (_bot) _raid.BotMode = true;
        _paused = false; _state = GameState.Raid;
        Window.Title = $"TopDown Raid — {map.Name}";
    }

    private static readonly bool Log = Environment.GetEnvironmentVariable("GAME1_LOG") == "1";

    private void EndRaid(RaidOutcome outcome)
    {
        if (_raid == null) return;
        var r = _raid; var p = r.Player;
        if (Log) Console.WriteLine($"[raid] {r.Map.Name} {outcome} t={r.Time:0.0}s kills={r.KillsThisRaid} gold={r.GoldThisRaid} hp={p.Health:0} armor={p.Armor:0} weapons={p.Weapons.Count} bagItems={p.Inventory.NonEmpty().Count()}");
        _summary.Outcome = outcome; _summary.MapName = r.Map.Name; _summary.Kills = r.KillsThisRaid; _summary.Gold = r.GoldThisRaid; _summary.Duration = r.Time;
        _summary.Brought.Clear(); _summary.Weapons.Clear();
        if (outcome == RaidOutcome.Extracted)
        {
            var guns = new List<WeaponLoadout>();
            foreach (var w in p.Weapons)
            {
                if (w.Def.GunItem is not { } gi) continue;
                var wl = new WeaponLoadout(gi); foreach (var kv in w.Attachments) wl.Attachments[kv.Key] = kv.Value; guns.Add(wl);
                _summary.Weapons.Add(gi); foreach (var a in w.Attachments.Values) _summary.Brought.Add(new ItemStack(a, 1));
            }
            if (p.Helmet is { } hm) _summary.Brought.Add(new ItemStack(hm, 1));
            if (p.Vest is { } vs) _summary.Brought.Add(new ItemStack(vs, 1));
            _summary.Brought.AddRange(p.Inventory.NonEmpty());
            _profile.ReturnFromRaid(guns, p.Helmet, p.Vest, p.Inventory, p.Gold, r.KillsThisRaid);
        }
        else _profile.LoseRaid(r.KillsThisRaid);
        if (!_noSave) _profile.Save();
        if (_inventoryScreen.IsOpen) _inventoryScreen.Close(p);
        _raid = null; _state = GameState.Summary;
        Window.Title = "TopDown Raid";
    }

    // ================================================================================================= update
    protected override void Update(GameTime gameTime)
    {
        float dt = MathF.Min((float)gameTime.ElapsedGameTime.TotalSeconds, 1f / 20f);
        var vp = GraphicsDevice.Viewport;
        _input.Update(IsActive, vp.Bounds);
        _time += dt; _runTime += dt;
        if (_autoShot != null && _runTime > _shotDelay && !_shotRequested && _shotPath == null) { _shotRequested = true; _shotPath = _autoShot; }
        HandleDebugKeys();

        switch (_state)
        {
            case GameState.Menu:
                IsMouseVisible = true;
                switch (_menu.Update(_input, vp.Width, vp.Height))
                {
                    case MenuScreen.Action.Continue: _state = GameState.Stash; break;
                    case MenuScreen.Action.Quit: if (!_noSave) _profile.Save(); Exit(); break;
                }
                break;
            case GameState.Stash:
                IsMouseVisible = true;
                switch (_stash.Update(_input, _profile, vp.Width, vp.Height))
                {
                    case StashScreen.Action.Deploy: StartRaid(); break;
                    case StashScreen.Action.ChooseMap: _state = GameState.MapSelect; break;
                    case StashScreen.Action.Back: if (!_noSave) _profile.Save(); _state = GameState.Menu; break;
                }
                break;
            case GameState.MapSelect:
                IsMouseVisible = true;
                switch (_mapSelect.Update(_input, _profile, vp.Width, vp.Height))
                {
                    case MapSelectScreen.Action.Confirm: StartRaid(); break;
                    case MapSelectScreen.Action.Back: _state = GameState.Stash; break;
                }
                break;
            case GameState.Summary:
                IsMouseVisible = true;
                if (_summary.Update(_input, vp.Width, vp.Height) == SummaryScreen.Action.Continue) _state = GameState.Stash;
                break;
            case GameState.Raid:
                UpdateRaid(dt, vp);
                break;
        }

        _frames++; _fpsTimer += gameTime.ElapsedGameTime.TotalSeconds;
        if (_fpsTimer >= 0.5) { _fps = (int)Math.Round(_frames / _fpsTimer); _frames = 0; _fpsTimer = 0; }
        base.Update(gameTime);
    }

    private void UpdateRaid(float dt, Viewport vp)
    {
        var raid = _raid!; var player = raid.Player;
        IsMouseVisible = !IsActive || player.InventoryOpen || !player.IsAlive || _paused || _pipeline.View != RenderPipeline.DebugView.Final;
        if (_input.Pressed(Keys.Escape) && !_inventoryScreen.IsOpen) { EndRaid(RaidOutcome.TimedOut); return; }   // abandon = MIA
        if (_input.Pressed(Keys.Space)) _paused = !_paused;

        if (_uiDemo != null && !_uiDemoDone && _runTime > 1.0)
        {
            _uiDemoDone = true;
            player.Inventory.Add(ItemType.GunSmg, 1); player.Inventory.Add(ItemType.Medkit, 1); player.Inventory.Add(ItemType.SmgMag, 3);
            if (_uiDemo == "loot")
            {
                var bag = new Inventory(); bag.Add(ItemType.GunShotgun, 1); bag.Add(ItemType.Shells, 2); bag.Add(ItemType.PistolMag, 3); bag.Add(ItemType.Coin, 7); bag.Add(ItemType.ArmorPlate, 1);
                player.OpenLoot = new LootSource { Title = "BODY: GUNNER", Items = bag, Position = player.Position };
                _inventoryScreen.OpenWith(player, player.OpenLoot);
            }
            else _inventoryScreen.Open(player);
        }

        if (!_paused) _inventoryScreen.Update(_input, player, raid.Ctx, vp.Width, vp.Height);
        raid.Update(dt, _input, new Vector2(vp.Width, vp.Height), _paused);
        if (Log && (int)(raid.Time * 2) != (int)((raid.Time - dt) * 2) && (int)(raid.Time * 2) % 10 == 0)
            Console.WriteLine($"[raid] t={raid.Time:0}s hp={player.Health:0} armor={player.Armor:0} kills={raid.KillsThisRaid} enemies={raid.Enemies.Alive.Count} bullets={raid.Projectiles.ActiveCount} particles={_particles.LiveCount} zoom={raid.Camera.Zoom:0.00} cam={raid.Camera.Position.X:0},{raid.Camera.Position.Y:0}");
        if (!_paused) _particles.Update(dt);
        if (raid.Outcome != RaidOutcome.None) { EndRaid(raid.Outcome); return; }

        _hud.DebugLine = $"{_fps} FPS  P{_particles.LiveCount}  B{raid.Projectiles.ActiveCount}  E{raid.Enemies.Alive.Count}  " +
                         $"{(_pipeline.SinglePassLights ? "1PASS" : "NPASS")} {(_pipeline.BloomEnabled ? "BLOOM" : "")} {_pipeline.View}{(_paused ? "  PAUSED" : "")}";
    }

    private void HandleDebugKeys()
    {
        if (_input.Pressed(Keys.F1)) _pipeline.WireframeParticles = !_pipeline.WireframeParticles;
        if (_input.Pressed(Keys.F2)) _pipeline.ShowScissor = !_pipeline.ShowScissor;
        if (_input.Pressed(Keys.F3)) _pipeline.BloomEnabled = !_pipeline.BloomEnabled;
        if (_input.Pressed(Keys.F4)) _pipeline.SinglePassLights = !_pipeline.SinglePassLights;
        for (int i = 0; i < 6; i++) if (_input.Pressed(Keys.F5 + i)) _pipeline.View = (RenderPipeline.DebugView)(i + 1);
        if (_input.Pressed(Keys.F11)) _hud.ShowDebug = !_hud.ShowDebug;
        if (_input.Pressed(Keys.F12)) { _shotRequested = true; _shotPath = $"screenshot_{DateTime.Now:yyyyMMdd_HHmmss}.png"; }
    }

    // ================================================================================================= draw
    protected override void Draw(GameTime gameTime)
    {
        var pp = GraphicsDevice.PresentationParameters; int w = pp.BackBufferWidth, h = pp.BackBufferHeight;
        if (_state == GameState.Raid && _raid != null)
        {
            var raid = _raid;
            _pipeline.Time = _time;
            _particles.BeginFrameVertices();
            raid.Projectiles.DrawTracers();
            var visible = raid.Camera.VisibleWorld;
            _pipeline.RenderFrame(raid.Camera.View, raid.Camera.Zoom,
                drawScene: batch => raid.DrawScene(batch, visible),
                lights: raid.ActiveLights(visible),
                drawEmissive: p => _particles.Draw(p.CurrentView, p.Width, p.Height, p.States.AdditiveLight, p.WireframeParticles ? p.States.Wireframe : p.States.SolidNoCull),
                drawOverlay: sb =>
                {
                    if (_pipeline.View != RenderPipeline.DebugView.Final) return;
                    _hud.Draw(sb, raid.Ctx, _input.MouseScreen, _input.MouseInWindow, w, h, raid);
                    sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
                    _inventoryScreen.Draw(sb, raid.Player, w, h);
                    sb.End();
                });
        }
        else
        {
            GraphicsDevice.SetRenderTarget(null);
            GraphicsDevice.Clear(new Color(6, 7, 9));
            var sb = _pipeline.SpriteBatch;
            sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            switch (_state)
            {
                case GameState.Menu: _menu.Draw(sb, _profile, _input.MouseScreen, w, h, _time); break;
                case GameState.Stash: _stash.Draw(sb, _profile, _input.MouseScreen, w, h, _time); break;
                case GameState.MapSelect: _mapSelect.Draw(sb, _profile, _input.MouseScreen, w, h, _time); break;
                case GameState.Summary: _summary.Draw(sb, _input.MouseScreen, w, h, _time); break;
            }
            sb.End();
        }

        base.Draw(gameTime);
        if (_shotRequested && _shotPath != null)
        {
            _pipeline.SaveScreenshot(_shotPath); _shotRequested = false;
            if (_autoShot != null) Exit();
        }
    }

    protected override void OnExiting(object sender, ExitingEventArgs args)
    {
        if (!_noSave && _profile != null) _profile.Save();
        base.OnExiting(sender, args);
    }

    protected override void UnloadContent()
    {
        _particles.Dispose(); _pipeline.Dispose();
        base.UnloadContent();
    }
}
