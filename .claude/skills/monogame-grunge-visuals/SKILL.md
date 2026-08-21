---
name: monogame-grunge-visuals
description: Procedural "worn surface" visuals for a 2D/top-down MonoGame scene without art assets — a seamless ground tile built from a periodic-noise height field (aggregate grain, masked ridged-noise cracks, tile-edge joints, hashed depressions) with a derived normal map and layered albedo (low-frequency dirt, stains, specks, eroded paint lines); rectangular prop sprites with a noise-based grime multiplier and a transposed variant; flickering/buzzing point lights driven by noise; ember/smoke emitters; a post-process colour grade (desaturate, split-tone, contrast, animated film grain, vignette); and ambient haze particles. Use when a MonoGame scene needs a weathered, atmospheric look generated entirely in code.
---

# Procedural worn-surface visuals

## 1. Seamless ground tile (height field -> normal + albedo, 512 px)
Build a height field from **periodic** value noise so the tile wraps; every term uses a noise period equal to its
frequency (`Noise(u*f, v*f, period: f)`). Starting values:
```csharp
grain   = Noise(u*128, v*128, 128)*0.06 + Noise(u*256, v*256, 256)*0.03          // fine aggregate
r1      = 1 - |2*Noise(u*4+3.1, v*4+1.7, 4) - 1|;                                // ridged noise (cellular lines)
crack   = clamp((r1 - 0.986)/0.012) * clamp((Noise(u*3+.5, v*3+8.5, 3) - 0.56)/0.12);  // THIN threshold x low-freq MASK
joint   = 1 - clamp(min(distToTileEdgeX, distToTileEdgeY)/5)                     // groove along the tile border
hole    = max over 3 hashed positions of clamp(1 - wrappedDist/radius * edgeNoise) // depressions
h = 0.6 + grain - crack*0.25 - joint*0.3 - smoothstep(hole)*0.5;
// normal map from the height field: strength 1.5, wrap: true (sample neighbours modulo size)
```
Albedo layering, in order: `base ± fine noise`, `× (0.78 + 0.45*lowFreqNoise)` (dirt variation), lighter dusty
patches, elliptical stains (wrapped distance, `× (1 - stain*0.75)`), cracks -> very dark, holes -> a different,
lighter material, ~1.5 % random single-pixel specks, optional painted line (`|y - size/2| < 5`, dash 120 on / 200 off
px) eroded by noise and by the crack mask.

Lesson learned: ridged value noise is cellular. Without a strong low-frequency mask and a tight threshold the whole
floor becomes a dark maze of lines. Start sparse and add cracks last.

## 2. Props as grime-noised shape sprites
A `ShapeSprite(width, height, transpose)` builder composes boxes, ellipses, capsules and raised strips into an
albedo/normal pair; `transpose` produces the 90-degree-rotated variant of long props for free. Grime is a multiplier on
the albedo:
```
albedo *= 1 - GrimeAmount * (0.7*Noise(p*GrimeScale + GrimeSeed) + 0.3*fineNoise)
```
Useful parts: corrugation = raised strips every ~10 px; chipped edges = small dark ellipses at the ends; a wear bloom =
low-frequency noise blended toward a second tint at one end; stacked bags = staggered rows of capsules with a full
dome and alternating tint; debris = footprint ellipse + ellipse rocks + box chunks + thin capsule bars.
Keep prop definitions data-driven (`Size, IsLong, LootChance, Name`) so the world generator stays generic; long props
can spawn a second segment end-to-end; layout should avoid the spawn point, other props and lights.

## 3. Dynamic lights that flicker
- Lamps on a jittered grid; give every Nth lamp a different colour temperature, and make some "buzz":
  `Intensity = Noise(t*14 + phase) > 0.82 ? lowValue : normalValue`.
- Fire sources: `n = 0.6*Noise(t*9 + phase) + 0.4*Noise(t*23)`; `Intensity = base*(0.75 + 0.5*n)`;
  `Radius = r0 + 50*n`. Emit ~22 particles/s: 75 % emissive embers (small, rise fast, gravity around -60),
  25 % non-emissive smoke (large, slow, 2-4 s life).
- Keep a dim, warm player light so the player is readable without flattening the lamp pools.

## 4. Colour grade (final pixel shader; set all params from C# — .fx initialisers are ignored on OpenGL)
```hlsl
float lum = dot(color, float3(.299,.587,.114));
color = lerp(color, lum.xxx, Desaturate);                                       // ~0.3 starting value
color = lerp(color * GradeShadows, color * GradeHighlights, saturate(lum*1.6));  // split-tone: one tint for shadows, another for highlights
color = (color - 0.5) * Contrast + 0.5;                                          // ~1.08
float2 gp = uv * ScreenSize + frac(Time*0.37)*1000;                              // animate the grain
float grain = frac(sin(dot(gp, float2(12.9898,78.233))) * 43758.5453) - 0.5;
color += grain * GrainAmount * (1 - lum*0.6);                                    // ~0.07, mostly in the darks
// vignette: d = length(uv-0.5)*1.4142; color *= 1 - smoothstep(R-soft, R, d)*strength   (start R .85, soft .55, strength .62)
```
`ScreenSize` and `Time` must be uploaded before the final pass every frame.

## 5. Atmosphere
Emit ~12 haze puffs/s at random points of the visible world: size 30-70 px, low alpha, 3-6 s life, slow drift, no
drag. They pick up the point lights and make the air read as dusty. Keep particle tints near neutral so the grade
decides the mood.

## Verification
Take a headless screenshot (see monogame-headless-screenshots) with a zoomed-out camera after a few seconds of
warm-up and check: floor is not a maze, light pools are visible, smoke/embers move, props are readable, grain is
subtle. If the frame is too dark, raise ambient and light intensity before touching the grade.
