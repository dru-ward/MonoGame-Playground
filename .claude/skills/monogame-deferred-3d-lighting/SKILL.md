---
name: monogame-deferred-3d-lighting
description: Deferred lighting for 3D scenes in MonoGame (DesktopGL verified) — one geometry pass writes a G-buffer through multiple render targets (albedo+specular strength, world normal+shininess, clip depth), a half-float light buffer accumulates a shadowed directional/ambient full-screen pass plus additive sphere-volume point lights that reconstruct world position from depth with the inverse view-projection, and a full-screen composite applies rim, emissive, fog and tone mapping. Covers the MRT rules, the clear-colour trick for the depth target, deriving screen UV from clip position inside a volume pass, light-buffer format, balancing many coloured lights against a key light, debug blits, and what you give up (MSAA). Use when a 3D MonoGame scene needs more than a couple of lights, per-pixel lighting decoupled from geometry complexity, or lights attached to animated bones/props.
---

# Deferred 3D lighting in MonoGame

Built and verified on DesktopGL (OpenGL path, HiDef profile) on top of the skinned character renderer in
monogame-skinning-shader; the forward path was kept on a toggle for comparison.

## Pass layout
```
1  G-buffer   SetRenderTargets(albedo, normal, depth)   geometry pass (skinning happens here, once)
2  Light RT   clear 0; Directional+ambient full-screen quad; per point light: sphere volume, additive
3  Composite  full-screen quad -> back buffer / screenshot target: albedo*light + spec + rim + emissive, fog, tone map
4  HUD        SpriteBatch as usual (restore depth/blend afterwards)
```
The shadow map for the key light is rendered before pass 1 exactly as in the forward path and sampled in pass 2.

## G-buffer (MRT) facts
* Pixel shader returns a struct with `COLOR0..COLOR2` semantics; MGCB/MojoShader compile this for `ps_3_0` and
  `GraphicsDevice.SetRenderTargets(rt0, rt1, rt2)` works on DesktopGL.
* All bound targets must share size and multisample count. Deferred therefore **gives up MSAA** (0 samples); the
  forward path keeps 8× MSAA, which is the main reason to keep both.
* Only the first target needs a depth buffer (`DepthFormat.Depth24`); the rest use `DepthFormat.None`.
* Formats that worked: albedo `Color` (rgb = sRGB albedo, a = specular strength), normal `Color`
  (rgb = N*0.5+0.5, a = shininess 0..1), depth `Single` (clip `z/w` from a `ClipPos : TEXCOORDn` interpolator —
  `SV_POSITION` cannot be read in `ps_3_0`).
* `Clear` applies one colour to every bound target. Clearing to **white** makes the depth target read 1.0, which
  every later pass treats as "nothing here" (`if (depth >= 0.99999) discard/return 0`), and the white albedo/normal
  are never read. Pick the clear colour for the channel that needs a sentinel.
* Before `SetRenderTargets`, set `Textures[i] = null` for every slot that sampled those targets last frame.
* Sampler registers are assigned by usage per technique, so the grain texture that is `s1` in the forward technique
  may be `s0` in the G-buffer technique — set all of `SamplerStates[0..3]` to the wrap/linear state for that pass.
* Emissive flag without an extra target: write shininess = 1 and specular = 0 for glow materials and test
  `normal.a > 0.995 && albedo.a < 0.005` in the composite. Cheap, and no real material uses that combination.

## World position from depth
```hlsl
float3 ReconstructWorld(float2 uv, float depth)   // depth = clip z/w as stored
{
    float4 ndc = float4(uv.x * 2 - 1, (1 - uv.y) * 2 - 1, depth, 1);
    float4 p = mul(ndc, InvViewProjection);      // Matrix.Invert(view * proj), row-vector convention
    return p.xyz / p.w;
}
```
Works with the non-linear z/w directly; no linearisation needed.

## Directional + ambient (full-screen quad)
Quad = 4 `VertexPositionTexture` in clip space (−1..1) with uv (0,0) at top-left, drawn with
`DrawUserPrimitives(TriangleStrip)`; the VS passes position through untouched. The PS reads N, P, material, then
does the same wrap-diffuse / Blinn-Phong / hemisphere ambient / fill as the forward shader and PCF-samples the shadow
map with the reconstructed world position. Output `float4(diffuse rgb, spec)` — keep spec scalar in alpha and
multiply by the light colour luminance.

## Point lights as sphere volumes
* Low-poly unit sphere (12×18, `VertexPosition`, 16-bit indices), `WorldViewProjection = Scale(r*1.05) *
  Translate(pos) * view * proj`.
* `RasterizerState.CullClockwise` (draw back faces) + `DepthStencilState.None`: correct whether the camera is
  outside or inside the volume, no near-plane clipping case.
* Screen UV inside the volume PS comes from the interpolated clip position:
  `uv = (ndc.x*0.5+0.5, 0.5 - ndc.y*0.5)` — the DirectX convention is the correct one on DesktopGL too when
  rendering into a render target (verified by flipping the sign: the flipped version samples a vertically mirrored
  G-buffer and paints ghost images).
* Attenuation `x = saturate(1 - d²/r²); atten = x² * intensity` is exactly 0 at the radius, so the slightly oversized
  volume edge is invisible.
* Additive blend (`One/One` for colour and alpha) into the light buffer.
* A light can follow a bone: `Position = Vector3.Transform(localOffset, bone.World * character.World)` evaluated
  each frame (the orb on a staff tip keeps working when the weapon is sheathed because the weapon bone itself moves).
* Flicker: `1 + amp*(0.6 sin 23t + 0.4 sin(7.3t+1.7))`.

## Light buffer format
`SurfaceFormat.HalfVector4` render target works on DesktopGL HiDef and is needed: a `Color` target clips at 1.0 and
with a key light around 1.7 plus several point lights at 3–5 the sum exceeds that almost everywhere. Fall back to
`Color` in a try/catch and report which one is in use on the HUD.

## Composite
`albedo_linear = pow(albedo, 2.2)`; `color = albedo*light.rgb + light.a + rim*(0.15+0.85*albedo)`; emissive override;
fog by reconstructed distance; `1 - exp(-1.5*color)` then `pow(1/2.2)`. Background pixels (depth sentinel) output
the fog colour through the same tone map so the horizon matches.

## Balancing — the bug that was not a bug
With the forward key light (≈1.7) the point lights (1–1.6) appeared to do nothing on the floor. Blitting the light
buffer showed the floor already at ≈2.0 (white) so the coloured pools were invisible except on shadowed tile-gap
faces; the characters did show faint tints. Fix was balance, not code: in deferred mode drop the key to a cool
≈0.4–0.55, ambient/fill to roughly a third, raise point intensities to 3–5, and lighten a very dark floor albedo.
Rule of thumb with this tone map: a point light needs `intensity * albedo_linear` ≳ 0.15 at the surface to read.

## Debugging
* `--debug albedo|normal|light` option that `SpriteBatch.Draw`s the chosen target full-screen after the composite
  — one capture each was enough to prove normals were right and diagnose the balance issue above.
* Keep the forward path behind a key (`B`) so any deferred artefact can be A/B'd immediately.
* Screenshots via monogame-headless-screenshots: the composite writes into the `--shot` MSAA target without issue.

## Cost / limits observed
* Five lights on a 1600×900 buffer with ~51k triangles is negligible; the per-light cost is fill-rate (volume
  pixels × G-buffer reads), so keep radii tight.
* No per-point-light shadows; only the key light is shadowed. Transparency needs a forward pass after the
  composite (none here). MSAA is gone — supersample the buffers or add an FXAA pass if edges matter.
