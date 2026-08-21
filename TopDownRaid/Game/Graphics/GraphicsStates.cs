using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game.Graphics;

/// <summary>
/// All custom Blend / Sampler / Rasterizer states, created once. State objects are immutable after first use, so
/// they must never be created per frame.
/// </summary>
public sealed class GraphicsStates : System.IDisposable
{
    /// <summary>result = src*1 + dst*1 — light accumulation and emissive particles.</summary>
    public BlendState AdditiveLight { get; }
    /// <summary>result = src*BlendFactor + dst — dim constant overlay (scissor visualisation).</summary>
    public BlendState Tint { get; }
    /// <summary>Wrap + anisotropic + sharpened mip bias — the tiled floor.</summary>
    public SamplerState TileAnisoWrap { get; }
    /// <summary>Clamp + linear — sampling render targets (never wrap: edge bleeding).</summary>
    public SamplerState RtLinearClamp { get; }
    public RasterizerState ScissorNoCull { get; }
    public RasterizerState Wireframe { get; }
    public RasterizerState SolidNoCull { get; }

    public GraphicsStates()
    {
        AdditiveLight = new BlendState
        {
            Name = "AdditiveLight",
            ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One, ColorBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add,
        };
        Tint = new BlendState
        {
            Name = "ScissorTint",
            ColorSourceBlend = Blend.BlendFactor, ColorDestinationBlend = Blend.One, ColorBlendFunction = BlendFunction.Add,
            AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add,
            BlendFactor = new Color(16, 16, 16, 0),
        };
        TileAnisoWrap = new SamplerState
        {
            Name = "TileAnisoWrap",
            AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap, AddressW = TextureAddressMode.Wrap,
            Filter = TextureFilter.Anisotropic, MaxAnisotropy = 8, MipMapLevelOfDetailBias = -0.5f,
        };
        RtLinearClamp = new SamplerState
        {
            Name = "RtLinearClamp",
            AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, AddressW = TextureAddressMode.Clamp,
            Filter = TextureFilter.Linear,
        };
        ScissorNoCull = new RasterizerState { Name = "ScissorNoCull", CullMode = CullMode.None, ScissorTestEnable = true };
        Wireframe     = new RasterizerState { Name = "Wireframe",     CullMode = CullMode.None, FillMode = FillMode.WireFrame };
        SolidNoCull   = new RasterizerState { Name = "SolidNoCull",   CullMode = CullMode.None };
    }

    public void Dispose()
    {
        AdditiveLight.Dispose(); Tint.Dispose(); TileAnisoWrap.Dispose(); RtLinearClamp.Dispose();
        ScissorNoCull.Dispose(); Wireframe.Dispose(); SolidNoCull.Dispose();
    }
}
