using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

namespace CharacterModels;

public class Game1 : Game
{
    private readonly GraphicsDeviceManager _graphics;
    private SpriteBatch _spriteBatch = null!;
    private SpriteFont _font = null!;
    private Effect _effect = null!;
    private Effect _deferredFx = null!;
    private DeferredRenderer _deferred = null!;
    private bool _useDeferred = true;
    private static readonly (Vector3 pos, Vector3 color, float radius, float intensity, float flicker)[] Lamps =
    {
        (new Vector3(-3.4f, 1.35f, 1.7f), new Vector3(1.0f, 0.55f, 0.22f), 5.5f, 5.0f, 0.25f),
        (new Vector3(3.4f, 1.35f, 1.7f), new Vector3(0.3f, 0.5f, 1.0f), 5.5f, 4.0f, 0f),
        (new Vector3(0f, 1.9f, -2.8f), new Vector3(0.9f, 0.3f, 0.8f), 6f, 4.0f, 0f),
        (new Vector3(-1.2f, 0.45f, 3.2f), new Vector3(0.3f, 1.0f, 0.45f), 3.5f, 3.0f, 0f)
    };
    private Texture2D _grain = null!;
    private RenderTarget2D _shadowMap = null!;
    private const int ShadowSize = 2048;

    private readonly List<Character> _characters = new();
    private readonly List<Tree> _trees = new();
    private readonly Wind _wind = new();
    private Weather _weather = null!;
    private readonly List<(Vector3 pos, float height, Color[] colors)> _leafSources = new();
    private static readonly float[] WindPresets = { 0f, 0.35f, 0.7f, 1.3f };
    private int _windPreset = 2;
    private VertexBuffer _groundVb = null!;
    private IndexBuffer _groundIb = null!;
    private readonly Matrix[] _identityPalette = { Matrix.Identity };

    // Camera
    private float _camYaw = 0.0f, _camPitch = MathHelper.ToRadians(10), _camDist = 6.0f;
    private Vector3 _camTarget = new(0, 0.95f, 0);
    private Vector3 _camTargetGoal = new(0, 0.95f, 0);
    private float _camDistGoal = 6.0f;
    private bool _autoOrbit = true;
    private int _focus = -1;

    // Lighting
    private float _lightYaw = MathHelper.ToRadians(35);
    private bool _wireframe;
    private bool _varied;
    private int _clipIndex = 1;

    private RenderTarget2D? _shotTarget;
    private int _frame;
    // Play mode (focused character is player-controlled)
    private Vector3 _moveVel;
    private const float WalkSpeed = 1.6f, RunSpeed = 4.4f;

    private KeyboardState _prevKeys;
    private MouseState _prevMouse;
    private float _time;
    private float _wobMinX = 9, _wobMaxX = -9, _wobMinZ = 9, _wobMaxZ = -9;
    private readonly Vector3 _fogColorLinear = new(0.045f, 0.05f, 0.07f);

    public Game1()
    {
        _graphics = new GraphicsDeviceManager(this)
        {
            PreferredBackBufferWidth = 1600,
            PreferredBackBufferHeight = 900,
            GraphicsProfile = GraphicsProfile.HiDef,
            PreferMultiSampling = true,
            SynchronizeWithVerticalRetrace = true
        };
        _graphics.PreparingDeviceSettings += (_, e) => e.GraphicsDeviceInformation.PresentationParameters.MultiSampleCount = 8;
        Content.RootDirectory = "Content";
        IsMouseVisible = true;
        Window.Title = "MonoGame Procedural Character Models";
        Window.AllowUserResizing = true;
    }

    protected override void LoadContent()
    {
        _spriteBatch = new SpriteBatch(GraphicsDevice);
        _font = Content.Load<SpriteFont>("Font");
        _effect = Content.Load<Effect>("Character");
        _deferredFx = Content.Load<Effect>("Deferred");
        _deferred = new DeferredRenderer(GraphicsDevice, _deferredFx);
        foreach (var (pos, color, radius, intensity, flicker) in Lamps)
            _deferred.Lights.Add(new PointLight { Position = pos, Color = color, Radius = radius, Intensity = intensity, Flicker = flicker });
        _grain = BuildGrainTexture(256);
        _shadowMap = new RenderTarget2D(GraphicsDevice, ShadowSize, ShadowSize, false, SurfaceFormat.Single, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);

        var specs = Roster.Create();
        float spacing = 1.25f;
        for (int i = 0; i < specs.Count; i++)
        {
            var c = CharacterBuilder.Build(GraphicsDevice, specs[i]);
            c.Position = new Vector3((i - (specs.Count - 1) * 0.5f) * spacing, 0, 0);
            c.Player.TimeOffset = i * 1.7f;
            c.Locomotion = Clips.Idle;
            _characters.Add(c);
        }
        BuildGround();
        PlantTrees((int)Program.Opt("trees", 22), (int)Program.Opt("seed", 7));
        _wind.Strength = Program.Opt("wind", WindPresets[_windPreset]);
        _weather = new Weather(GraphicsDevice) { Raining = Program.Flag("rain"), RainDensity = Program.Opt("rain", 1f) };
        foreach (var t in _trees) if (t.LeafColors.Length > 0) _leafSources.Add((t.Position, t.CrownHeight, t.LeafColors));

        // The mage's orb is a light: follow the staff tip through the weapon bone (works sheathed or drawn).
        foreach (var c in _characters)
        {
            if (c.Spec.Weapon != Weapon.Staff) continue;
            float s = c.Spec.Height / 1.8f;
            var orbLocal = new Vector3(0, 1.99f - 0.85f, 0.05f) * s;
            var mage = c;
            _deferred.Lights.Add(new PointLight
            {
                Color = new Vector3(0.45f, 0.8f, 1.0f), Radius = 3.2f, Intensity = 5f, Flicker = 0.12f,
                Follow = () => Vector3.Transform(orbLocal, mage.Skeleton["weaponR"].World * mage.World)
            });
        }
        if (Program.Flag("forward")) _useDeferred = false;

        // Startup options (for scripted screenshots / demos).
        _camYaw = MathHelper.ToRadians(Program.Opt("yaw", 0));
        _camPitch = MathHelper.ToRadians(Program.Opt("pitch", 10));
        _camDist = _camDistGoal = Program.Opt("dist", 6);
        _lightYaw = MathHelper.ToRadians(Program.Opt("light", 35));
        if (Program.Flag("no-orbit")) _autoOrbit = false;
        int focus = (int)Program.Opt("focus", -1);
        if (focus >= 0 && focus < _characters.Count) FocusOn(focus);
        int clip = (int)Program.Opt("clip", 1);
        if (clip >= 0 && clip < Clips.All.Count) _clipIndex = clip;
        _varied = Program.Flag("varied");
        ApplyClips();
        if (Program.Options.TryGetValue("export", out var exportDir))
            foreach (var c in _characters) c.ExportObj(System.IO.Path.Combine(exportDir, c.Spec.Name + ".obj"));
        float warm = Program.Opt("warm", 0);
        if (Program.Flag("drawn")) foreach (var c in _characters) { c.Drawn = true; c.DrawBlend = 1; }
        float drawAt = Program.Opt("draw", -1);
        for (float t = 0; t < warm; t += 1f / 60f)
        {
            if (drawAt >= 0 && t >= drawAt) { foreach (var c in _characters) c.ToggleWeapon(); drawAt = -1; }
            foreach (var c in _characters) c.Update(1f / 60f);
            _time += 1f / 60f;
            if (_focus >= 0 && Program.Flag("walk") && _trees.Count > 0)
            {
                // Headless collision test: run the focused character straight at the nearest tree.
                var pc = _characters[_focus];
                Tree? nearest = null; float best = float.MaxValue;
                foreach (var tr in _trees) { float d = Vector3.DistanceSquared(tr.Position, pc.Position); if (d < best) { best = d; nearest = tr; } }
                var dir = Vector3.Normalize(nearest!.Position - pc.Position);
                float ws = Program.Opt("walk", RunSpeed); if (ws <= 1) ws = RunSpeed;
                _moveVel = dir * ws;
                pc.Speed = ws; pc.Locomotion = pc.Move;
                pc.Position += _moveVel / 60f;
                ResolveCollisions(pc);
                pc.Yaw = MathF.Atan2(dir.X, dir.Z);
                _camTarget = _camTargetGoal = pc.Position + new Vector3(0, pc.Spec.Height * 0.55f, 0);
                // Wobble metric: head offset from the hips in the character's own frame (x = lateral, z = forward), after t > 1 s.
                var hips = pc.Skeleton["hips"].World.Translation; var head = pc.Skeleton["head"].World.Translation;
                var rel = Vector3.TransformNormal(head - hips, Matrix.CreateRotationY(-pc.Yaw));
                if (t > 1f) { _wobMinX = MathF.Min(_wobMinX, rel.X); _wobMaxX = MathF.Max(_wobMaxX, rel.X); _wobMinZ = MathF.Min(_wobMinZ, rel.Z); _wobMaxZ = MathF.Max(_wobMaxZ, rel.Z); }
                if (t + 1f / 60f >= warm)
                    Console.Error.WriteLine($"walk test: distance to tree {MathF.Sqrt(Vector3.DistanceSquared(nearest.Position, pc.Position)):0.000} (trunk r {nearest.Radius:0.000} + 0.28)   head sway lateral {(_wobMaxX - _wobMinX) * 100:0.0} cm  fore/aft {(_wobMaxZ - _wobMinZ) * 100:0.0} cm");
            }
        }
        foreach (var t in _trees) t.Update(_time, _wind);
        for (float t = 0; t < MathF.Min(warm, 4f); t += 1f / 30f) _weather.Update(1f / 30f, _camTarget, _wind, _leafSources);
    }

    /// <summary>Plants a ring of mixed-style trees around the plaza with a minimum spacing; --trees 0 disables.</summary>
    private void PlantTrees(int count, int seed)
    {
        var rnd = new Random(seed);
        var placed = new List<Vector3>();
        var styles = (TreeStyle[])Enum.GetValues(typeof(TreeStyle));
        if (Program.Flag("gallery"))
        {
            // One of each style in a row behind the characters (README / inspection shots).
            for (int i = 0; i < styles.Length; i++)
            {
                var t = TreeBuilder.Build(GraphicsDevice, styles[i], seed * 131 + i, 1f);
                t.Position = new Vector3((i - (styles.Length - 1) * 0.5f) * 3.2f, 0, -3.5f);
                _trees.Add(t);
            }
            return;
        }
        int tries = 0;
        while (_trees.Count < count && tries++ < count * 40)
        {
            float ang = (float)rnd.NextDouble() * MathHelper.TwoPi;
            float rad = 6.2f + (float)rnd.NextDouble() * 6.5f;
            var pos = new Vector3(MathF.Cos(ang) * rad, 0, MathF.Sin(ang) * rad);
            if (MathF.Abs(pos.X) > 13f || MathF.Abs(pos.Z) > 13f) continue;
            bool ok = true;
            foreach (var q in placed) if (Vector3.DistanceSquared(q, pos) < 2.6f * 2.6f) { ok = false; break; }
            if (!ok) continue;
            // Cycle styles so every kind appears; palms and dead trees are rarer.
            var style = styles[_trees.Count % styles.Length];
            if ((style == TreeStyle.Palm || style == TreeStyle.Dead) && rnd.NextDouble() < 0.4) style = TreeStyle.Oak;
            var t = TreeBuilder.Build(GraphicsDevice, style, seed * 131 + _trees.Count, 0.85f + (float)rnd.NextDouble() * 0.4f);
            t.Position = pos; t.Yaw = (float)rnd.NextDouble() * MathHelper.TwoPi;
            _trees.Add(t); placed.Add(pos);
        }
    }

    private void FocusOn(int index)
    {
        _focus = index;
        if (_focus < 0) { _camTargetGoal = new Vector3(0, 0.95f, 0); _camDistGoal = 6; }
        else { var c = _characters[_focus]; _camTargetGoal = c.Position + new Vector3(0, c.Spec.Height * Program.Opt("ty", 0.55f), 0); _camDistGoal = Program.Opt("dist", 2.6f); }
        _camTarget = _camTargetGoal; _camDist = _camDistGoal;
    }

    private Texture2D BuildGrainTexture(int size)
    {
        var tex = new Texture2D(GraphicsDevice, size, size, true, SurfaceFormat.Color);
        var rnd = new Random(1234);
        // Value noise with a few octaves, tileable via modulo lattice.
        float[] Lattice(int n)
        {
            var l = new float[n * n];
            for (int i = 0; i < l.Length; i++) l[i] = (float)rnd.NextDouble();
            return l;
        }
        var oct = new[] { (Lattice(8), 8, 0.5f), (Lattice(16), 16, 0.3f), (Lattice(32), 32, 0.2f) };
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float v = 0;
            foreach (var (l, n, amp) in oct)
            {
                float fx = x / (float)size * n, fy = y / (float)size * n;
                int x0 = (int)fx, y0 = (int)fy; float tx = fx - x0, ty = fy - y0;
                tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
                float a = l[(y0 % n) * n + x0 % n], b = l[(y0 % n) * n + (x0 + 1) % n];
                float c = l[((y0 + 1) % n) * n + x0 % n], d = l[((y0 + 1) % n) * n + (x0 + 1) % n];
                v += MathHelper.Lerp(MathHelper.Lerp(a, b, tx), MathHelper.Lerp(c, d, tx), ty) * amp;
            }
            v += (float)(rnd.NextDouble() - 0.5) * 0.15f;
            byte g = (byte)MathHelper.Clamp(v * 255f, 0, 255);
            data[y * size + x] = new Color(g, g, g, (byte)255);
        }
        tex.SetData(0, null, data, 0, data.Length);
        return tex;
    }

    private void BuildGround()
    {
        var sk = new Skeleton();
        sk.Add("root", null, Vector3.Zero, Vector3.Up);
        var w = Weighter.Fixed(sk, "root");
        var mb = new MeshBuilder();
        const int half = 14;
        var a = new Color(92, 90, 96); var b = new Color(78, 76, 82);
        var grass = new Random(3);
        for (int z = -half; z < half; z++)
        for (int x = -half; x < half; x++)
        {
            float cx = x + 0.5f, cz = z + 0.5f;
            float r = MathF.Sqrt(cx * cx + cz * cz);
            if (r < 5.2f)
                mb.Box(new Vector3(cx, -0.02f, cz), new Vector3(0.985f, 0.04f, 0.985f), ((x + z) & 1) == 0 ? a : b, new Vector2(0.25f, 0.3f), w);
            else
            {
                // Grass: a low, slightly uneven slab with hue variation, and a ring of kerb stones at the plaza rim.
                int v = grass.Next(-9, 10);
                var g = r < 5.9f ? new Color(84, 80, 78) : new Color(58 + v, 92 + v + grass.Next(-6, 7), 40 + v);
                float h = r < 5.9f ? 0.05f : 0.03f + (float)grass.NextDouble() * 0.025f;
                mb.Box(new Vector3(cx, h * 0.5f - 0.02f, cz), new Vector3(1.0f, h, 1.0f), g, r < 5.9f ? new Vector2(0.2f, 0.3f) : Mat.Cloth, w);
            }
        }
        foreach (var c in _characters)
        {
            var col = new Color(88, 86, 94);
            mb.Loft(new[]
            {
                new Ring(c.Position + new Vector3(0, 0.0f, 0), 0.5f, col, new Vector2(0.4f, 0.4f)) { Tangent = Vector3.Up },
                new Ring(c.Position + new Vector3(0, 0.025f, 0), 0.5f, col, new Vector2(0.4f, 0.4f)) { Tangent = Vector3.Up },
                new Ring(c.Position + new Vector3(0, 0.03f, 0), 0.47f, col, new Vector2(0.4f, 0.4f)) { Tangent = Vector3.Up },
                new Ring(c.Position + new Vector3(0, 0.03f, 0), 0.0f, col, new Vector2(0.4f, 0.4f)) { Tangent = Vector3.Up }
            }, 40, w, Vector3.Backward);
        }
        foreach (var (pos, color, _, _, _) in Lamps)
        {
            var pole = new Color(40, 38, 42);
            mb.Loft(new[]
            {
                new Ring(new Vector3(pos.X, 0, pos.Z), 0.035f, pole, new Vector2(0.4f, 0.5f)),
                new Ring(new Vector3(pos.X, pos.Y - 0.12f, pos.Z), 0.025f, pole, new Vector2(0.4f, 0.5f))
            }, 10, w, Vector3.Backward, capEnd: true, capSteps: 2);
            var c = new Color(color.X, color.Y, color.Z);
            mb.Ellipsoid(pos, new Vector3(0.09f), 14, 10, c, Mat.Glow, w);
        }
        (_groundVb, _groundIb) = mb.Upload(GraphicsDevice);
    }

    // ------------------------------------------------------------------ update

    protected override void Update(GameTime gameTime)
    {
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt;
        var keys = Keyboard.GetState();
        var mouse = Mouse.GetState();
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);

        if (keys.IsKeyDown(Keys.Escape)) Exit();
        if (Pressed(Keys.Space)) _autoOrbit = !_autoOrbit;
        if (Pressed(Keys.G)) _wireframe = !_wireframe;
        if (Pressed(Keys.B)) _useDeferred = !_useDeferred;
        if (Pressed(Keys.N)) _weather.Raining = !_weather.Raining;
        if (Pressed(Keys.T)) { _windPreset = (_windPreset + 1) % WindPresets.Length; _wind.Strength = WindPresets[_windPreset]; }
        if (Pressed(Keys.V)) { _varied = !_varied; ApplyClips(); }
        if (Pressed(Keys.R)) { _focus = -1; _camYaw = 0; _autoOrbit = false; _camTargetGoal = new Vector3(0, 0.95f, 0); _camDistGoal = 6; _camPitch = MathHelper.ToRadians(10); }
        if (Pressed(Keys.F) || Pressed(Keys.Tab))
        {
            int next = (_focus + 2) % (_characters.Count + 1) - 1;
            if (_focus >= 0) { _characters[_focus].Speed = 0; _characters[_focus].Locomotion = Clips.Idle; }
            _focus = next; _moveVel = Vector3.Zero;
            if (_focus < 0) { _camTargetGoal = new Vector3(0, 0.95f, 0); _camDistGoal = 6; }
            else { var c = _characters[_focus]; _camTargetGoal = c.Position + new Vector3(0, c.Spec.Height * 0.55f, 0); _camDistGoal = 2.6f; }
        }
        for (int i = 0; i < Clips.All.Count && i < 9; i++)
            if (Pressed(Keys.D1 + i)) { _clipIndex = i; _varied = false; ApplyClips(); }
        if (keys.IsKeyDown(Keys.L)) _lightYaw += dt * 1.2f;
        if (keys.IsKeyDown(Keys.K)) _lightYaw -= dt * 1.2f;

        // Camera control
        float orbitSpeed = 1.5f * dt;
        if (keys.IsKeyDown(Keys.Left)) _camYaw -= orbitSpeed;
        if (keys.IsKeyDown(Keys.Right)) _camYaw += orbitSpeed;
        if (keys.IsKeyDown(Keys.Up)) _camPitch += orbitSpeed * 0.6f;
        if (keys.IsKeyDown(Keys.Down)) _camPitch -= orbitSpeed * 0.6f;
        if (IsActive && mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Pressed)
        {
            _camYaw += (mouse.X - _prevMouse.X) * 0.006f;
            _camPitch += (mouse.Y - _prevMouse.Y) * 0.004f;
            _autoOrbit = false;
        }
        if (IsActive && mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Pressed)
        {
            var view = Matrix.CreateRotationY(-_camYaw);
            var right = Vector3.TransformNormal(Vector3.Right, Matrix.Invert(view));
            _camTargetGoal += (-right * (mouse.X - _prevMouse.X) + Vector3.Up * (mouse.Y - _prevMouse.Y)) * 0.0025f * _camDist;
        }
        int wheel = mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;
        if (wheel != 0) _camDistGoal = MathHelper.Clamp(_camDistGoal * MathF.Pow(0.9f, wheel / 120f), 1.2f, 15f);
        if (_autoOrbit) _camYaw += dt * 0.25f;
        _camPitch = MathHelper.Clamp(_camPitch, MathHelper.ToRadians(-15), MathHelper.ToRadians(80));
        _camDist = MathHelper.Lerp(_camDist, _camDistGoal, 1 - MathF.Exp(-dt * 6));
        _camTarget = Vector3.Lerp(_camTarget, _camTargetGoal, 1 - MathF.Exp(-dt * 6));

        if (_focus >= 0) UpdatePlayer(dt, keys);
        foreach (var c in _characters) c.Update(dt);
        foreach (var t in _trees) t.Update(_time, _wind);
        _weather.Update(dt, _camTarget, _wind, _leafSources);

        _prevKeys = keys; _prevMouse = mouse;
        base.Update(gameTime);
    }

    /// <summary>Third-person control of the focused character: camera-relative WASD, Shift to run, Q/E/X actions.</summary>
    private void UpdatePlayer(float dt, KeyboardState keys)
    {
        var c = _characters[_focus];
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);

        // Camera-relative movement basis on the ground plane.
        var fwd = new Vector3(-MathF.Sin(_camYaw), 0, -MathF.Cos(_camYaw));
        var right = new Vector3(-fwd.Z, 0, fwd.X);
        var input = Vector3.Zero;
        if (keys.IsKeyDown(Keys.W) || Program.Flag("walk")) input += fwd;   // --walk: headless test holds W + Shift
        if (keys.IsKeyDown(Keys.S)) input -= fwd;
        if (keys.IsKeyDown(Keys.D)) input += right;
        if (keys.IsKeyDown(Keys.A)) input -= right;
        bool moving = input.LengthSquared() > 0.01f;
        if (moving) input.Normalize();

        if (Pressed(Keys.Q))
        {
            if (c.HasWeapon && !c.Drawn && !c.Busy) { c.ToggleWeapon(); c.Queued = Clips.Attack; }
            else if (!c.Busy || c.Action == Clips.Attack) c.PlayAction(Clips.Attack);
        }
        if (Pressed(Keys.E) && !c.Busy) c.PlayAction(Clips.Wave);
        if (Pressed(Keys.X)) { if (c.Action == Clips.Dance) c.CancelAction(); else if (!c.Busy) c.PlayAction(Clips.Dance); }
        if (Pressed(Keys.H)) c.ToggleWeapon();
        if (moving && c.Action != null && c.Action.Name != "Draw") c.CancelAction();

        bool run = keys.IsKeyDown(Keys.LeftShift) || keys.IsKeyDown(Keys.RightShift) || Program.Flag("walk");
        float walkOpt = Program.Opt("walk", 0);
        var targetVel = moving ? input * (walkOpt > 1 ? walkOpt : run ? RunSpeed : WalkSpeed) : Vector3.Zero;
        float accel = moving ? 7f : 10f;
        _moveVel = Vector3.Lerp(_moveVel, targetVel, 1 - MathF.Exp(-dt * accel));
        float speed = _moveVel.Length();

        if (moving)
        {
            // Turn toward the input direction (not the smoothed velocity, which lags and makes the body hunt).
            float targetYaw = MathF.Atan2(input.X, input.Z);
            float delta = MathHelper.WrapAngle(targetYaw - c.Yaw);
            float maxTurn = MathHelper.ToRadians(540f) * dt;
            c.Yaw += MathHelper.Clamp(delta * (1 - MathF.Exp(-dt * 9f)), -maxTurn, maxTurn);
        }
        c.Position += _moveVel * dt;
        ResolveCollisions(c);
        c.Position.X = MathHelper.Clamp(c.Position.X, -13f, 13f);
        c.Position.Z = MathHelper.Clamp(c.Position.Z, -13f, 13f);

        // Speed-driven locomotion: one clip, one stride phase; no idle/walk/run cross-fades.
        c.Speed = speed;
        c.Locomotion = c.Move;

        // Camera follows.
        _camTargetGoal = c.Position + new Vector3(0, c.Spec.Height * 0.55f, 0);
        _autoOrbit = false;
    }

    /// <summary>Circle-vs-circle push-out against tree trunks, lamp posts and the other characters (two passes so corners settle).</summary>
    private void ResolveCollisions(Character c)
    {
        const float selfR = 0.28f;
        for (int pass = 0; pass < 2; pass++)
        {
            foreach (var t in _trees) PushOut(ref c.Position, t.Position, selfR + t.Radius);
            foreach (var (pos, _, _, _, _) in Lamps) PushOut(ref c.Position, new Vector3(pos.X, 0, pos.Z), selfR + 0.07f);
            foreach (var o in _characters) if (o != c) PushOut(ref c.Position, o.Position, selfR + 0.3f);
        }
    }

    private void PushOut(ref Vector3 p, Vector3 centre, float minDist)
    {
        float dx = p.X - centre.X, dz = p.Z - centre.Z;
        float d2 = dx * dx + dz * dz;
        if (d2 >= minDist * minDist) return;
        float d = MathF.Sqrt(d2);
        if (d < 1e-4f) { dx = 1; dz = 0; d = 1; }
        p.X = centre.X + dx / d * minDist;
        p.Z = centre.Z + dz / d * minDist;
        // Kill the velocity component into the obstacle so the character slides along it.
        var n = new Vector3(dx / d, 0, dz / d);
        float into = Vector3.Dot(_moveVel, n);
        if (into < 0) _moveVel -= n * into;
    }

    private void ApplyClips()
    {
        for (int i = 0; i < _characters.Count; i++)
        {
            var clip = _varied ? Clips.All[1 + (i + 1) % (Clips.All.Count - 1)] : Clips.All[_clipIndex];
            var c = _characters[i];
            c.CancelAction(); c.Speed = 0;
            if (clip.Duration > 0) { c.Locomotion = Clips.Idle; c.PlayAction(clip); if (clip == Clips.Attack) { c.Drawn = true; } }
            else c.Locomotion = clip;
        }
    }

    // -------------------------------------------------------------------- draw

    private static Vector3 ToneMap(Vector3 c)
    {
        c = new Vector3(1 - MathF.Exp(-c.X * 1.5f), 1 - MathF.Exp(-c.Y * 1.5f), 1 - MathF.Exp(-c.Z * 1.5f));
        return new Vector3(MathF.Pow(c.X, 1 / 2.2f), MathF.Pow(c.Y, 1 / 2.2f), MathF.Pow(c.Z, 1 / 2.2f));
    }

    protected override void Draw(GameTime gameTime)
    {
        var gd = GraphicsDevice;
        var pp = gd.PresentationParameters;
        float aspect = pp.BackBufferWidth / (float)pp.BackBufferHeight;

        // Camera matrices
        var camOffset = Vector3.Transform(new Vector3(0, 0, _camDist), Matrix.CreateRotationX(-_camPitch) * Matrix.CreateRotationY(_camYaw));
        var camPos = _camTarget + camOffset;
        var view = Matrix.CreateLookAt(camPos, _camTarget, Vector3.Up);
        var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(38), aspect, 0.1f, 80f);

        // Light
        var lightDir = Vector3.Normalize(Vector3.Transform(new Vector3(0, -0.85f, -0.6f), Matrix.CreateRotationY(_lightYaw)));
        var sceneCenter = new Vector3(0, 0.9f, 0);
        var lightView = Matrix.CreateLookAt(sceneCenter - lightDir * 18f, sceneCenter, Vector3.Up);
        float shadowExtent = _trees.Count > 0 ? 20f : 8.5f;
        var lightProj = Matrix.CreateOrthographic(shadowExtent, shadowExtent, 1f, 36f);
        var lightVp = lightView * lightProj;

        // ---- Shadow pass
        gd.SetRenderTarget(_shadowMap);
        gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1f, 0);
        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState = RasterizerState.CullNone;
        gd.BlendState = BlendState.Opaque;
        _effect.CurrentTechnique = _effect.Techniques["ShadowCaster"];
        _effect.Parameters["LightViewProjection"].SetValue(lightVp);
        DrawScene(shadowPass: true);

        // ---- Main pass
        string? shot = Program.Options.TryGetValue("shot", out var sp) ? sp : null;
        if (shot != null && _shotTarget == null)
            _shotTarget = new RenderTarget2D(gd, pp.BackBufferWidth, pp.BackBufferHeight, false, SurfaceFormat.Color, DepthFormat.Depth24, 8, RenderTargetUsage.DiscardContents);
        var lighting = new SceneLighting
        {
            LightDirection = lightDir, LightColor = new Vector3(1.05f, 0.98f, 0.88f) * 1.7f,
            FillDirection = Vector3.Normalize(new Vector3(0.8f, -0.15f, 0.35f)), FillColor = new Vector3(0.16f, 0.18f, 0.24f),
            SkyColor = new Vector3(0.20f, 0.22f, 0.28f), GroundColor = new Vector3(0.10f, 0.085f, 0.07f),
            RimColor = new Vector3(0.28f, 0.32f, 0.42f), FogColor = _fogColorLinear, FogStart = 24f, FogEnd = 75f,
            LightViewProjection = lightVp, ShadowMap = _shadowMap, ShadowMapSize = ShadowSize, ShadowStrength = 0.9f
        };
        var p = _effect.Parameters;
        p["View"].SetValue(view);
        p["Projection"].SetValue(proj);
        p["GrainTexture"].SetValue(_grain);
        p["GrainStrength"].SetValue(0.35f);
        p["Time"].SetValue(_time);
        p["WindStrength"].SetValue(_wind.Strength);
        p["WindDirection"].SetValue(_wind.Direction);

        if (_useDeferred && !_wireframe)
        {
            // Dusk balance: a weaker, cooler key so the point lights carry the scene.
            lighting.LightColor = new Vector3(0.95f, 0.92f, 0.98f) * 1.0f;
            lighting.SkyColor = new Vector3(0.17f, 0.19f, 0.25f);
            lighting.GroundColor = new Vector3(0.08f, 0.07f, 0.06f);
            lighting.FillColor = new Vector3(0.10f, 0.11f, 0.15f);
            _effect.CurrentTechnique = _effect.Techniques["GBuffer"];
            _deferred.Render(pp.BackBufferWidth, pp.BackBufferHeight, _shotTarget, view, proj, camPos, lighting, _time,
                () => DrawScene(shadowPass: false));
            _weather.Draw(gd, view, proj, camPos, depthAvailable: false, ToneMap(_fogColorLinear));
            if (Program.Options.TryGetValue("debug", out var dbg))
            {
                Texture2D? t = dbg switch { "light" => _deferred.LightTarget, "normal" => _deferred.NormalTarget, "albedo" => _deferred.AlbedoTarget, _ => null };
                if (t != null)
                {
                    _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, SamplerState.PointClamp);
                    _spriteBatch.Draw(t, new Rectangle(0, 0, pp.BackBufferWidth, pp.BackBufferHeight), Color.White);
                    _spriteBatch.End();
                }
            }
            DrawHud(view, proj);
            FinishFrame(shot);
            base.Draw(gameTime);
            return;
        }

        gd.SetRenderTarget(_shotTarget);
        var fogTm = ToneMap(_fogColorLinear);
        gd.Clear(new Color(fogTm));
        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState = _wireframe ? new RasterizerState { FillMode = FillMode.WireFrame, CullMode = CullMode.None } : RasterizerState.CullCounterClockwise;
        gd.SamplerStates[0] = SamplerState.PointClamp;
        gd.SamplerStates[1] = SamplerState.LinearWrap;

        _effect.CurrentTechnique = _effect.Techniques["Skinned"];
        p["LightViewProjection"].SetValue(lightVp);
        p["LightDirection"].SetValue(lightDir);
        p["LightColor"].SetValue(new Vector3(1.05f, 0.98f, 0.88f) * 1.7f);
        p["FillDirection"].SetValue(Vector3.Normalize(new Vector3(0.8f, -0.15f, 0.35f)));
        p["FillColor"].SetValue(new Vector3(0.16f, 0.18f, 0.24f));
        p["SkyColor"].SetValue(new Vector3(0.20f, 0.22f, 0.28f));
        p["GroundColor"].SetValue(new Vector3(0.10f, 0.085f, 0.07f));
        p["CameraPosition"].SetValue(camPos);
        p["RimColor"].SetValue(new Vector3(0.28f, 0.32f, 0.42f));
        p["ShadowMap"].SetValue(_shadowMap);
        p["ShadowMapSize"].SetValue((float)ShadowSize);
        p["ShadowStrength"].SetValue(0.9f);
        p["FogStart"].SetValue(24f);
        p["FogEnd"].SetValue(75f);
        p["FogColor"].SetValue(_fogColorLinear);
        DrawScene(shadowPass: false);
        _weather.Draw(gd, view, proj, camPos, depthAvailable: true, fogTm);

        DrawHud(view, proj);
        FinishFrame(shot);
        base.Draw(gameTime);
    }

    private void FinishFrame(string? shot)
    {
        if (_shotTarget == null) return;
        GraphicsDevice.SetRenderTarget(null);
        if (++_frame >= 3)
        {
            using var fs = System.IO.File.Create(shot!);
            _shotTarget.SaveAsPng(fs, _shotTarget.Width, _shotTarget.Height);
            Exit();
        }
    }

    private void DrawScene(bool shadowPass)
    {
        var gd = GraphicsDevice;
        var p = _effect.Parameters;

        p["World"].SetValue(Matrix.Identity);
        p["Bones"].SetValue(_identityPalette);
        gd.SetVertexBuffer(_groundVb);
        gd.Indices = _groundIb;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _groundIb.IndexCount / 3);
        }

        foreach (var c in _characters) DrawSkinned(c.World, c.Skeleton.Palette, c.VertexBuffer, c.IndexBuffer);
        foreach (var t in _trees) DrawSkinned(t.World, t.Skeleton.Palette, t.VertexBuffer, t.IndexBuffer);
    }

    private void DrawSkinned(Matrix world, Matrix[] palette, VertexBuffer vb, IndexBuffer ib)
    {
        var gd = GraphicsDevice;
        _effect.Parameters["World"].SetValue(world);
        _effect.Parameters["Bones"].SetValue(palette);
        gd.SetVertexBuffer(vb);
        gd.Indices = ib;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, ib.IndexCount / 3);
        }
    }

    private void DrawHud(Matrix view, Matrix proj)
    {
        var gd = GraphicsDevice;
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);

        int tris = 0, verts = 0, treeTris = 0;
        foreach (var c in _characters) { tris += c.Triangles; verts += c.Vertices; }
        foreach (var t in _trees) treeTris += t.Triangles;

        void Text(string s, Vector2 pos, Color col, float scale = 1f)
        {
            _spriteBatch.DrawString(_font, s, pos + new Vector2(1, 1), Color.Black * 0.7f, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
            _spriteBatch.DrawString(_font, s, pos, col, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        }

        Text("MonoGame Procedural Characters", new Vector2(16, 12), Color.White, 1.3f);
        string mode = _useDeferred ? $"deferred: {_deferred.Lights.Count} point lights + shadowed key ({_deferred.LightFormat})" : "forward: key + fill, PCF shadows";
        Text($"{_characters.Count} characters  |  {tris:N0} triangles  {verts:N0} vertices  |  {_characters[0].Skeleton.Count} bones each  |  {mode}",
            new Vector2(16, 40), new Color(200, 205, 215));
        string clipName = _varied ? "varied" : Clips.All[_clipIndex].Name;
        string windName = _wind.Strength <= 0 ? "still" : _wind.Strength < 0.5f ? "calm" : _wind.Strength < 1f ? "breezy" : "gale";
        Text($"Animation: {clipName}     Focus: {(_focus < 0 ? "all" : _characters[_focus].Spec.Name)}     Trees: {_trees.Count} ({treeTris:N0} tris, skinned)   Wind: {windName} ({_wind.Strength:0.00})   Rain: {(_weather.Raining ? "on" : "off")}   Particles: {_weather.Count}",
            new Vector2(16, 62), new Color(255, 220, 150));

        var lines = _focus >= 0
            ? new[]
            {
                $"Controlling {_characters[_focus].Spec.Name}:  W A S D  move   Shift  run   H draw / sheathe weapon   Q attack   E wave   X dance",
                "F / Tab  next character (cycles back to overview)     Mouse drag / arrows  orbit   Wheel  zoom",
                "1-7 animation   T wind   N rain   B forward/deferred   L/K rotate light   G wireframe   R reset   Esc quit"
            }
            : new[]
            {
                "F / Tab  focus a character and take control of it (WASD + Shift)",
                "1-7  animation (bind, idle, walk, run, wave, attack, dance)   V  varied",
                "Mouse drag  orbit   Right drag  pan   Wheel  zoom   Arrows  orbit",
                "Space auto-orbit   T wind   N rain   B forward/deferred   L/K rotate light   G wireframe   R reset   Esc quit"
            };
        float y = gd.Viewport.Height - 16 - lines.Length * 20;
        foreach (var l in lines) { Text(l, new Vector2(16, y), new Color(190, 195, 205), 0.9f); y += 20; }

        // Name labels over heads
        foreach (var c in _characters)
        {
            var worldPos = c.Position + new Vector3(0, c.Spec.Height + 0.28f, 0);
            var sp = gd.Viewport.Project(worldPos, proj, view, Matrix.Identity);
            if (sp.Z < 0 || sp.Z > 1) continue;
            var size = _font.MeasureString(c.Spec.Name) * 0.95f;
            Text(c.Spec.Name, new Vector2(sp.X - size.X / 2, sp.Y - size.Y), Color.White, 0.95f);
        }
        _spriteBatch.End();

        // SpriteBatch clobbers these; restore for next frame's 3D pass.
        gd.DepthStencilState = DepthStencilState.Default;
        gd.BlendState = BlendState.Opaque;
    }
}
