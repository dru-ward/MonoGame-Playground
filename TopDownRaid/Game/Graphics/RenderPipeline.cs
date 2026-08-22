using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Content;
using Microsoft.Xna.Framework.Graphics;

namespace Game.Graphics;

/// <summary>
/// The deferred-style 2.5D renderer:
/// <code>
///   Pass 1  AlbedoRT   scene sprites, albedo textures            (SceneBatch, camera view)
///   Pass 2  NormalRT   same sprites, normal-map textures         (rotated sprites via SpriteNormalRotate)
///   Pass 3  LightRT    Clear(ambient); per light: additive blend + scissor rect, PointLight technique
///   Pass 4  SceneRT    Composite (albedo * diffuse + spec) then emissive quads (particles, tracers) additively
///   Pass 5/6 BloomA/B  bright-pass at ½ res, separable Gaussian ping-pong
///   Pass 7  backbuffer FinalCombine (scene + bloom, vignette) or a debug Blit; then the unlit UI overlay
/// </code>
/// Coordinate note: the camera never rotates ⇒ view space == render-target pixel space ⇒ light positions are
/// transformed on the CPU (Vector2.Transform(pos, view)) and the lighting shader is matrix-free.
/// </summary>
public sealed class RenderPipeline : IDisposable
{
    public enum DebugView { Albedo = 1, Normal, Light, Scene, Bloom, Final }
    public const int MaxLights = 8;      // == MAX_LIGHTS in Deferred.fx
    private const int BlurTaps = 15;     // == BLUR_TAPS in Deferred.fx

    // ---- public knobs -----------------------------------------------------------------------------------
    public DebugView View = DebugView.Final;
    public bool BloomEnabled = true;
    public bool WireframeParticles = false;
    public bool ShowScissor = false;
    public bool SinglePassLights = false;
    public Vector4 Ambient = new(0.085f, 0.09f, 0.115f, 0f);   // dim, cold night ambient (rgb), a = 0 specular
    public float BloomIntensity = 1.15f;
    /// <summary>Seconds, drives film grain animation. Set by the host each frame.</summary>
    public float Time;

    public GraphicsDevice Device { get; }
    public GraphicsStates States { get; }
    public SpriteBatch SpriteBatch { get; }
    public SceneBatch Scene { get; }
    public Effect Effect { get; }
    public Texture2D Pixel { get; }
    public int Width => _rtW;
    public int Height => _rtH;
    public Matrix CurrentView { get; private set; }

    // ---- internals --------------------------------------------------------------------------------------
    private RenderTarget2D? _albedoRT, _normalRT, _lightRT, _sceneRT, _bloomA, _bloomB;
    private int _rtW, _rtH;
    private readonly EffectParameter _pWvp, _pScreenSize, _pAlbedoTex, _pNormalTex, _pLightTex, _pBloomTex,
                                     _pLightPos, _pLightColor, _pLightRadius, _pLightIntensity,
                                     _pLightPositions, _pLightColors, _pLightRadiusIntensity,
                                     _pSampleOffsets, _pSampleWeights, _pBloomIntensity, _pLightDir, _pLightCone, _pLightDirs, _pLightCones;
    private readonly EffectParameter _pTime;
    private readonly Vector2[] _blurOffsetsH = new Vector2[BlurTaps], _blurOffsetsV = new Vector2[BlurTaps];
    private readonly float[] _blurWeights = new float[BlurTaps];
    private readonly VertexPositionTexture[] _quad = new VertexPositionTexture[4];
    private static readonly short[] QuadIndices = { 0, 1, 2, 0, 2, 3 };
    private int _quadW = -1, _quadH = -1;
    private readonly Vector3[] _lp = new Vector3[MaxLights]; private readonly Vector3[] _lc = new Vector3[MaxLights]; private readonly Vector2[] _lri = new Vector2[MaxLights];
    private readonly Vector2[] _ld = new Vector2[MaxLights]; private readonly Vector2[] _lcone = new Vector2[MaxLights];

    public RenderPipeline(GraphicsDevice gd, ContentManager content)
    {
        Device = gd;
        States = new GraphicsStates();
        SpriteBatch = new SpriteBatch(gd);
        Pixel = TextureFactory.CreatePixel(gd, Color.White);
        Effect = content.Load<Effect>("Shaders/Deferred");
        Scene = new SceneBatch(SpriteBatch, Effect, States);

        EffectParameter P(string n) => Effect.Parameters[n] ?? throw new InvalidOperationException($"Deferred.fx has no parameter '{n}'");
        _pWvp = P("WorldViewProjection"); _pScreenSize = P("ScreenSize");
        _pAlbedoTex = P("AlbedoTex"); _pNormalTex = P("NormalTex"); _pLightTex = P("LightTex"); _pBloomTex = P("BloomTex");
        _pLightPos = P("LightPosition"); _pLightColor = P("LightColor"); _pLightRadius = P("LightRadius"); _pLightIntensity = P("LightIntensity");
        _pLightPositions = P("LightPositions"); _pLightColors = P("LightColors"); _pLightRadiusIntensity = P("LightRadiusIntensity");
        _pSampleOffsets = P("SampleOffsets"); _pSampleWeights = P("SampleWeights"); _pBloomIntensity = P("BloomIntensity");
        _pLightDir = P("LightDir"); _pLightCone = P("LightCone"); _pLightDirs = P("LightDirs"); _pLightCones = P("LightCones");

        // NOTE: initialisers inside the .fx are NOT applied by the OpenGL effect runtime — set every scalar here.
        Effect.Parameters["SpecularPower"].SetValue(32f);
        Effect.Parameters["SpecularAmount"].SetValue(0.35f);
        Effect.Parameters["Exposure"].SetValue(1.0f);
        Effect.Parameters["BloomThreshold"].SetValue(0.6f);
        Effect.Parameters["BloomSoftKnee"].SetValue(0.5f);
        Effect.Parameters["NormalRotation"].SetValue(new Vector2(1f, 0f));
        SetGrade(daylight: false);
        Effect.Parameters["VignetteRadius"].SetValue(0.85f);
        Effect.Parameters["VignetteSoftness"].SetValue(0.55f);
        _pTime = Effect.Parameters["Time"];

        const float sigma = 3.0f; float sum = 0f;
        for (int i = 0; i < BlurTaps; i++) { float x = i - (BlurTaps - 1) / 2f; _blurWeights[i] = MathF.Exp(-(x * x) / (2f * sigma * sigma)); sum += _blurWeights[i]; }
        for (int i = 0; i < BlurTaps; i++) _blurWeights[i] /= sum;

        EnsureRenderTargets();
    }

    /// <summary>
    /// Colour grade preset. Night = grungy urban (desaturated, cool shadows, warm highlights, heavy vignette,
    /// grain); daylight = a light touch so bright outdoor maps keep their colour.
    /// </summary>
    public void SetGrade(bool daylight)
    {
        Effect.Parameters["Desaturate"].SetValue(daylight ? 0.10f : 0.30f);
        Effect.Parameters["GradeShadows"].SetValue(daylight ? new Vector3(0.96f, 0.99f, 0.98f) : new Vector3(0.82f, 0.90f, 1.05f));
        Effect.Parameters["GradeHighlights"].SetValue(daylight ? new Vector3(1.06f, 1.03f, 0.95f) : new Vector3(1.10f, 1.02f, 0.90f));
        Effect.Parameters["Contrast"].SetValue(daylight ? 1.04f : 1.08f);
        Effect.Parameters["GrainAmount"].SetValue(daylight ? 0.035f : 0.07f);
        Effect.Parameters["VignetteStrength"].SetValue(daylight ? 0.28f : 0.62f);
    }

    /// <summary>(Re)creates the render targets whenever the back buffer size changes.</summary>
    public void EnsureRenderTargets()
    {
        var pp = Device.PresentationParameters;
        int w = Math.Max(1, pp.BackBufferWidth), h = Math.Max(1, pp.BackBufferHeight);
        if (w == _rtW && h == _rtH && _albedoRT != null) return;
        DisposeTargets();
        _albedoRT = new RenderTarget2D(Device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
        _normalRT = new RenderTarget2D(Device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
        _lightRT  = new RenderTarget2D(Device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
        _sceneRT  = new RenderTarget2D(Device, w, h, false, SurfaceFormat.Color, DepthFormat.None);
        int bw = Math.Max(1, w / 2), bh = Math.Max(1, h / 2);
        _bloomA = new RenderTarget2D(Device, bw, bh, false, SurfaceFormat.Color, DepthFormat.None);
        _bloomB = new RenderTarget2D(Device, bw, bh, false, SurfaceFormat.Color, DepthFormat.None);
        for (int i = 0; i < BlurTaps; i++) { float t = i - (BlurTaps - 1) / 2f; _blurOffsetsH[i] = new Vector2(t / bw, 0f); _blurOffsetsV[i] = new Vector2(0f, t / bh); }
        _rtW = w; _rtH = h;
    }

    /// <summary>
    /// Renders one frame. <paramref name="drawScene"/> is invoked twice (albedo pass, then normal pass);
    /// <paramref name="drawEmissive"/> draws additive world-space quads into the lit scene;
    /// <paramref name="drawOverlay"/> draws unlit screen-space UI onto the back buffer.
    /// </summary>
    public void RenderFrame(Matrix view, float zoom, Action<SceneBatch> drawScene, IReadOnlyList<PointLight> lights,
                            Action<RenderPipeline> drawEmissive, Action<SpriteBatch> drawOverlay)
    {
        EnsureRenderTargets();
        CurrentView = view;
        var gd = Device; int w = _rtW, h = _rtH;

        // ---- PASS 1 : albedo -----------------------------------------------------------------------------
        Unbind(); gd.SetRenderTarget(_albedoRT); gd.Clear(Color.Black);
        Scene.Begin(view, normalPass: false); drawScene(Scene); Scene.End();

        // ---- PASS 2 : normals (clear to encoded flat normal so uncovered pixels face the camera) ---------
        Unbind(); gd.SetRenderTarget(_normalRT); gd.Clear(new Color(128, 128, 255, 255));
        Scene.Begin(view, normalPass: true); drawScene(Scene); Scene.End();

        // ---- PASS 3 : lights -----------------------------------------------------------------------------
        Unbind(); gd.SetRenderTarget(_lightRT); gd.Clear(new Color(Ambient));
        DrawLights(lights, view, zoom, w, h);

        // ---- PASS 4 : composite + emissive ---------------------------------------------------------------
        Unbind(); gd.SetRenderTarget(_sceneRT);
        gd.BlendState = BlendState.Opaque; gd.DepthStencilState = DepthStencilState.None; gd.RasterizerState = States.SolidNoCull;
        SetRtSamplers();
        _pAlbedoTex.SetValue(_albedoRT); _pLightTex.SetValue(_lightRT);
        Effect.CurrentTechnique = Effect.Techniques["Composite"];
        DrawFullScreenQuad(w, h);
        drawEmissive(this);

        // ---- PASS 5/6 : bloom ----------------------------------------------------------------------------
        if (BloomEnabled) DrawBloom();

        // ---- PASS 7 : back buffer ------------------------------------------------------------------------
        Unbind(); gd.SetRenderTarget(null); gd.Clear(Color.Black);
        gd.BlendState = BlendState.Opaque; gd.RasterizerState = States.SolidNoCull; SetRtSamplers();
        var pp = gd.PresentationParameters; int bw = pp.BackBufferWidth, bh = pp.BackBufferHeight;
        switch (View)
        {
            case DebugView.Albedo: Blit(_albedoRT!, bw, bh); break;
            case DebugView.Normal: Blit(_normalRT!, bw, bh); break;
            case DebugView.Light:  Blit(_lightRT!, bw, bh); break;
            case DebugView.Scene:  Blit(_sceneRT!, bw, bh); break;
            case DebugView.Bloom:  Blit(_bloomA!, bw, bh); break;
            default:
                _pTime.SetValue(Time); _pScreenSize.SetValue(new Vector2(bw, bh));
                _pAlbedoTex.SetValue(_sceneRT);
                _pBloomTex.SetValue(BloomEnabled ? _bloomA : Pixel);
                _pBloomIntensity.SetValue(BloomEnabled ? BloomIntensity : 0f);
                Effect.CurrentTechnique = Effect.Techniques["FinalCombine"];
                DrawFullScreenQuad(bw, bh);
                break;
        }
        drawOverlay(SpriteBatch);
    }

    private void DrawLights(IReadOnlyList<PointLight> lights, Matrix view, float zoom, int w, int h)
    {
        var gd = Device;
        gd.BlendState = States.AdditiveLight; gd.DepthStencilState = DepthStencilState.None;
        SetRtSamplers();
        _pNormalTex.SetValue(_normalRT); _pScreenSize.SetValue(new Vector2(w, h));

        if (SinglePassLights)
        {
            gd.RasterizerState = States.SolidNoCull;
            for (int i = 0; i < MaxLights; i++)
            {
                if (i < lights.Count)
                {
                    var l = lights[i]; var sp = Vector2.Transform(l.Position, view);
                    _lp[i] = new Vector3(sp, l.Height * zoom); _lc[i] = l.Color; _lri[i] = new Vector2(l.Radius * zoom, l.EffectiveIntensity);
                    _ld[i] = l.Direction; _lcone[i] = l.ConeCos;
                }
                else { _lp[i] = Vector3.Zero; _lc[i] = Vector3.Zero; _lri[i] = new Vector2(1f, 0f); _ld[i] = Vector2.UnitX; _lcone[i] = new Vector2(-2f, -2f); }
            }
            _pLightPositions.SetValue(_lp); _pLightColors.SetValue(_lc); _pLightRadiusIntensity.SetValue(_lri);
            _pLightDirs.SetValue(_ld); _pLightCones.SetValue(_lcone);
            Effect.CurrentTechnique = Effect.Techniques["MultiLight"];
            DrawFullScreenQuad(w, h);
            return;
        }

        gd.RasterizerState = States.ScissorNoCull;
        var full = new Rectangle(0, 0, w, h);
        foreach (var l in lights)
        {
            var sp = Vector2.Transform(l.Position, view); float r = l.Radius * zoom;
            var bounds = Rectangle.Intersect(new Rectangle((int)(sp.X - r), (int)(sp.Y - r), (int)(2 * r), (int)(2 * r)), full);
            if (bounds.Width <= 0 || bounds.Height <= 0) continue;
            gd.ScissorRectangle = bounds;
            if (ShowScissor)
            {
                gd.BlendState = States.Tint; _pAlbedoTex.SetValue(Pixel);
                Effect.CurrentTechnique = Effect.Techniques["Blit"]; DrawFullScreenQuad(w, h);
                gd.BlendState = States.AdditiveLight;
            }
            _pLightPos.SetValue(new Vector3(sp, l.Height * zoom)); _pLightColor.SetValue(l.Color);
            _pLightRadius.SetValue(r); _pLightIntensity.SetValue(l.EffectiveIntensity);
            _pLightDir.SetValue(l.Direction); _pLightCone.SetValue(l.ConeCos);
            Effect.CurrentTechnique = Effect.Techniques["PointLight"];
            DrawFullScreenQuad(w, h);
        }
        gd.RasterizerState = States.SolidNoCull;
    }

    private void DrawBloom()
    {
        var gd = Device; int bw = _bloomA!.Width, bh = _bloomA.Height;
        gd.BlendState = BlendState.Opaque; gd.DepthStencilState = DepthStencilState.None; gd.RasterizerState = States.SolidNoCull;
        SetRtSamplers();
        Unbind(); gd.SetRenderTarget(_bloomA);
        _pAlbedoTex.SetValue(_sceneRT); Effect.CurrentTechnique = Effect.Techniques["BloomExtract"]; DrawFullScreenQuad(bw, bh);
        _pSampleWeights.SetValue(_blurWeights); Effect.CurrentTechnique = Effect.Techniques["GaussianBlur"];
        for (int iter = 0; iter < 2; iter++)
        {
            Unbind(); gd.SetRenderTarget(_bloomB); _pAlbedoTex.SetValue(_bloomA); _pSampleOffsets.SetValue(_blurOffsetsH); DrawFullScreenQuad(bw, bh);
            Unbind(); gd.SetRenderTarget(_bloomA); _pAlbedoTex.SetValue(_bloomB); _pSampleOffsets.SetValue(_blurOffsetsV); DrawFullScreenQuad(bw, bh);
        }
    }

    private void Blit(Texture2D src, int w, int h)
    {
        _pAlbedoTex.SetValue(src); Effect.CurrentTechnique = Effect.Techniques["Blit"]; DrawFullScreenQuad(w, h);
    }

    /// <summary>Pixel-space quad; WVP = ortho(0,w,h,0) so x_clip = 2x/w-1, y_clip = 1-2y/h. UV*ScreenSize = pixel.</summary>
    private void DrawFullScreenQuad(int w, int h)
    {
        if (w != _quadW || h != _quadH)
        {
            _quad[0] = new VertexPositionTexture(new Vector3(0, 0, 0), new Vector2(0, 0));
            _quad[1] = new VertexPositionTexture(new Vector3(w, 0, 0), new Vector2(1, 0));
            _quad[2] = new VertexPositionTexture(new Vector3(w, h, 0), new Vector2(1, 1));
            _quad[3] = new VertexPositionTexture(new Vector3(0, h, 0), new Vector2(0, 1));
            _quadW = w; _quadH = h;
        }
        _pWvp.SetValue(Matrix.CreateOrthographicOffCenter(0, w, h, 0, 0, 1));
        foreach (var pass in Effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            Device.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _quad, 0, 4, QuadIndices, 0, 2);
        }
    }

    private void SetRtSamplers() { for (int i = 0; i < 4; i++) Device.SamplerStates[i] = States.RtLinearClamp; }
    /// <summary>A render target must never be bound as a texture while it is the active target.</summary>
    private void Unbind() { for (int i = 0; i < 4; i++) Device.Textures[i] = null; }

    /// <summary>Reads the back buffer back and writes a PNG (used by F12 and headless verification).</summary>
    public void SaveScreenshot(string path)
    {
        var pp = Device.PresentationParameters; int w = pp.BackBufferWidth, h = pp.BackBufferHeight;
        var data = new Color[w * h]; Device.GetBackBufferData(data);
        using var tex = new Texture2D(Device, w, h); tex.SetData(data);
        using var fs = File.Create(path); tex.SaveAsPng(fs, w, h);
    }

    private void DisposeTargets()
    {
        _albedoRT?.Dispose(); _normalRT?.Dispose(); _lightRT?.Dispose(); _sceneRT?.Dispose(); _bloomA?.Dispose(); _bloomB?.Dispose();
    }

    public void Dispose() { DisposeTargets(); States.Dispose(); SpriteBatch.Dispose(); Pixel.Dispose(); }
}
