---
name: monogame-hlsl-effects
description: Write and drive custom HLSL .fx effects in MonoGame DesktopGL (vs_3_0/ps_3_0 via MGCB) and DirectX — the cross-platform file skeleton, multi-technique files sharing one vertex shader, unrolled loops over uniform arrays, sampler/register conventions, full-screen quads with a pixel-space orthographic WVP, parameter caching from C#, and the runtime gotchas (HLSL initialisers ignored on OpenGL, render-target/texture binding order, sampler state). Includes bright-pass, separable Gaussian blur and vignette recipes. Use when adding shaders, post-processing or per-pixel lighting to any MonoGame project.
---

# Custom HLSL effects in MonoGame (OpenGL profile)

MonoGame has **no runtime HLSL compiler**: shaders must be `.fx` files built by MGCB (`EffectImporter`/`EffectProcessor`).
On DesktopGL the profile is Shader Model 3 — fixed-count `[unroll]` loops only, no MRT with SpriteBatch, no dynamic constant
indexing in pixel shaders.

## File skeleton (works on both OpenGL and DirectX)

```hlsl
#if OPENGL
    #define SV_POSITION POSITION
    #define VS_SHADERMODEL vs_3_0
    #define PS_SHADERMODEL ps_3_0
#else
    #define VS_SHADERMODEL vs_4_0_level_9_3
    #define PS_SHADERMODEL ps_4_0_level_9_3
#endif

float4x4 WorldViewProjection;
float2   ScreenSize;

// Leave Filter/Address OUT of sampler_state so GraphicsDevice.SamplerStates[n] controls them from C#.
Texture2D AlbedoTex; sampler AlbedoSampler : register(s0) = sampler_state { Texture = <AlbedoTex>; };
Texture2D NormalTex; sampler NormalSampler : register(s1) = sampler_state { Texture = <NormalTex>; };

struct VSInput  { float4 Position : POSITION0;   float2 TexCoord : TEXCOORD0; };
struct VSOutput { float4 Position : SV_POSITION; float2 TexCoord : TEXCOORD0; };

VSOutput MainVS(VSInput i) { VSOutput o; o.Position = mul(i.Position, WorldViewProjection); o.TexCoord = i.TexCoord; return o; }

float4 BlitPS(VSOutput i) : COLOR0 { return float4(tex2D(AlbedoSampler, i.TexCoord).rgb, 1); }

technique Blit { pass P0 { VertexShader = compile VS_SHADERMODEL MainVS(); PixelShader = compile PS_SHADERMODEL BlitPS(); } }
// ...more techniques share MainVS
```

Fixed-count loops over uniform arrays are fine when unrolled:

```hlsl
#define MAX_LIGHTS 8
float3 LightPositions[MAX_LIGHTS]; float3 LightColors[MAX_LIGHTS]; float2 LightRadiusIntensity[MAX_LIGHTS];
[unroll] for (int i = 0; i < MAX_LIGHTS; i++) acc += Shade(P, N, LightPositions[i], LightColors[i], ...);
// unused slots: upload intensity 0 from C# instead of branching on a LightCount uniform
```

## Driving it from C#

Cache parameters once, draw full-screen quads with pixel-space vertices and an orthographic WVP:

```csharp
_effect = Content.Load<Effect>("Shaders/MyEffect");
_pWvp = _effect.Parameters["WorldViewProjection"]; _pAlbedoTex = _effect.Parameters["AlbedoTex"]; // etc.

private readonly VertexPositionTexture[] _quad = new VertexPositionTexture[4];
private static readonly short[] QuadIdx = { 0, 1, 2, 0, 2, 3 };

void DrawFullScreenQuad(int w, int h)
{
    _quad[0] = new(new Vector3(0, 0, 0), new Vector2(0, 0)); _quad[1] = new(new Vector3(w, 0, 0), new Vector2(1, 0));
    _quad[2] = new(new Vector3(w, h, 0), new Vector2(1, 1)); _quad[3] = new(new Vector3(0, h, 0), new Vector2(0, 1));
    // pixel (x,y) -> clip: x_clip = 2x/w - 1, y_clip = 1 - 2y/h   (same matrix SpriteBatch uses)
    _pWvp.SetValue(Matrix.CreateOrthographicOffCenter(0, w, h, 0, 0, 1));
    foreach (var pass in _effect.CurrentTechnique.Passes)
    {
        pass.Apply();
        GraphicsDevice.DrawUserIndexedPrimitives(PrimitiveType.TriangleList, _quad, 0, 4, QuadIdx, 0, 2);
    }
}

// usage
GraphicsDevice.SetRenderTarget(target); GraphicsDevice.BlendState = BlendState.Opaque;
GraphicsDevice.RasterizerState = RasterizerState.CullNone;      // quad winding doesn't matter then
for (int i = 0; i < 4; i++) GraphicsDevice.SamplerStates[i] = SamplerState.LinearClamp;
_pAlbedoTex.SetValue(sourceRT);
_effect.CurrentTechnique = _effect.Techniques["Blit"];
DrawFullScreenQuad(target.Width, target.Height);
```

Textures are bound by setting the `Texture2D` parameter (`AlbedoTex`), not by name of the sampler. Array params take
`Vector3[]`, `Vector2[]`, `float[]` and must have exactly the declared length.

## Gotchas (all hit in practice)
1. **HLSL initialisers are NOT applied at runtime** on the OpenGL effect path (`float Exposure = 1.0;` reads as 0 → black
   output). Set every scalar the shaders depend on explicitly from C# in `LoadContent`.
2. Before `SetRenderTarget(rt)` make sure `rt` is not still bound as a texture from a previous pass:
   `for (int i = 0; i < 4; i++) GraphicsDevice.Textures[i] = null;`
3. If the technique uses samplers s0 and s2 only, still set `SamplerStates[0..3]` — cheap and avoids surprises.
4. Keep C# constants in sync with `#define`s (`MaxLights == MAX_LIGHTS`, `BlurTaps == BLUR_TAPS`).
5. Debug a black frame by adding a `Blit` technique and an env-var/keys to view each intermediate RT
   (see monogame-headless-screenshots for capturing frames from the command line).

## Post-processing recipes
- **Bright pass (soft knee):** `knee = T*k; soft = clamp(b - T + knee, 0, 2knee); soft = soft²/(4knee); c *= max(soft, b-T)/max(b,ε)`.
- **Separable Gaussian:** CPU computes `weights[i] = exp(-x²/2σ²)` normalised; offsets `(i-7)/width, 0` then `0, (i-7)/height`;
  ping-pong two half-res RTs, 2 iterations.
- **Vignette:** `d = length(uv-0.5)*1.4142; color *= 1 - smoothstep(R-soft, R, d) * strength`.
