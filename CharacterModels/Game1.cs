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
    private EffectParameter _pWorld = null!, _pBones = null!;
    private EffectParameter _pCameraPosition = null!, _pFillColor = null!, _pFillDirection = null!, _pFogColor = null!, _pFogEnd = null!, _pFogStart = null!, _pGrainStrength = null!, _pGrainTexture = null!, _pGroundColor = null!, _pLightColor = null!, _pLightDirection = null!, _pLightViewProjection = null!, _pProjection = null!, _pRimColor = null!, _pShadowMap = null!, _pShadowMapSize = null!, _pShadowStrength = null!, _pSkyColor = null!, _pTime = null!, _pView = null!, _pWindDirection = null!, _pTramplePos = null!, _pTrampleRadius = null!, _pWindStrength = null!;
    private static readonly RasterizerState Wireframe = new() { FillMode = FillMode.WireFrame, CullMode = CullMode.None };
    private Effect _deferredFx = null!;
    private DeferredRenderer _deferred = null!;
    private bool _useDeferred = true;
    private Action? _drawGBuffer;
    // Scripted play-testing (--script, --frames, --every, --log); see Playtest.cs.
    private InputScript? _script;
    private PlaytestRecorder? _recorder;
    private bool _mouseFromScript;
    private bool _scriptDone;
    private float _scriptEndTime;
    private string? _pendingShotLabel;

    /// <summary>Non-key script commands: cam yaw pitch dist | wind s | rain on/off | shot label | focus n | light deg | deferred/forward.</summary>
    private void OnScriptCommand(string[] args)
    {
        float F(int i, float fb) => i < args.Length && float.TryParse(args[i], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var v) ? v : fb;
        switch (args[0].ToLowerInvariant())
        {
            case "cam": _camYaw = MathHelper.ToRadians(F(1, 0)); _camPitch = MathHelper.ToRadians(F(2, 10)); _camDist = _camDistGoal = F(3, _camDist); _autoOrbit = false; break;
            case "wind": _wind.Strength = F(1, 0.7f); break;
            case "rain": _weather.Raining = args.Length < 2 || args[1] != "off"; break;
            case "shot": _pendingShotLabel = args.Length > 1 ? args[1] : "shot"; break;
            case "focus": FocusOn((int)F(1, -1)); break;
            case "light": _lightYaw = MathHelper.ToRadians(F(1, 35)); break;
            case "deferred": _useDeferred = true; break;
            case "forward": _useDeferred = false; break;
            default: Console.Error.WriteLine($"script: unknown command '{args[0]}'"); break;
        }
    }
    private static readonly (Vector3 pos, Vector3 color, float radius, float intensity, float flicker)[] Lamps =
    {
        (new Vector3(-3.4f, 1.35f, 1.7f), new Vector3(1.0f, 0.55f, 0.22f), 5.5f, 5.0f, 0.25f),
        (new Vector3(3.4f, 1.35f, 1.7f), new Vector3(0.3f, 0.5f, 1.0f), 5.5f, 4.0f, 0f),
        (new Vector3(0f, 1.9f, -2.8f), new Vector3(0.9f, 0.3f, 0.8f), 6f, 4.0f, 0f),
        (new Vector3(-1.2f, 0.45f, 3.2f), new Vector3(0.3f, 1.0f, 0.45f), 3.5f, 3.0f, 0f)
    };
    private Texture2D _grain = null!;
    private RenderTarget2D _shadowMap = null!;
    private RenderTargetBinding[] _shadowBinding = null!;
    private readonly RenderTargetBinding[] _shotBinding = new RenderTargetBinding[1];
    private const int ShadowSize = 2048;

    private readonly List<Character> _characters = new();
    private readonly List<Tree> _trees = new();
    private Vegetation? _vegetation;
    private bool _vegetationHidden;
    private readonly Wind _wind = new();
    private Weather _weather = null!;
    private readonly List<(Vector3 pos, float height, Color[] colors)> _leafSources = new(), _noLeafSources = new();
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
    private float _camDistEffective = 6.0f;   // _camDist after pulling in for trees / ground
    private bool _autoOrbit = true;
    private int _focus = -1;

    // Lighting
    private float _lightYaw = MathHelper.ToRadians(35);
    private bool _wireframe;
    private bool _varied;
    private bool _undressed, _treesHidden;
    private readonly List<CharacterSpec> _dressedSpecs = new();
    private int _clipIndex = 1;

    private RenderTarget2D? _shotTarget;
    private int _frame;
    // Play mode (focused character is player-controlled)
    private Vector3 _moveVel;
    private float _actualSpeed;
    private const float WalkSpeed = 1.6f, RunSpeed = 4.4f;

    private KeyboardState _prevKeys;
    private MouseState _prevMouse;
    private float _time;
    // Allocation / GC telemetry: bytes allocated on the game thread between the start of Update and the end of Draw.
    private long _allocStart, _allocFrame, _allocTotal; private int _perfFrames; private float _allocAvg;
    private readonly int[] _gcBase = new int[3];
    private readonly System.Diagnostics.Stopwatch _frameClock = new();
    private double _cpuMsTotal, _cpuMsMax;
    private readonly System.Text.StringBuilder _hud = new(256);
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
        _pWorld = _effect.Parameters["World"]; _pBones = _effect.Parameters["Bones"];
        _pCameraPosition = _effect.Parameters["CameraPosition"]; _pFillColor = _effect.Parameters["FillColor"]; _pFillDirection = _effect.Parameters["FillDirection"]; _pFogColor = _effect.Parameters["FogColor"]; _pFogEnd = _effect.Parameters["FogEnd"]; _pFogStart = _effect.Parameters["FogStart"]; _pGrainStrength = _effect.Parameters["GrainStrength"]; _pGrainTexture = _effect.Parameters["GrainTexture"]; _pGroundColor = _effect.Parameters["GroundColor"]; _pLightColor = _effect.Parameters["LightColor"]; _pLightDirection = _effect.Parameters["LightDirection"]; _pLightViewProjection = _effect.Parameters["LightViewProjection"]; _pProjection = _effect.Parameters["Projection"]; _pRimColor = _effect.Parameters["RimColor"]; _pShadowMap = _effect.Parameters["ShadowMap"]; _pShadowMapSize = _effect.Parameters["ShadowMapSize"]; _pShadowStrength = _effect.Parameters["ShadowStrength"]; _pSkyColor = _effect.Parameters["SkyColor"]; _pTime = _effect.Parameters["Time"]; _pView = _effect.Parameters["View"]; _pWindDirection = _effect.Parameters["WindDirection"]; _pTramplePos = _effect.Parameters["TramplePos"]; _pTrampleRadius = _effect.Parameters["TrampleRadius"]; _pWindStrength = _effect.Parameters["WindStrength"];
        _deferredFx = Content.Load<Effect>("Deferred");
        _deferred = new DeferredRenderer(GraphicsDevice, _deferredFx);
        foreach (var (pos, color, radius, intensity, flicker) in Lamps)
            _deferred.Lights.Add(new PointLight { Position = pos, Color = color, Radius = radius, Intensity = intensity, Flicker = flicker });
        _grain = BuildGrainTexture(256);
        _shadowMap = new RenderTarget2D(GraphicsDevice, ShadowSize, ShadowSize, false, SurfaceFormat.Single, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
        _shadowBinding = new RenderTargetBinding[] { _shadowMap };

        var specs = Roster.Create();
        float spacing = 1.25f;
        for (int i = 0; i < specs.Count; i++)
        {
            var c = CharacterBuilder.Build(GraphicsDevice, specs[i]);
            c.Position = new Vector3((i - (specs.Count - 1) * 0.5f) * spacing, 0, 0);
            c.Player.TimeOffset = i * 1.7f;
            c.Locomotion = Clips.Idle;
            _characters.Add(c);
            _dressedSpecs.Add(specs[i]);
        }
        BuildGround();
        PlantTrees((int)Program.Opt("trees", 22), (int)Program.Opt("seed", 7));
        if (!Program.Flag("no-grass"))
        {
            var opt = new Vegetation.Options { BladesPerM2 = Program.Opt("grass", 38f) };
            foreach (var t in _trees) opt.Keepouts.Add((t.Position.X, t.Position.Z, t.Radius + 0.1f));
            foreach (var (pos, _, _, _, _) in Lamps) opt.Keepouts.Add((pos.X, pos.Z, 0.12f));
            _vegetation = Vegetation.Build(GraphicsDevice, (int)Program.Opt("seed", 7) + 1, opt);
        }
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
        if (Program.Flag("undressed")) SetUndressed(true);
        if (Program.Flag("no-trees")) _treesHidden = true;
        if (Program.Options.TryGetValue("script", out var scriptText))
        {
            _script = InputScript.Parse(scriptText);
            _script.Command += OnScriptCommand;
            _recorder = new PlaytestRecorder(Program.Options.TryGetValue("frames", out var fd) ? fd : null, Program.Opt("every", 0.25f),
                                             Program.Options.TryGetValue("log", out var lp) ? lp : null);
            _autoOrbit = false;
        }

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

    /// <summary>Swaps every character between its dressed spec and the base-body version (mesh rebuild, rig and animation untouched).</summary>
    private void SetUndressed(bool undressed)
    {
        _undressed = undressed;
        for (int i = 0; i < _characters.Count; i++)
            CharacterBuilder.Rebuild(GraphicsDevice, _characters[i], undressed ? _dressedSpecs[i].Undress() : _dressedSpecs[i]);
        _hudStatsKey = -1;   // triangle counts changed
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
        _allocStart = GC.GetAllocatedBytesForCurrentThread();
        _frameClock.Restart();
        float dt = (float)gameTime.ElapsedGameTime.TotalSeconds;
        _time += dt;
        var keys = _script != null ? _script.Advance(dt) : Keyboard.GetState();
        var mouse = _script != null ? _prevMouse : Mouse.GetState();   // scripts never move the mouse
        bool Pressed(Keys k) => keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);

        if (keys.IsKeyDown(Keys.Escape)) Exit();
        if (Pressed(Keys.Space)) _autoOrbit = !_autoOrbit;
        if (Pressed(Keys.G)) _wireframe = !_wireframe;
        if (Pressed(Keys.B)) _useDeferred = !_useDeferred;
        if (Pressed(Keys.N)) _weather.Raining = !_weather.Raining;
        if (Pressed(Keys.U)) SetUndressed(!_undressed);
        if (Pressed(Keys.Y)) _treesHidden = !_treesHidden;
        if (Pressed(Keys.I)) _vegetationHidden = !_vegetationHidden;
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
        _camTarget = Vector3.Lerp(_camTarget, _camTargetGoal, 1 - MathF.Exp(-dt * (_focus >= 0 ? 10 : 6)));
        _camDistEffective = CameraCollision(_camDist);

        if (_focus >= 0) UpdatePlayer(dt, keys);
        foreach (var c in _characters) c.Update(dt);
        if (!_treesHidden) foreach (var t in _trees) t.Update(_time, _wind);
        _weather.Update(dt, _camTarget, _wind, _treesHidden ? _noLeafSources : _leafSources);
        if (_script != null && _script.Done && !_scriptDone) { _scriptDone = true; _scriptEndTime = _time; }

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
        var before = c.Position;
        c.Position += _moveVel * dt;
        ResolveCollisions(c);
        c.Position.X = MathHelper.Clamp(c.Position.X, -13f, 13f);
        c.Position.Z = MathHelper.Clamp(c.Position.Z, -13f, 13f);
        // Animate from the displacement that actually happened (after collision and world bounds), so feet never
        // run on the spot against a trunk or the edge of the map. Smoothed to hide per-frame jitter.
        float actual = dt > 0 ? Vector3.Distance(before, c.Position) / dt : 0f;
        _actualSpeed = MathHelper.Lerp(_actualSpeed, actual, 1 - MathF.Exp(-dt * 15f));
        speed = MathF.Min(speed, _actualSpeed);

        // Speed-driven locomotion: one clip, one stride phase; no idle/walk/run cross-fades.
        c.Speed = speed;
        c.Locomotion = c.Move;

        // Camera follows, leading the character by a fraction of a second of travel so a sprint stays framed.
        _camTargetGoal = c.Position + _moveVel * 0.22f + new Vector3(0, c.Spec.Height * 0.55f, 0);
        _autoOrbit = false;
    }

    /// <summary>
    /// Pulls the orbit camera in so it never sits inside a tree: the target->camera ray is tested against each tree's
    /// trunk (three stacked spheres) and crown sphere, and the distance is clamped to the nearest entry point.
    /// The ground is kept below the camera too. Pull-in is immediate; _camDist itself keeps easing so release is smooth.
    /// </summary>
    private float CameraCollision(float dist)
    {
        var dir = Vector3.Transform(Vector3.Backward, Matrix.CreateRotationX(-_camPitch) * Matrix.CreateRotationY(_camYaw));
        float best = dist;
        for (int i = 0; i < (_treesHidden ? 0 : _trees.Count); i++)
        {
            var t = _trees[i];
            if (Vector3.DistanceSquared(t.Position, _camTarget) > (dist + 3f) * (dist + 3f)) continue;
            float h = t.CrownHeight;
            for (int k = 0; k < 6; k++)   // spheres every ~0.5 m so the ray cannot slip between them
                best = MathF.Min(best, RayHit(_camTarget, dir, t.Position + new Vector3(0, 0.3f + h * 0.16f * k, 0), t.Radius + 0.3f));
            // Exact foliage volumes recorded by the builder (tree-local -> world by yaw + position).
            var world = t.World;
            for (int f = 0; f < t.Foliage.Count; f++)
            {
                var (c, r) = t.Foliage[f];
                best = MathF.Min(best, RayHit(_camTarget, dir, Vector3.Transform(c, world), r + 0.1f));
            }
        }
        // Ground plane: keep the eye at least 0.25 m up.
        if (dir.Y < -1e-4f) best = MathF.Min(best, (0.25f - _camTarget.Y) / dir.Y);
        return MathHelper.Clamp(best - 0.15f, 1.2f, dist);
    }

    /// <summary>Distance along the ray to where it enters the sphere (0 when the origin is already inside), or +inf on a miss.</summary>
    private static float RayHit(Vector3 origin, Vector3 dir, Vector3 centre, float radius)
    {
        var oc = origin - centre;
        float b = Vector3.Dot(oc, dir), c = oc.LengthSquared() - radius * radius;
        if (c < 0) return float.PositiveInfinity;   // origin inside: nothing sensible to pull in to
        float disc = b * b - c;
        if (disc < 0) return float.PositiveInfinity;
        float t = -b - MathF.Sqrt(disc);
        return t > 0 ? t : float.PositiveInfinity;
    }

    /// <summary>Circle-vs-circle push-out against tree trunks, lamp posts and the other characters (two passes so corners settle).</summary>
    private void ResolveCollisions(Character c)
    {
        const float selfR = 0.28f;
        for (int pass = 0; pass < 2; pass++)
        {
            if (!_treesHidden) foreach (var t in _trees) PushOut(ref c.Position, t.Position, selfR + t.Radius);
            foreach (var (pos, _, _, _, _) in Lamps) PushOut(ref c.Position, new Vector3(pos.X, 0, pos.Z), selfR + 0.07f);
            if (_vegetation != null && !_vegetationHidden) foreach (var (bp, br) in _vegetation.Bushes) PushOut(ref c.Position, bp, selfR * 0.6f + br);
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
        var camOffset = Vector3.Transform(new Vector3(0, 0, _camDistEffective), Matrix.CreateRotationX(-_camPitch) * Matrix.CreateRotationY(_camYaw));
        var camPos = _camTarget + camOffset;
        var view = Matrix.CreateLookAt(camPos, _camTarget, Vector3.Up);
        var proj = Matrix.CreatePerspectiveFieldOfView(MathHelper.ToRadians(38), aspect, 0.1f, 80f);

        // Light
        var lightDir = Vector3.Normalize(Vector3.Transform(new Vector3(0, -0.85f, -0.6f), Matrix.CreateRotationY(_lightYaw)));
        var sceneCenter = new Vector3(0, 0.9f, 0);
        var lightView = Matrix.CreateLookAt(sceneCenter - lightDir * 18f, sceneCenter, Vector3.Up);
        float shadowExtent = _trees.Count > 0 ? 20f : 8.5f;
        if (_treesHidden) shadowExtent = 8.5f;
        var lightProj = Matrix.CreateOrthographic(shadowExtent, shadowExtent, 1f, 36f);
        var lightVp = lightView * lightProj;

        // ---- Shadow pass
        gd.SetRenderTargets(_shadowBinding);   // single-target SetRenderTarget allocates a binding array per call
        gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1f, 0);
        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState = RasterizerState.CullNone;
        gd.BlendState = BlendState.Opaque;
        _effect.CurrentTechnique = _effect.Techniques["ShadowCaster"];
        _pLightViewProjection.SetValue(lightVp);
        DrawScene(shadowPass: true);

        // ---- Main pass
        string? shot = Program.Options.TryGetValue("shot", out var sp) ? sp : null;
        if ((shot != null || _recorder != null) && _shotTarget == null)
            _shotTarget = new RenderTarget2D(gd, pp.BackBufferWidth, pp.BackBufferHeight, false, SurfaceFormat.Color, DepthFormat.Depth24, 8, RenderTargetUsage.DiscardContents);
        var lighting = new SceneLighting
        {
            LightDirection = lightDir, LightColor = new Vector3(1.05f, 0.98f, 0.88f) * 1.7f,
            FillDirection = Vector3.Normalize(new Vector3(0.8f, -0.15f, 0.35f)), FillColor = new Vector3(0.16f, 0.18f, 0.24f),
            SkyColor = new Vector3(0.20f, 0.22f, 0.28f), GroundColor = new Vector3(0.10f, 0.085f, 0.07f),
            RimColor = new Vector3(0.28f, 0.32f, 0.42f), FogColor = _fogColorLinear, FogStart = 24f, FogEnd = 75f,
            LightViewProjection = lightVp, ShadowMap = _shadowMap, ShadowMapSize = ShadowSize, ShadowStrength = 0.9f
        };
        _pView.SetValue(view);
        _pProjection.SetValue(proj);
        _pGrainTexture.SetValue(_grain);
        _pGrainStrength.SetValue(0.35f);
        _pTime.SetValue(_time);
        _pWindStrength.SetValue(_wind.Strength);
        _pWindDirection.SetValue(_wind.Direction);
        _pTramplePos.SetValue(_focus >= 0 ? _characters[_focus].Position : new Vector3(0, -100, 0));
        _pTrampleRadius.SetValue(0.55f);

        if (_useDeferred && !_wireframe)
        {
            // Dusk balance: a weaker, cooler key so the point lights carry the scene.
            lighting.LightColor = new Vector3(0.95f, 0.92f, 0.98f) * 1.0f;
            lighting.SkyColor = new Vector3(0.17f, 0.19f, 0.25f);
            lighting.GroundColor = new Vector3(0.08f, 0.07f, 0.06f);
            lighting.FillColor = new Vector3(0.10f, 0.11f, 0.15f);
            _effect.CurrentTechnique = _effect.Techniques["GBuffer"];
            _drawGBuffer ??= () => DrawScene(shadowPass: false);   // cached: a fresh lambda per frame is a 64 B delegate allocation
            _deferred.Render(pp.BackBufferWidth, pp.BackBufferHeight, _shotTarget, view, proj, camPos, lighting, _time, _drawGBuffer);
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

        if (_shotTarget == null) gd.SetRenderTargets(null); else { _shotBinding[0] = _shotTarget; gd.SetRenderTargets(_shotBinding); }
        var fogTm = ToneMap(_fogColorLinear);
        gd.Clear(new Color(fogTm));
        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState = _wireframe ? Wireframe : RasterizerState.CullCounterClockwise;
        gd.SamplerStates[0] = SamplerState.PointClamp;
        gd.SamplerStates[1] = SamplerState.LinearWrap;

        _effect.CurrentTechnique = _effect.Techniques["Skinned"];
        _pLightViewProjection.SetValue(lightVp);
        _pLightDirection.SetValue(lightDir);
        _pLightColor.SetValue(new Vector3(1.05f, 0.98f, 0.88f) * 1.7f);
        _pFillDirection.SetValue(Vector3.Normalize(new Vector3(0.8f, -0.15f, 0.35f)));
        _pFillColor.SetValue(new Vector3(0.16f, 0.18f, 0.24f));
        _pSkyColor.SetValue(new Vector3(0.20f, 0.22f, 0.28f));
        _pGroundColor.SetValue(new Vector3(0.10f, 0.085f, 0.07f));
        _pCameraPosition.SetValue(camPos);
        _pRimColor.SetValue(new Vector3(0.28f, 0.32f, 0.42f));
        _pShadowMap.SetValue(_shadowMap);
        _pShadowMapSize.SetValue((float)ShadowSize);
        _pShadowStrength.SetValue(0.9f);
        _pFogStart.SetValue(24f);
        _pFogEnd.SetValue(75f);
        _pFogColor.SetValue(_fogColorLinear);
        DrawScene(shadowPass: false);
        _weather.Draw(gd, view, proj, camPos, depthAvailable: true, fogTm);

        DrawHud(view, proj);
        FinishFrame(shot);
        base.Draw(gameTime);
    }

    private void FinishFrame(string? shot)
    {
        // Telemetry after the HUD so the string work is counted too.
        _allocFrame = GC.GetAllocatedBytesForCurrentThread() - _allocStart;
        double ms = _frameClock.Elapsed.TotalMilliseconds;
        if (_perfFrames > 2) { _allocTotal += _allocFrame; _allocAvg = _allocTotal / (float)(_perfFrames - 2); _cpuMsTotal += ms; _cpuMsMax = Math.Max(_cpuMsMax, ms); }
        else for (int g = 0; g < 3; g++) _gcBase[g] = GC.CollectionCount(g);
        _perfFrames++;
        int perf = (int)Program.Opt("perf", 0);
        if (perf > 0 && _perfFrames >= perf)
        {
            Console.Error.WriteLine($"perf: {perf} frames, avg {_allocAvg:N0} B/frame allocated on the game thread, GC gen0/1/2 = {GC.CollectionCount(0) - _gcBase[0]}/{GC.CollectionCount(1) - _gcBase[1]}/{GC.CollectionCount(2) - _gcBase[2]}, last frame {_allocFrame:N0} B, CPU Update+Draw avg {_cpuMsTotal / Math.Max(1, _perfFrames - 2):0.00} ms max {_cpuMsMax:0.00} ms");
            Exit();
        }
        if (_script != null)
        {
            var focused = _focus >= 0 ? _characters[_focus] : null;
            _recorder!.Log(_script.Time, _script.Current?.Text ?? "end", focused, _allocFrame, ms);
            if (_shotTarget != null)
            {
                GraphicsDevice.SetRenderTargets(null);
                _recorder.MaybeSave(_script.Time, _shotTarget, _pendingShotLabel);
                _pendingShotLabel = null;
            }
            // Let the final state settle for a few frames so the last frame shows the outcome, then exit.
            if (_scriptDone && _time - _scriptEndTime > 0.3f)
            {
                if (_shotTarget != null) _recorder.MaybeSave(_script.Time, _shotTarget, "final");
                Console.Error.WriteLine($"playtest: {_script.Steps.Count} steps, {_script.Time:0.00} s, {_recorder.Saved.Count} frames saved");
                _recorder.Dispose();
                Exit();
            }
            return;
        }
        if (_shotTarget == null) return;
        GraphicsDevice.SetRenderTargets(null);
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

        _pWorld.SetValue(Matrix.Identity);
        _pBones.SetValue(_identityPalette);
        gd.SetVertexBuffer(_groundVb);
        gd.Indices = _groundIb;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _groundIb.IndexCount / 3);
        }

        if (!shadowPass && _vegetation != null && !_vegetationHidden) DrawSkinned(Matrix.Identity, _identityPalette, _vegetation.VertexBuffer, _vegetation.IndexBuffer);
        foreach (var c in _characters) DrawSkinned(c.World, c.Skeleton.Palette, c.VertexBuffer, c.IndexBuffer);
        if (!_treesHidden) foreach (var t in _trees) DrawSkinned(t.World, t.Skeleton.Palette, t.VertexBuffer, t.IndexBuffer);
    }

    private void DrawSkinned(Matrix world, Matrix[] palette, VertexBuffer vb, IndexBuffer ib)
    {
        var gd = GraphicsDevice;
        _pWorld.SetValue(world);
        _pBones.SetValue(palette);
        gd.SetVertexBuffer(vb);
        gd.Indices = ib;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, ib.IndexCount / 3);
        }
    }

    // HUD lines that only change with state are cached; dynamic lines are built into a reused StringBuilder
    // (SpriteBatch.DrawString has a StringBuilder overload, so no string is materialised per frame).
    private static readonly string[] HudLinesOverview =
    {
        "F / Tab  focus a character and take control of it (WASD + Shift)",
        "1-7  animation (bind, idle, walk, run, wave, attack, dance)   V  varied",
        "Mouse drag  orbit   Right drag  pan   Wheel  zoom   Arrows  orbit",
        "Space auto-orbit   T wind   N rain   U undress   Y trees   I grass   B forward/deferred   L/K rotate light   G wireframe   R reset   Esc quit"
    };
    private readonly string[] _hudLinesControl =
    {
        "",
        "F / Tab  next character (cycles back to overview)     Mouse drag / arrows  orbit   Wheel  zoom",
        "1-7 animation   T wind   N rain   U undress   Y trees   I grass   B forward/deferred   L/K rotate light   G wireframe   R reset   Esc quit"
    };
    private int _hudControlFocus = -2;
    private string _hudStats = "";
    private int _hudStatsKey = -1;
    private readonly Vector2 _shadowOffset = new(1, 1);

    private void DrawHud(Matrix view, Matrix proj)
    {
        var gd = GraphicsDevice;
        _spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp);

        Text("MonoGame Procedural Characters", new Vector2(16, 12), Color.White, 1.3f);

        // Static stats line: rebuilt only when the render mode changes.
        int statsKey = _useDeferred ? 1 : 0;
        if (statsKey != _hudStatsKey)
        {
            int tris = 0, verts = 0;
            for (int i = 0; i < _characters.Count; i++) { tris += _characters[i].Triangles; verts += _characters[i].Vertices; }
            string mode = _useDeferred ? $"deferred: {_deferred.Lights.Count} point lights + shadowed key ({_deferred.LightFormat})" : "forward: key + fill, PCF shadows";
            _hudStats = $"{_characters.Count} characters  |  {tris:N0} triangles  {verts:N0} vertices  |  {_characters[0].Skeleton.Count} bones each  |  {mode}";
            _hudStatsKey = statsKey;
        }
        Text(_hudStats, new Vector2(16, 40), new Color(200, 205, 215));

        // Dynamic line: StringBuilder, no per-frame string allocation.
        var sb = _hud; sb.Clear();
        sb.Append("Animation: ").Append(_varied ? "varied" : Clips.All[_clipIndex].Name);
        sb.Append("     Focus: ").Append(_focus < 0 ? "all" : _characters[_focus].Spec.Name);
        sb.Append("     Trees: ").Append(_treesHidden ? 0 : _trees.Count);
        if (_undressed) sb.Append("   Base bodies");
        if (_vegetation != null && !_vegetationHidden) sb.Append("   Grass: ").Append(_vegetation.Blades).Append(" blades, ").Append(_vegetation.Flowers).Append(" flowers");
        sb.Append("   Wind: ").Append(_wind.Strength <= 0 ? "still" : _wind.Strength < 0.5f ? "calm" : _wind.Strength < 1f ? "breezy" : "gale");
        sb.Append(" (").AppendFixed(_wind.Strength, 2).Append(')');
        sb.Append("   Rain: ").Append(_weather.Raining ? "on" : "off");
        sb.Append("   Particles: ").Append(_weather.Count);
        sb.Append("   Alloc: ").Append(_allocFrame).Append(" B/frame   GC: ").Append(GC.CollectionCount(0)).Append('/').Append(GC.CollectionCount(1)).Append('/').Append(GC.CollectionCount(2));
        TextSb(sb, new Vector2(16, 62), new Color(255, 220, 150));

        string[] lines;
        if (_focus >= 0)
        {
            if (_hudControlFocus != _focus)
            {
                _hudLinesControl[0] = $"Controlling {_characters[_focus].Spec.Name}:  W A S D  move   Shift  run   H draw / sheathe weapon   Q attack   E wave   X dance";
                _hudControlFocus = _focus;
            }
            lines = _hudLinesControl;
        }
        else lines = HudLinesOverview;
        float y = gd.Viewport.Height - 16 - lines.Length * 20;
        for (int i = 0; i < lines.Length; i++) { Text(lines[i], new Vector2(16, y), new Color(190, 195, 205), 0.9f); y += 20; }

        // Name labels over heads
        for (int i = 0; i < _characters.Count; i++)
        {
            var c = _characters[i];
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

    private void Text(string s, Vector2 pos, Color col, float scale = 1f)
    {
        _spriteBatch.DrawString(_font, s, pos + _shadowOffset, Color.Black * 0.7f, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        _spriteBatch.DrawString(_font, s, pos, col, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }

    private void TextSb(System.Text.StringBuilder s, Vector2 pos, Color col, float scale = 1f)
    {
        _spriteBatch.DrawString(_font, s, pos + _shadowOffset, Color.Black * 0.7f, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
        _spriteBatch.DrawString(_font, s, pos, col, 0, Vector2.Zero, scale, SpriteEffects.None, 0);
    }
}

internal static class StringBuilderExt
{
    /// <summary>Appends a float with a fixed number of decimals without going through float.ToString (which allocates).</summary>
    public static System.Text.StringBuilder AppendFixed(this System.Text.StringBuilder sb, float v, int decimals)
    {
        if (v < 0) { sb.Append('-'); v = -v; }
        int scale = 1; for (int i = 0; i < decimals; i++) scale *= 10;
        long whole = (long)v; long frac = (long)MathF.Round((v - whole) * scale);
        if (frac >= scale) { whole++; frac -= scale; }
        sb.Append(whole);
        if (decimals > 0) { sb.Append('.'); for (int d = scale / 10; d >= 1; d /= 10) sb.Append((char)('0' + (frac / d) % 10)); }
        return sb;
    }
}
