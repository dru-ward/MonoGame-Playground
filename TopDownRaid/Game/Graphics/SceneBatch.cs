using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game.Graphics;

/// <summary>
/// Thin wrapper over SpriteBatch used by the two G-buffer passes. Callers draw scene sprites once through this API
/// and it picks the albedo or the normal texture depending on the current pass. Rotated sprites in the normal pass
/// are flushed through the SpriteNormalRotate pixel shader so their tangent-space normals are rotated with them.
/// </summary>
public sealed class SceneBatch
{
    private readonly SpriteBatch _sb;
    private readonly Effect _effect;
    private readonly EffectParameter _pNormalRotation;
    private readonly GraphicsStates _states;
    private Matrix _view;
    private bool _open;
    private BlendState _blend = BlendState.AlphaBlend;
    private SamplerState _sampler = SamplerState.LinearClamp;

    public bool NormalPass { get; private set; }

    public SceneBatch(SpriteBatch sb, Effect effect, GraphicsStates states)
    {
        _sb = sb; _effect = effect; _states = states;
        _pNormalRotation = effect.Parameters["NormalRotation"];
    }

    /// <summary>Starts a pass. Draw calls between Begin and End go to whichever render target is bound.</summary>
    public void Begin(Matrix view, bool normalPass)
    {
        _view = view; NormalPass = normalPass;
        Open(BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    public void End() { if (_open) { _sb.End(); _open = false; } }

    private void Open(BlendState blend, SamplerState sampler, Effect? fx = null)
    {
        if (_open) _sb.End();
        _blend = blend; _sampler = sampler;
        _sb.Begin(SpriteSortMode.Deferred, blend, sampler, DepthStencilState.None, RasterizerState.CullNone, fx, _view);
        _open = true;
    }

    private void EnsureDefault()
    {
        if (!_open || _blend != BlendState.AlphaBlend || _sampler != SamplerState.LinearClamp) Open(BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    /// <summary>Tiled floor: source rect larger than the texture + wrap sampler ⇒ GPU repeats it (one draw call).</summary>
    public void DrawTiled(SpritePair tex, Rectangle worldRect)
    {
        Open(BlendState.Opaque, _states.TileAnisoWrap);
        _sb.Draw(NormalPass ? tex.Normal : tex.Albedo, worldRect, new Rectangle(0, 0, worldRect.Width, worldRect.Height), Color.White);
        Open(BlendState.AlphaBlend, SamplerState.LinearClamp);
    }

    /// <summary>Axis-aligned sprite stretched into a rectangle. Tint only affects the albedo pass.</summary>
    public void DrawRect(SpritePair tex, Rectangle worldRect, Color? tint = null)
    {
        EnsureDefault();
        _sb.Draw(NormalPass ? tex.Normal : tex.Albedo, worldRect, NormalPass ? Color.White : (tint ?? Color.White));
    }

    /// <summary>Axis-aligned sprite by centre position and scale.</summary>
    public void Draw(SpritePair tex, Vector2 worldPos, float scale, Color? tint = null)
    {
        EnsureDefault();
        _sb.Draw(NormalPass ? tex.Normal : tex.Albedo, worldPos, null, NormalPass ? Color.White : (tint ?? Color.White), 0f,
                 tex.Origin, scale, SpriteEffects.None, 0f);
    }

    /// <summary>
    /// Rotated sprite. In the normal pass this costs a batch flush (Begin/End with the rotation shader), so keep
    /// rotated sprites to characters and props, not particles.
    /// </summary>
    public void DrawRotated(SpritePair tex, Vector2 worldPos, float rotation, float scale, Color? tint = null, bool rotateNormals = true)
    {
        if (NormalPass && rotateNormals)
        {
            _pNormalRotation.SetValue(new Vector2(MathF.Cos(rotation), MathF.Sin(rotation)));
            _effect.CurrentTechnique = _effect.Techniques["SpriteNormalRotate"];
            Open(BlendState.AlphaBlend, SamplerState.LinearClamp, _effect);
            _sb.Draw(tex.Normal, worldPos, null, Color.White, rotation, tex.Origin, scale, SpriteEffects.None, 0f);
            Open(BlendState.AlphaBlend, SamplerState.LinearClamp);
            return;
        }
        EnsureDefault();
        _sb.Draw(NormalPass ? tex.Normal : tex.Albedo, worldPos, null, NormalPass ? Color.White : (tint ?? Color.White), rotation,
                 tex.Origin, scale, SpriteEffects.None, 0f);
    }
}
