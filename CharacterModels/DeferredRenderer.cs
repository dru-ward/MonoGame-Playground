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
    private readonly VertexPositionTexture[] _quad;
    private readonly VertexBuffer _sphereVb;
    private readonly IndexBuffer _sphereIb;
    private readonly int _sphereTris;
    private static readonly BlendState Additive = new()
    {
        ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One,
        AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.One
    };

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
        gd.SetRenderTargets(_albedo, _normal, _depth);
        gd.Clear(ClearOptions.Target | ClearOptions.DepthBuffer, Color.White, 1f, 0);   // depth target reads 1 = empty
        gd.DepthStencilState = DepthStencilState.Default;
        gd.BlendState = BlendState.Opaque;
        gd.RasterizerState = RasterizerState.CullCounterClockwise;
        for (int i = 0; i < 4; i++) gd.SamplerStates[i] = SamplerState.LinearWrap;      // grain texture
        drawGBuffer();

        // ---- 2. Light accumulation
        UnbindTextures();
        gd.SetRenderTarget(_light);
        gd.Clear(Color.Transparent);
        gd.DepthStencilState = DepthStencilState.None;
        gd.BlendState = Additive;
        gd.RasterizerState = RasterizerState.CullNone;
        for (int i = 0; i < 6; i++) gd.SamplerStates[i] = SamplerState.PointClamp;

        var p = _fx.Parameters;
        var invVp = Matrix.Invert(view * proj);
        p["InvViewProjection"].SetValue(invVp);
        p["CameraPosition"].SetValue(camPos);
        p["AlbedoTex"].SetValue(_albedo);
        p["NormalTex"].SetValue(_normal);
        p["DepthTex"].SetValue(_depth);
        p["ShadowMap"].SetValue(L.ShadowMap);
        p["LightViewProjection"].SetValue(L.LightViewProjection);
        p["LightDirection"].SetValue(L.LightDirection);
        p["LightColor"].SetValue(L.LightColor);
        p["FillDirection"].SetValue(L.FillDirection);
        p["FillColor"].SetValue(L.FillColor);
        p["SkyColor"].SetValue(L.SkyColor);
        p["GroundColor"].SetValue(L.GroundColor);
        p["ShadowMapSize"].SetValue(L.ShadowMapSize);
        p["ShadowStrength"].SetValue(L.ShadowStrength);

        _fx.CurrentTechnique = _fx.Techniques["Directional"];
        DrawQuad();

        // Point lights as sphere volumes: back faces, no depth test, so a camera inside the volume still works.
        _fx.CurrentTechnique = _fx.Techniques["PointLight"];
        p["UvFlip"].SetValue(UvFlip);
        gd.RasterizerState = RasterizerState.CullClockwise;
        gd.SetVertexBuffer(_sphereVb);
        gd.Indices = _sphereIb;
        foreach (var light in Lights)
        {
            var pos = light.Follow?.Invoke() ?? light.Position;
            float flicker = light.Flicker > 0 ? 1f + light.Flicker * (0.6f * MathF.Sin(time * 23f) + 0.4f * MathF.Sin(time * 7.3f + 1.7f)) : 1f;
            p["PointPosition"].SetValue(pos);
            p["PointColor"].SetValue(light.Color);
            p["PointRadius"].SetValue(light.Radius);
            p["PointIntensity"].SetValue(light.Intensity * flicker);
            p["WorldViewProjection"].SetValue(Matrix.CreateScale(light.Radius * 1.05f) * Matrix.CreateTranslation(pos) * view * proj);
            foreach (var pass in _fx.CurrentTechnique.Passes)
            {
                pass.Apply();
                gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _sphereTris);
            }
        }

        // ---- 3. Composite
        UnbindTextures();
        gd.SetRenderTarget(output);
        gd.BlendState = BlendState.Opaque;
        gd.RasterizerState = RasterizerState.CullNone;
        p["LightTex"].SetValue(_light);
        p["AlbedoTex"].SetValue(_albedo);
        p["NormalTex"].SetValue(_normal);
        p["DepthTex"].SetValue(_depth);
        p["RimColor"].SetValue(L.RimColor);
        p["FogStart"].SetValue(L.FogStart);
        p["FogEnd"].SetValue(L.FogEnd);
        p["FogColor"].SetValue(L.FogColor);
        _fx.CurrentTechnique = _fx.Techniques["Composite"];
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
