---
name: monogame-grunge-visuals
description: Give a MonoGame top-down scene a grungy urban / night-raid look (Tarkov-style) entirely procedurally — seamless cracked-asphalt floor with joints, potholes, oil stains and grime; urban props (containers, jersey barriers, sandbags, rubble, burning barrels, lamp bases) as grime-noised shape sprites in both orientations; sodium/fluorescent lamps and flickering fire barrels with embers and smoke; a desaturate + split-tone + contrast + film-grain + vignette colour grade in the final shader; drifting haze particles. Use when a MonoGame scene needs a dirty, moody, industrial atmosphere.
---

# Grungy urban visuals (procedural)

## 1. Cracked asphalt tile (`TextureFactory.CreateAsphaltAlbedo/Normal`, 512 px, seamless)
Height field (`AsphaltHeight`), all periodic noise so it wraps:
```csharp
grain   = Noise(u*128, v*128, 128)*0.06 + Noise(u*256, v*256, 256)*0.03          // aggregate
r1      = 1 - |2*Noise(u*4+3.1, v*4+1.7, 4) - 1|;                                // ridged noise
crack   = clamp((r1 - 0.986)/0.012) * clamp((Noise(u*3+.5, v*3+8.5, 3) - 0.56)/0.12);  // THIN and MASKED
joint   = 1 - clamp(min(distToTileEdgeX, distToTileEdgeY)/5)                     // expansion joint at the tile border
pothole = max over 3 hashed positions of clamp(1 - wrappedDist/radius * edgeNoise)
h = 0.6 + grain - crack*0.25 - joint*0.3 - smoothstep(pothole)*0.5;   normal strength 1.5, wrap:true
```
Albedo: `base 0.30 ± fine noise`, `× (0.78 + 0.45*lowFreqGrime)`, lighter dusty patches, two elliptical **oil stains**
(wrapped distance, ×(1-stain*0.75)), cracks → tar 0.06, potholes → lighter gravel 0.30, 1.5 % random **gravel specks**,
a worn **dashed lane line** (`|y - size/2| < 5`, dashes 120/200 px, paint eroded by noise and cracks).
Lesson learned: ridged value noise is cellular — without a strong low-frequency mask and a tight threshold the floor
turns into a dark maze. Start sparse; add cracks last.

## 2. Props as grime-noised shape sprites (`Graphics/PropArt.cs`, `World/PropKind`)
`ShapeSprite(width, height, transpose)` gives rectangular sprites and a 90°-rotated variant for long props;
`GrimeAmount/GrimeScale/GrimeSeed` multiply the albedo by `1 - amount*(0.7*noise + 0.3*fineNoise)`.
| Prop | Size | Recipe |
|---|---|---|
| Container | 224×112 | steel box, 10 px corrugation ridges (raised strips), door end + handle, rusty end + rust bloom, grime .5 |
| Barrier | 192×56 | wide base box, narrower raised top, chipped ends (small dark ellipses), lifting slots |
| Sandbags | 128×56 | two staggered rows of capsules, dome 1.0, alternating tint |
| Rubble | 96×96 | dust footprint ellipse, rocks (ellipses), concrete chunks (boxes), rebar (thin rust capsules) |
| Fire barrel | 40×40 | rust rim ring, drum top, dark opening — fire = particles + light |
| Lamp base | 32×32 | concrete disc + steel pole cap (non-blocking decor) |
`PropDefs` (Size, IsLong, LootChance, Name) keeps the world generator data-driven; barriers spawn a second segment
end-to-end 50 % of the time; layout avoids the spawn, other props and lamps.

## 3. Urban light set (host)
- **Ambient** cold and low: `(0.085, 0.09, 0.115)`.
- **Sodium lamps**: jittered 3×3 grid, colour `(1.0, .74, .42)`, radius 760, height 220, intensity 1.55; every 4th is a
  cold flood `(.72, .82, 1.0)`, radius 860; every 3rd "buzzes": `Intensity = Noise(t*14+phase) > 0.82 ? 0.4 : 1.55`.
- **Fire barrels** (one per FireBarrel prop): `(1.0, .55, .22)`, height 55, `Intensity = 1.3*(0.75+0.5*n)`,
  `Radius = 340+50n` with `n = 0.6*Noise(t*9+phase) + 0.4*Noise(t*23)`; 22 emissions/s: 75 % embers (emissive, orange,
  rise fast, Gravity -60), 25 % smoke (non-emissive grey, big, slow, 2–4 s).
- Player headlamp warm-white, dim (0.95); muzzle/ricochet flashes stay.

## 4. Colour grade (FinalCombine PS; params set from C# — .fx initialisers are ignored)
```hlsl
float lum = dot(color, float3(.299,.587,.114));
color = lerp(color, lum.xxx, Desaturate);                                     // 0.30
color = lerp(color * GradeShadows, color * GradeHighlights, saturate(lum*1.6)); // (.82,.90,1.05) / (1.10,1.02,.90)
color = (color - 0.5) * Contrast + 0.5;                                        // 1.08
float2 gp = uv * ScreenSize + frac(Time*0.37)*1000;
float grain = frac(sin(dot(gp, float2(12.9898,78.233))) * 43758.5453) - 0.5;   // animated
color += grain * GrainAmount * (1 - lum*0.6);                                  // 0.07, mostly in the darks
// then vignette: radius .85, softness .55, strength .62
```
Set `ScreenSize` and `Time` before the final pass (the pipeline exposes `Time`; host sets it each frame).

## 5. Atmosphere
Emit ~12 haze puffs/s at random points of the visible world: size 30–70, dim grey, 3–6 s life, slow drift, no drag —
they catch the lights and make the air look dirty. Keep enemy kits dark (black/olive/grey) and player olive/tan.

## Verification
`GAME1_ZOOM=0.7 GAME1_SHOT_DELAY=4 GAME1_SCREENSHOT=out.png` — check: floor is not a maze, lamp pools visible, barrels
smoke, props readable, grain subtle. If it's too dark, raise `Ambient` and lamp intensity before touching the grade.
