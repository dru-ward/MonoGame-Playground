---
name: monogame-skinning-shader
description: Custom HLSL effect for GPU-skinned characters in MonoGame (DesktopGL and DX) — 4-bone float4x3 palette skinning, wrap-diffuse key light, hemisphere ambient, fill, Blinn-Phong + Fresnel from per-vertex material, rim light, 3x3 PCF shadow mapping into a Single render target, object-space procedural grain, fog, exposure tone-map with correct sRGB handling. Includes the MGCB/DesktopGL gotchas (OPENGL macros, vs_3_0 register budget, SpriteBatch state reset). Use when characters need better lighting than BasicEffect/SkinnedEffect or need shadows.
---

# Skinning + lighting shader for MonoGame characters

## Why not SkinnedEffect
`SkinnedEffect` does palette skinning (72 bones) but only 3 directional lights, no shadows, no rim, no tone map
and no per-vertex material control. A custom `.fx` built by the content pipeline gives all of that and is ~150
lines. The `SetValue(Matrix[])` on a `float4x3 Bones[N]` parameter works exactly as SkinnedEffect uses it.

## Content pipeline facts
* Add the effect to `Content.mgcb` with `EffectImporter`/`EffectProcessor`; MGCB compiles HLSL → GLSL (MojoShader)
  for DesktopGL on Windows without extra tooling. `dotnet tool restore` first (tools in `.config/dotnet-tools.json`).
* Guard profiles:
  ```hlsl
  #if OPENGL
      #define SV_POSITION POSITION
      #define VS_SHADERMODEL vs_3_0
      #define PS_SHADERMODEL ps_3_0
  #else
      #define VS_SHADERMODEL vs_4_0_level_9_3
      #define PS_SHADERMODEL ps_4_0_level_9_3
  #endif
  ```
* `vs_3_0` has 256 float4 constant registers: `float4x3 Bones[64]` (192) + matrices fits; `Bones[72]` also fits
  if the rest stays small. Use `float4x3`, not `float4x4`.
* Loops in `ps_3_0` must be `[unroll]`-able (the 3×3 PCF loop is fine).
* A `SpriteFont` (`FontDescriptionImporter`) builds from an installed TTF — fine for a dev HUD on one machine, but it
  ties the content build to that machine's fonts; monogame-hud-pixel-font has the portable, asset-free alternative.

## Skinning (vertex shader)
```hlsl
float4x3 Bones[64];
void Skin(inout float4 pos, inout float3 nrm, float4 idx, float4 wgt) {
    float4x3 skin = Bones[idx.x]*wgt.x + Bones[idx.y]*wgt.y + Bones[idx.z]*wgt.z + Bones[idx.w]*wgt.w;
    pos.xyz = mul(pos, skin);  nrm = mul(nrm, (float3x3)skin);
}
```
Pass the palette as `InverseBind * World` per bone (row-vector convention, matches `mul(pos, skin)`).
Static geometry (floor) uses the same shader with a one-element identity palette — no second effect needed.
Keep the **pre-skin object-space position** in an interpolator: object-space texturing/grain then sticks to the
skinned surface instead of swimming.

## Lighting model that worked for stylised characters
Per vertex: `Color` = albedo (sRGB), `Material.x` = specular strength, `Material.y` = shininess 0..1.
```
albedo  = pow(color, 2.2) * grain
diffuse = saturate((N·L + wrap) / (1 + wrap))     wrap 0.15–0.25 (softer terminator on skin/cloth)
ambient = lerp(GroundColor, SkyColor, N.y*0.5+0.5)  (hemisphere)
fill    = FillColor * saturate(N·(-FillDir)*0.5+0.5)
spec    = pow(N·H, lerp(8,160,shininess)) * specStrength * shadow * saturate(N·L*4)
          * (1 + 2*pow(1-N·V,3)*specStrength)        (Fresnel boost, metals pop at grazing angles)
rim     = pow(1 - N·V, 3.5) * RimColor * (0.15 + 0.85*albedo)   (scale rim by albedo or dark cloth looks hazy)
color   = albedo*(diffuse*shadow*LightColor + fill + ambient) + LightColor*spec + rim
fog, then tone map: color = 1 - exp(-color * 1.5); color = pow(color, 1/2.2)
```
* Materials as presets: Skin (0.18, 0.25), Cloth (0.04, 0.08), Leather (0.35, 0.35), Metal (1.0, 0.85),
  Hair (0.3, 0.45), Eye (0.8, 0.95).
* Key light ≈ 1.7× white-warm, fill ≈ 0.16–0.24 cool, sky 0.2–0.28, ground 0.07–0.1, rim 0.28–0.42. Lower
  values look washed out; the first attempt with rim 0.55 and ambient 0.3 read as grey haze.
* Clear the backbuffer with the tone-mapped fog colour (apply the same curve in C#) so fog blends seamlessly.
* Scope: this is a linear-light pipeline (3D, tone-mapped). The 2D skills (monogame-deferred-2d-lighting,
  monogame-hlsl-effects post-processing) deliberately light in sRGB space with no linearise/encode step — mixing the two
  conventions inside one pipeline is the conflict to avoid, not either choice on its own.
* **Double gamma is the classic mistake**: vertex colours are already sRGB. Linearise them, light in linear, and
  gamma-encode once at the end. Skipping the linearise step makes everything pastel/overexposed.

## Shadow map
* `RenderTarget2D(device, 2048, 2048, false, SurfaceFormat.Single, DepthFormat.Depth24, 0, DiscardContents)` works on
  DesktopGL HiDef. Clear to white (depth 1).
* Second technique `ShadowCaster`: same skinning, output `pos.z / pos.w` from an orthographic light
  `CreateLookAt(center - lightDir*12, center, Up)` × `CreateOrthographic(8.5, 8.5, 1, 24)` sized to the scene.
* Sample: `uv = (proj.x*0.5+0.5, 0.5 - proj.y*0.5)`; outside 0..1 → lit. Slope-scaled bias
  `max(0.0015, 0.006*(1-N·L))`, 3×3 PCF with `texel = 1/size`. `RasterizerState.CullNone` during the shadow pass
  avoids holes from thin/one-sided parts.
* `ShadowStrength` parameter (0.9) lets shadows stay slightly transparent, which reads better than pure black.

## Procedural grain texture
256² value noise (3 octaves, lattice modulo for tiling) + a little white noise, generated into a `Texture2D` at
startup. Sample twice with object-space `xy` and `zy` (poor-man's biplanar) and modulate albedo by
`lerp(1, 0.75+0.5*g, 0.35)`. Breaks up flat vertex colours at almost no cost.

## Render loop order and state
1. Shadow pass → `SetRenderTarget(shadowMap)`, `DepthStencilState.Default`, `CullNone`, draw everything.
2. `SetRenderTarget(null)` (or an MSAA offscreen target for screenshots), clear, `CullCounterClockwise`,
   set all params once, draw floor then characters (`World`, `Bones` per draw).
3. HUD with `SpriteBatch`, then **restore `DepthStencilState.Default` and `BlendState.Opaque`** — SpriteBatch
   leaves depth off and alpha blending on, which silently breaks the next frame's 3D pass.
* `GraphicsProfile.HiDef`, `PreferMultiSampling = true` and `PresentationParameters.MultiSampleCount = 8` in
  `PreparingDeviceSettings` give MSAA on DesktopGL.
* Sampler slots: declare samplers with only `Texture = <...>` in the `.fx` (no Filter/Address — see
  monogame-hlsl-effects) and set `SamplerStates[0] = PointClamp` (shadow map) and `SamplerStates[1] = LinearWrap`
  (grain) from C#; filter settings written inside `sampler_state` are not reliably applied on the OpenGL path.
* No HLSL initialisers (`float Wrap = 0.25;`) — they are ignored on OpenGL; set every parameter from C# each frame.
