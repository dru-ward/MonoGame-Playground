---
name: monogame-deferred-2d-lighting
description: Deferred-style 2D/2.5D lighting pipeline for MonoGame — albedo + normal G-buffer render targets drawn with two SpriteBatch passes (no MRT needed on the GL profile), per-pixel normal-mapped point lights accumulated with an additive BlendState and a scissor RasterizerState, a matrix-free light shader that reconstructs pixel position from UV because the camera never rotates, composite, half-res bloom, final combine, and explicit graphics-state management with debug blits of every intermediate target. Use when adding dynamic lights, normal maps, or multi-pass render-target rendering to a 2D MonoGame game.
---

> Colour space: this pipeline lights and composites directly in sRGB (no linearisation, no tone map); the numbers
> below assume that. Do not combine with the linear-light/tone-mapped conventions in monogame-skinning-shader.

# Deferred-style 2D lighting pipeline

```
Pass 1  AlbedoRT   SpriteBatch(view matrix)  albedo textures
Pass 2  NormalRT   SpriteBatch(view matrix)  SAME geometry, normal-map textures  (clear to (128,128,255))
Pass 3  LightRT    Clear(ambient); per light: additive blend + scissor rect, full-screen quad, PointLight shader
Pass 4  SceneRT    Composite: albedo * light.rgb + light.a(spec)   then additive emissive particles
Pass 5/6 BloomA/B  bright-pass at ½ res, separable blur ping-pong
Pass 7  backbuffer FinalCombine (scene + bloom, optional vignette)
```
Two SpriteBatch passes replace MRT (not available with SpriteBatch on the GL profile).

#> Colour space: this pipeline lights and composites directly in sRGB (no linearisation, no tone map); the numbers
> below assume that. Do not combine with the linear-light/tone-mapped conventions in monogame-skinning-shader.

# Suggested structure
- A `RenderPipeline.RenderFrame(view, zoom, drawScene, lights, drawEmissive, drawOverlay)` that runs all passes and
  calls `drawScene` twice (albedo pass, normal pass).
- A `SceneBatch` wrapper as the only API scene code sees (`DrawTiled/DrawRect/Draw/DrawRotated` on albedo+normal
  `SpritePair`s); it picks the right texture for the current pass.
- A `LightManager` holding persistent lights plus short `Flash()` transients, returning culled, strongest-first lights
  so a single-pass variant can take the top N.
- One `GraphicsStates` object owning every custom state.

#> Colour space: this pipeline lights and composites directly in sRGB (no linearisation, no tone map); the numbers
> below assume that. Do not combine with the linear-light/tone-mapped conventions in monogame-skinning-shader.

# Coordinate trick that keeps the shader matrix-free
Camera never rotates ⇒ view space == render-target pixel space ⇒ tangent-space normals of axis-aligned sprites are already
in screen space. Transform light positions on the CPU and reconstruct pixel position from UV in the shader:

```csharp
_view = Matrix.CreateTranslation(-cam.X, -cam.Y, 0) * Matrix.CreateScale(zoom, zoom, 1) * Matrix.CreateTranslation(w/2f, h/2f, 0);
var sp = Vector2.Transform(light.Position, _view);          // world -> screen px
_pLightPos.SetValue(new Vector3(sp, light.Height * zoom));  // height & radius scale with zoom
_pLightRadius.SetValue(light.Radius * zoom);
```
```hlsl
float3 P = float3(input.TexCoord * ScreenSize, 0);           // pixel on the z=0 plane
float3 N = normalize(tex2D(NormalSampler, input.TexCoord).xyz * 2 - 1);
float3 toL = LightPosition - P; float d = length(toL); float3 L = toL / max(d, 1e-4);
float x = saturate(1 - d*d/(LightRadius*LightRadius)); float atten = x*x * LightIntensity;   // exactly 0 at radius
float NdotL = saturate(dot(N, L));
float3 H = normalize(L + float3(0,0,1));                                                        // camera looks down +Z
float spec = pow(saturate(dot(N,H)), SpecularPower) * SpecularAmount * step(0.001, NdotL);
return float4(LightColor * NdotL * atten, spec * atten * luminance(LightColor));               // rgb diffuse, a spec
```
Normal maps: +X right, +Y **down**, +Z toward viewer (DirectX-style green). Rotated sprites break "tangent==screen" —
draw them in the normal pass through a pixel-shader-only technique that rotates the sampled normal by the sprite's
angle (the batch wrapper can do this automatically when a rotation is given), or use radially symmetric normal maps.

#> Colour space: this pipeline lights and composites directly in sRGB (no linearisation, no tone map); the numbers
> below assume that. Do not combine with the linear-light/tone-mapped conventions in monogame-skinning-shader.

# Render targets

```csharp
_albedoRT = new RenderTarget2D(gd, w, h, false, SurfaceFormat.Color, DepthFormat.None);   // ×4 full res (albedo, normal, light, scene)
_bloomA   = new RenderTarget2D(gd, w/2, h/2, false, SurfaceFormat.Color, DepthFormat.None); // ×2 half res
// recreate when PresentationParameters.BackBuffer size changes (call an EnsureRenderTargets() at top of Draw)
```

#> Colour space: this pipeline lights and composites directly in sRGB (no linearisation, no tone map); the numbers
> below assume that. Do not combine with the linear-light/tone-mapped conventions in monogame-skinning-shader.

# States (create once, never per frame)

```csharp
_additive = new BlendState { ColorSourceBlend = Blend.One, ColorDestinationBlend = Blend.One, ColorBlendFunction = BlendFunction.Add,
                             AlphaSourceBlend = Blend.One, AlphaDestinationBlend = Blend.One, AlphaBlendFunction = BlendFunction.Add };
_tileSampler = new SamplerState { AddressU = TextureAddressMode.Wrap, AddressV = TextureAddressMode.Wrap,
                                  Filter = TextureFilter.Anisotropic, MaxAnisotropy = 8, MipMapLevelOfDetailBias = -0.5f };
_rtSampler   = new SamplerState { AddressU = TextureAddressMode.Clamp, AddressV = TextureAddressMode.Clamp, Filter = TextureFilter.Linear };
_scissor     = new RasterizerState { CullMode = CullMode.None, ScissorTestEnable = true };
_wire        = new RasterizerState { CullMode = CullMode.None, FillMode = FillMode.WireFrame };
_tint        = new BlendState { ColorSourceBlend = Blend.BlendFactor, ColorDestinationBlend = Blend.One,
                                AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.One, BlendFactor = new Color(16,16,16,0) };
```
Restore `BlendState.Opaque` / `DepthStencilState.None` / a solid rasterizer after each pass; SpriteBatch.Begin sets its own.

#> Colour space: this pipeline lights and composites directly in sRGB (no linearisation, no tone map); the numbers
> below assume that. Do not combine with the linear-light/tone-mapped conventions in monogame-skinning-shader.

# Tiled floor with a wrap sampler (one draw call)
```csharp
_spriteBatch.Begin(SpriteSortMode.Deferred, BlendState.Opaque, _tileSampler, DepthStencilState.None, RasterizerState.CullNone, null, _view);
_spriteBatch.Draw(floorTex, new Rectangle(0,0,World,World), new Rectangle(0,0,World,World), Color.White); // source > texture ⇒ tiles
_spriteBatch.End();
```

#> Colour space: this pipeline lights and composites directly in sRGB (no linearisation, no tone map); the numbers
> below assume that. Do not combine with the linear-light/tone-mapped conventions in monogame-skinning-shader.

# Per-light pass with scissor clipping
```csharp
gd.SetRenderTarget(_lightRT); gd.Clear(new Color(0.12f,0.12f,0.17f,0f));  // ambient rgb (starting value), 0 spec
gd.BlendState = _additive; gd.RasterizerState = _scissor; SetRtSamplers(); _pNormalTex.SetValue(_normalRT);
foreach (var l in lights)
{
    if (l.Intensity <= 0) continue;
    var sp = Vector2.Transform(l.Position, _view); float r = l.Radius * zoom;
    var box = Rectangle.Intersect(new Rectangle((int)(sp.X-r), (int)(sp.Y-r), (int)(2*r), (int)(2*r)), new Rectangle(0,0,w,h));
    if (box.Width <= 0 || box.Height <= 0) continue;         // off-screen light is free
    gd.ScissorRectangle = box;
    // set light params...; _effect.CurrentTechnique = Techniques["PointLight"]; DrawFullScreenQuad(w, h);
}
```
Because attenuation hits exactly zero at the radius, the scissor clip is invisible. Alternative single-pass: upload arrays
of ≤8 lights and use an unrolled loop technique (keep a toggle to compare).

#> Colour space: this pipeline lights and composites directly in sRGB (no linearisation, no tone map); the numbers
> below assume that. Do not combine with the linear-light/tone-mapped conventions in monogame-skinning-shader.

# Composite / final
```csharp
gd.SetRenderTarget(_sceneRT); gd.BlendState = BlendState.Opaque;
_pAlbedoTex.SetValue(_albedoRT); _pLightTex.SetValue(_lightRT); technique "Composite"; quad
// then emissive particles additively into _sceneRT so they feed the bloom
// bloom: extract _sceneRT -> _bloomA; blur A->B (H), B->A (V) ×2
gd.SetRenderTarget(null); technique "FinalCombine" (AlbedoTex=_sceneRT, BloomTex=_bloomA or a 1x1 black/white pixel with intensity 0)
```
Always call `Textures[i] = null` before `SetRenderTarget` on an RT that was just sampled. Provide debug keys that `Blit`
each intermediate RT — invaluable when a pass goes black.
