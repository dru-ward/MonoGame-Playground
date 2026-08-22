using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CharacterModels;

public sealed class PointLight
{
    public Vector3 Position;
    public Vector3 Color = Vector3.One;
    public float Radius = 4f;
    public float Intensity = 1f;
    public float Flicker;                 // 0..1 amplitude of noise on intensity
    public Func<Vector3>? Follow;         // optional: position provider (e.g. a bone socket)
}

/// <summary>Lighting parameters shared by the forward and deferred paths.</summary>
public struct SceneLighting
{
    public Vector3 LightDirection, LightColor, FillDirection, FillColor, SkyColor, GroundColor, RimColor, FogColor;
    public Matrix LightViewProjection;
    public Texture2D ShadowMap;
    public float ShadowMapSize, ShadowStrength, FogStart, FogEnd;
}

/// <summary>
/// Deferred path: G-buffer (albedo+spec, normal+shininess, depth) via MRT, a light accumulation buffer filled by a
/// shadowed directional full-screen pass plus additive sphere-volume point lights, then a full-screen composite.
/// </summary>
public sealed class DeferredRenderer : IDisposable
{
    private readonly GraphicsDevice _gd;
    private readonly Effect _fx;
    private RenderTarget2D? _albedo, _normal, _depth, _light;
    // Cached bindings: SetRenderTargets(params ...) and single-target SetRenderTarget allocate a binding array per call.
    private RenderTargetBinding[] _gbufferBindings = Array.Empty<RenderTargetBinding>(), _lightBinding = Array.Empty<RenderTargetBinding>();
    private readonly RenderTargetBinding[] _outputBinding = new RenderTargetBinding[1];
    private readonly VertexPositionTexture[] _quad;
    private readonly VertexBuffer _sphereVb;
    private readonly IndexBuffer _sphereIb;
    private readonly int _sphereTris;
    private static readonly BlendState Additive = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.One
    };

    // Effect parameters looked up once: EffectParameterCollection[string] is a linear name search per call.
    private readonly EffectParameter _pAlbedoTex, _pCameraPosition, _pDepthTex, _pFillColor, _pFillDirection, _pFogColor, _pFogEnd, _pFogStart, _pGroundColor, _pInvViewProjection, _pLightColor, _pLightDirection, _pLightTex, _pLightViewProjection, _pNormalTex, _pPointColor, _pPointIntensity, _pPointPosition, _pPointRadius, _pRimColor, _pShadowMap, _pShadowMapSize, _pShadowStrength, _pSkyColor, _pUvFlip, _pWorldViewProjection;
    private readonly EffectTechnique _tDirectional, _tPointLight, _tComposite;

    public readonly List<PointLight> Lights = new();
    public string LightFormat = "?";
    /// <summary>Screen-UV y sign for passes that derive UV from clip position while rendering into a render target.</summary>
    public float UvFlip = float.Parse(Program.Options.TryGetValue("uvflip", out var f) ? f : "1", System.Globalization.CultureInfo.InvariantCulture);
    public Texture2D? AlbedoTarget => _albedo;
    public Texture2D? NormalTarget => _normal;
    public Texture2D? LightTarget => _light;

    public DeferredRenderer(GraphicsDevice gd, Effect deferredEffect)
    {
        _gd = gd; _fx = deferredEffect;
        var P = _fx.Parameters;
        _pAlbedoTex = P["AlbedoTex"]; _pCameraPosition = P["CameraPosition"]; _pDepthTex = P["DepthTex"]; _pFillColor = P["FillColor"]; _pFillDirection = P["FillDirection"]; _pFogColor = P["FogColor"]; _pFogEnd = P["FogEnd"]; _pFogStart = P["FogStart"]; _pGroundColor = P["GroundColor"]; _pInvViewProjection = P["InvViewProjection"]; _pLightColor = P["LightColor"]; _pLightDirection = P["LightDirection"]; _pLightTex = P["LightTex"]; _pLightViewProjection = P["LightViewProjection"]; _pNormalTex = P["NormalTex"]; _pPointColor = P["PointColor"]; _pPointIntensity = P["PointIntensity"]; _pPointPosition = P["PointPosition"]; _pPointRadius = P["PointRadius"]; _pRimColor = P["RimColor"]; _pShadowMap = P["ShadowMap"]; _pShadowMapSize = P["ShadowMapSize"]; _pShadowStrength = P["ShadowStrength"]; _pSkyColor = P["SkyColor"]; _pUvFlip = P["UvFlip"]; _pWorldViewProjection = P["WorldViewProjection"];
        _tDirectional = _fx.Techniques["Directional"]; _tPointLight = _fx.Techniques["PointLight"]; _tComposite = _fx.Techniques["Composite"];
        _quad = new[]
        {
            new VertexPositionTexture(new Vector3(-1, 1, 0), new Vector2(0, 0)),
            new VertexPositionTexture(new Vector3(1, 1, 0), new Vector2(1, 0)),
            new VertexPositionTexture(new Vector3(-1, -1, 0), new Vector2(0, 1)),
            new VertexPositionTexture(new Vector3(1, -1, 0), new Vector2(1, 1))
        };
        (_sphereVb, _sphereIb, _sphereTris) = BuildSphere(gd, 12, 18);
    }

    private static (VertexBuffer, IndexBuffer, int) BuildSphere(GraphicsDevice gd, int stacks, int slices)
    {
        var verts = new List<VertexPosition>();
        var idx = new List<short>();
        for (int st = 0; st <= stacks; st++)
        {
            float phi = st / (float)stacks * MathHelper.Pi;
            for (int sl = 0; sl < slices; sl++)
            {
                float th = sl / (float)slices * MathHelper.TwoPi;
                verts.Add(new VertexPosition(new Vector3(MathF.Sin(phi) * MathF.Sin(th), MathF.Cos(phi), MathF.Sin(phi) * MathF.Cos(th))));
            }
        }
        for (int st = 0; st < stacks; st++)
        for (int sl = 0; sl < slices; sl++)
        {
            int a = st * slices + sl, b = st * slices + (sl + 1) % slices, c = a + slices, d = b + slices;
            idx.Add((short)a); idx.Add((short)b); idx.Add((short)d);
            idx.Add((short)a); idx.Add((short)d); idx.Add((short)c);
        }
        var vb = new VertexBuffer(gd, VertexPosition.VertexDeclaration, verts.Count, BufferUsage.WriteOnly);
        vb.SetData(verts.ToArray());
        var ib = new IndexBuffer(gd, IndexElementSize.SixteenBits, idx.Count, BufferUsage.WriteOnly);
        ib.SetData(idx.ToArray());
        return (vb, ib, idx.Count / 3);
    }

    private void EnsureTargets(int w, int h)
    {
        if (_albedo != null && _albedo.Width == w && _albedo.Height == h) return;
        _albedo?.Dispose(); _normal?.Dispose(); _depth?.Dispose(); _light?.Dispose();
        // MRT: every bound target must share size and multisample count (none here — deferred gives up MSAA).
        _albedo = new RenderTarget2D(_gd, w, h, false, SurfaceFormat.Color, DepthFormat.Depth24, 0, RenderTargetUsage.DiscardContents);
        _normal = new RenderTarget2D(_gd, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
        _depth = new RenderTarget2D(_gd, w, h, false, SurfaceFormat.Single, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
        _gbufferBindings = new RenderTargetBinding[] { _albedo, _normal, _depth };
        try
        {
            _light = new RenderTarget2D(_gd, w, h, false, SurfaceFormat.HalfVector4, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            LightFormat = "HalfVector4";
        }
        catch (Exception)
        {
            _light = new RenderTarget2D(_gd, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.DiscardContents);
            LightFormat = "Color (8-bit, clips)";
        }
        _lightBinding = new RenderTargetBinding[] { _light };
    }

    private void UnbindTextures()
    {
        for (int i = 0; i < 8; i++) _gd.Textures[i] = null;
    }

    /// <param name="drawGBuffer">Draws the scene with the character effect already switched to its GBuffer technique.</param>
    public void Render(int width, int height, RenderTarget2D? output, Matrix view, Matrix proj, Vector3 camPos,
                       in SceneLighting L, float time, Action drawGBuffer)
    {
        EnsureTargets(width, height);
        var gd = _gd;

        // ---- 1. G-buffer (MRT)
        UnbindTextures();
        gd.SetRenderTargets(_gbufferBindings);
        gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1f, 0);   // depth target reads 1 = empty
        gd.DepthStencilState = DepthStencilState.Default;
        gd.BlendState = BlendState.Opaque;
        gd.RasterizerState = RasterizerState.CullCounterClockwise;
        for (int i = 0; i < 4; i++) gd.SamplerStates[i] = SamplerState.LinearWrap;      // grain texture
        drawGBuffer();

        // ---- 2. Light accumulation
        UnbindTextures();
        gd.SetRenderTargets(_lightBinding);
        gd.Clear(Color.Transparent);
        gd.DepthStencilState = DepthStencilState.None;
        gd.BlendState = Additive;
        gd.RasterizerState = RasterizerState.CullNone;
        for (int i = 0; i < 6; i++) gd.SamplerStates[i] = SamplerState.PointClamp;

        var invVp = Matrix.Invert(view * proj);
        _pInvViewProjection.SetValue(invVp);
        _pCameraPosition.SetValue(camPos);
        _pAlbedoTex.SetValue(_albedo);
        _pNormalTex.SetValue(_normal);
        _pDepthTex.SetValue(_depth);
        _pShadowMap.SetValue(L.ShadowMap);
        _pLightViewProjection.SetValue(L.LightViewProjection);
        _pLightDirection.SetValue(L.LightDirection);
        _pLightColor.SetValue(L.LightColor);
        _pFillDirection.SetValue(L.FillDirection);
        _pFillColor.SetValue(L.FillColor);
        _pSkyColor.SetValue(L.SkyColor);
        _pGroundColor.SetValue(L.GroundColor);
        _pShadowMapSize.SetValue(L.ShadowMapSize);
        _pShadowStrength.SetValue(L.ShadowStrength);

        _fx.CurrentTechnique = _tDirectional;
        DrawQuad();

        // Point lights as sphere volumes: back faces, no depth test, so a camera inside the volume still works.
        _fx.CurrentTechnique = _tPointLight;
        _pUvFlip.SetValue(UvFlip);
        gd.RasterizerState = RasterizerState.CullClockwise;
        gd.SetVertexBuffer(_sphereVb);
        gd.Indices = _sphereIb;
        for (int li = 0; li < Lights.Count; li++)
        {
            var light = Lights[li];
            var pos = light.Follow?.Invoke() ?? light.Position;
            float flicker = light.Flicker > 0 ? 1f + light.Flicker * (0.6f * MathF.Sin(time * 23f) + 0.4f * MathF.Sin(time * 7.3f + 1.7f)) : 1f;
            _pPointPosition.SetValue(pos);
            _pPointColor.SetValue(light.Color);
            _pPointRadius.SetValue(light.Radius);
            _pPointIntensity.SetValue(light.Intensity * flicker);
            _pWorldViewProjection.SetValue(Matrix.CreateScale(light.Radius * 1.05f) * Matrix.CreateTranslation(pos) * view * proj);
            foreach (var pass in _fx.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _sphereTris);
            }
        }

        // ---- 3. Composite
        UnbindTextures();
        if (output == null) gd.SetRenderTargets(null); else { _outputBinding[0] = output; gd.SetRenderTargets(_outputBinding); }
        gd.BlendState = BlendState.Opaque;
        gd.RasterizerState = RasterizerState.CullNone;
        _pLightTex.SetValue(_light);
        _pAlbedoTex.SetValue(_albedo);
        _pNormalTex.SetValue(_normal);
        _pDepthTex.SetValue(_depth);
        _pRimColor.SetValue(L.RimColor);
        _pFogStart.SetValue(L.FogStart);
        _pFogEnd.SetValue(L.FogEnd);
        _pFogColor.SetValue(L.FogColor);
        _fx.CurrentTechnique = _tComposite;
        DrawQuad();

        gd.DepthStencilState = DepthStencilState.Default;
    }

    private void DrawQuad()
    {
        foreach (var pass in _fx.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawUserPrimitives(PrimitiveType.TriangleStrip, _quad, 0, 2);
        }
    }

    public void Dispose()
    {
        _albedo?.Dispose(); _normal?.Dispose(); _depth?.Dispose(); _light?.Dispose();
        _sphereVb.Dispose(); _sphereIb.Dispose();
    }
}
