---
name: monogame-procedural-vegetation
description: Procedural ground cover for a 3D MonoGame scene with no assets — grass blades, a tall golden field band with seed heads, wildflowers in species patches and lumpy bushes, all generated into one static vertex buffer and animated only in the vertex shader (travelling wind gusts, shimmer, and trampling that flattens blades around the player). Covers the per-vertex bend-weight channel packed into colour alpha alongside tree-foliage flutter, blade geometry that lights like ground not walls, clump/patch placement with value noise, keep-outs for plaza/trunks, skipping the shadow pass, bush collision, and measured cost (≈18k blades at ~0.03 ms CPU, 0 B/frame). Use when a MonoGame scene needs grass, meadows, fields, flowers or bushes that move, or any cheap mass-vegetation effect.
---

# Procedural grass, fields, flowers and bushes (MonoGame)

Built for the CharacterModels scene on top of the skinned-character effect; works with any vertex shader that
can read a per-vertex weight and a few uniforms. All CPU work happens once at load; per frame it is one draw
call and 0 B allocated.

## 1. One static mesh, all motion in the vertex shader
Grass is far too numerous to animate on the CPU or with bones. Pack a **bend weight** into a channel the
vertex already carries (colour alpha here, which the tree foliage already used for flutter) and do everything
in `VS`:
```
alpha >= 0.5 : foliage flutter range  (trees, bushes)  amount = (1 - a) * 2
alpha <  0.5 : ground-cover bend       (grass, flowers) bend   = (0.5 - a) * 2   (0 at the root, 1 at the tip)
```
```hlsl
float along = dot(worldPos.xz, WindDirection.xz);
float gust  = 0.5 + 0.5 * sin(Time * 0.7 - along * 0.35 + sin(worldPos.x * 0.4 + worldPos.z * 0.6) * 1.5); // gust front travelling downwind
float wave  = 0.5 * sin(Time * 1.7 - along * 1.1) + 0.3 * sin(Time * 2.9 + x * 1.3 - z * 0.9) + 0.2 * sin(Time * 5.7 + x * 5.1 + z * 3.7);
float amt   = WindStrength * bend * (0.35 + 0.65 * gust);
float3 lean = WindDirection * (0.10 * amt + 0.09 * amt * wave);
lean.y      = -(0.05 * amt + 0.04 * amt * abs(wave)) * bend;      // tips drop as they lean
// trample: radial push + press-down around TramplePos within TrampleRadius (smoothstep falloff)
```
* `TramplePos` = the controlled character's position (or far below the ground when nobody is controlled),
  radius 0.55 m, push 0.30 m × bend, press 0.18 m × bend. A walking character leaves a clear ring; no CPU state.
* The `-along` terms make gusts and waves *travel* with the wind; the position-hash term breaks up the front.
* Pass the same `Flutter()` in the shadow-caster VS for trees — but **do not draw grass into the shadow map**
  at all: thousands of thin triangles produce acne and cost more than they give. Draw ground cover only in the
  main/G-buffer pass (`if (!shadowPass)`).

## 2. Geometry that reads as vegetation
* **Blade** = 5 vertices: root pair, mid pair (×0.7 width, lifted 55 %), tip; two triangles + one, emitted in
  **both windings** (the scene culls back faces). Height 0.14–0.34 m (× clump), width 2.5–4.5 cm, random yaw,
  a curl of 18 % height away from the face. Bend weights 0 / 0.35 / 1.
* **Normal**: *not* the face normal. Use `normalize(faceNormal.xz, 1.6)` — an up-biased normal so blades are lit
  like the ground they stand on; true face normals make half the blades dark walls.
* **Colour**: root→tip gradient (dark 44/82/30 → 96–136/156–178/58–70), hue from low-frequency noise so patches
  differ; the gradient is what makes a flat triangle look like a blade.
* **Field**: a band (`IsField(x,z)`) of 0.55–0.9 m stalks in golden tones, thinner (2–3 cm), bend scale 0.75,
  70 % carry a **seed head** (a slanted diamond, 5–8 cm). No density thinning in the field.
* **Flowers**: stem = thin blade (1.2 cm); head = 6 petal triangles (dark edge colour, bright tip pulled down
  10 %), raised centre quad; the whole head tilted toward the light. Species table (petal, centre, size):
  daisy, buttercup, poppy, cornflower, bluebell. Patch noise picks *one species per patch*
  (`species[noise(x·0.11, z·0.11)·N]`) and density `pow(patch, 2.2) × 1.6` so flowers cluster rather than salt the lawn.
* **Bushes**: 4–6 overlapping lumpy domes (upper 115 % of an ellipsoid so the rim meets the ground), radius
  0.45–0.8 m, baked dark underside gradient, alpha in the foliage range so they flutter like trees; grass is
  thinned under them; they get a push-out collision circle (0.85 r) — walking through a bush looks wrong.

## 3. Placement
* Density from two octaves of value noise: `clump = 0.6·N(0.45x) + 0.4·N(1.7x)`, keep a blade with probability
  `0.25 + 0.9·clump`, and scale height by `0.7 + 0.6·clump` — clumps, not a carpet.
* Keep-outs: plaza radius + margin, tree trunk radius + 0.1, lamp posts, world edge; flowers need a larger margin.
* Budget: 38 blades/m² over a 27 m square minus the plaza ≈ 17.7k blades + 170 flowers + 10 bushes ≈ 110k
  triangles in one buffer. Measured: +0.03 ms CPU per frame (0.82 → 0.85 ms), 0 B/frame.
* Seed everything from one `Random(seed)` so `--seed` reproduces the meadow; expose `--grass density`,
  `--no-grass`, and a key to hide it for A/B shots.

## 4. Gotchas
* Reusing colour alpha for two ranges changes the foliage scale: halve the foliage constants (0.07→0.035,
  0.03→0.015) when the amount formula doubles, or the trees suddenly thrash.
* Up-biased normals + no shadow casting means grass is lit slightly brighter than the ground — drop the blade
  material's specular (0.05) or the field glows.
* Seed-head diamonds all in one plane look like confetti edge-on; randomise their yaw per stalk.
* A sprinting character crosses 8.8 m in 2 s — when scripting a "stand in the meadow" shot, walk, don't sprint,
  or you end up in the field.
