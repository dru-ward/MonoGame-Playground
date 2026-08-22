---
name: monogame-procedural-structures
description: Procedural buildings and props for a 3D MonoGame scene with no assets — timber-framed cottages (plinth, plaster, beams, door, emissive lit windows, pitched slate or thatched roof with tile rows, gable box-stacks, chimney), a plank barn, a dry-stone well, a watchtower, post-and-rail fences along polylines, dry-stone walls with gateways, barrels, crates and hay bales — all from boxes and lofts into one static mesh, each registering AABB/circle colliders and point lights. Covers the XNA quaternion-order trap (q1*q2 applies q2 first) that flips roofs, 90°-step placement so colliders stay axis-aligned, circle-vs-AABB push-out and ray-vs-AABB camera collision, growing the map (bounds, planting, grass budget, shadow frustum/resolution), keep-outs for trees and grass, and scripted close-up inspection. Use when a MonoGame scene needs a village, buildings, walls, fences or props generated in code.
---

# Procedural structures (buildings, walls, props) in MonoGame

Built for the CharacterModels hamlet: 2 cottages, barn, well, watchtower, fences, a walled yard, barrels and
crates on a 48 m map. One static `VertexBuffer` (the scene's skinned vertex format with an identity palette),
one draw call per pass, 0 B/frame.

## 1. The quaternion trap (this flipped every roof)
`Quaternion q = tilt * yaw` in XNA/MonoGame applies **yaw first, then tilt in world space** — `q1 * q2` is the
Hamilton product, the reverse of `Quaternion.Concatenate(q1, q2)` (q1 then q2). A roof slab tilted about the
building's local X and then yawed must be written `yaw * tilt` (or `Concatenate(tilt, yaw)`). Symptom: slabs
sloping toward the gable ends and crossing at the ridge like an X. Check any composed rotation with a
close-up shot before building on it. Small "lean" jitters (fence posts) hide the bug; roofs do not.

## 2. Building recipes (metres; origin at ground centre; local +z = front; yaw in 90° steps)
* **Placement helper**: `P(origin, yaw, x, y, z)` rotates a local offset; `Size(yaw, s)` swaps x/z extents for
  90°/270° so unrotated boxes still fit. Restricting yaw to 90° steps keeps every collider an AABB.
* **Cottage** (w 5, d 4, h 2.6): stone plinth box (+0.2, 0.4 tall) → plaster wall box → timber frame: 4 corner
  posts (0.18²), sill and top beams on every face (0.16 × 0.12, 2 cm proud), two verticals per long face →
  door (0.9 × 2 frame, lighter inset panel, iron knob) → windows (frame 0.8², glass 0.66² with **`Mat.Glow`
  material so the deferred composite treats it as emissive**, mullions, stone sill) → roof → chimney (0.5²
  stone stack + cap) → one warm point light 0.6 m outside the front window (r 4.5, intensity 2.5, flicker 0.15).
* **Pitched roof**: ridge height `eave + d × 0.42`; each slab is a box of length
  `√((d/2 + 0.35)² + rise²) + 0.15` centred at `z = ±(d/4 + 0.1)`, tilted `atan2(rise, d/2 + 0.35)` about
  local X; four thin **tile-row boxes** per slab alternating light/dark sell slate or thatch (thatch: thicker
  slab 0.28, `Mat.Cloth`); a ridge beam box on top. **Gables**: 6 stacked boxes thinning linearly (stepped
  triangle) — no triangle primitive needed. Overhang 0.35 m plus 0.35 m past the gables.
* **Barn** (7 × 9 × 3.4): horizontal plank strips (0.28 m, alternating tones) instead of plaster, corner posts
  0.22², double door 2.6 × 2.7 with iron seam, loft hatch, steeper roof (rise w × 0.38, ridge along z, 5 tile
  rows), 7-step gables front and back, hay bales (1 × 0.7 × 0.7 with a strap box) by the door.
* **Well**: 3 courses of 12 stone blocks on a 0.75 m ring, each yawed to the tangent, alternate courses offset
  half a block; dark disc for water (`Mat.Eye` for a wet highlight); two posts, crossbar, tiny pitched roof,
  rope (2 cm box) and bucket (loft); a cool blue point light inside.
* **Watchtower**: 4 legs as lofts tapering and leaning inward (bottom 1.25 × half, top 0.8 × half), braces at
  1/3 and 2/3 height (a horizontal + a diagonal per side), platform 2.4 × half, railing posts, 4 roof posts,
  a pyramid roof = 4-segment ellipsoid with `shape = d.Y > 0 ? 1 : 0.25` yawed 45°, ladder rungs every 0.42 m;
  a lantern light on the platform.
* **Fence**: posts every ≤1.8 m along each polyline segment (0.12², ±0.03 rad lean), two rails (0.07 × 0.1 × length)
  at 0.5 and 0.9 m; one collider box per segment padded 0.12.
* **Dry-stone wall** (1.1 m): courses of 0.26 m, block lengths 0.35–0.6, odd courses offset 0.25, ±6 cm width
  and ±1 cm height jitter, lighter top course; `gateAt` leaves a gap with two taller posts; colliders are the
  two segments either side of the gate.
* **Props**: barrel = 3-ring loft (0.27/0.32/0.27) with two iron bands; crate = box + 12 edge battens.

## 3. Colliders and the camera
* Player: circle vs AABB — if inside the box expanded by r, push out along the axis of least penetration and
  remove the velocity component into that wall (slides). Circles for round props. Verified by script: a sprint
  into a wall pins x at `wallX − 0.28` and a diagonal walk slides at `walk × sin 45°`.
* Camera: ray-vs-AABB slab test on the target→camera ray (return +inf when starting inside) alongside the tree
  spheres; round props as 4 stacked spheres. A `look x y z` script command that targets a building *centre*
  puts the target inside its own collider — aim at a point in front of the building instead.
* Register keep-outs: trees (AABB expanded 2.2 m, circles) and grass (`KeepoutBoxes`, margin 0.15) so nothing
  grows through floors.

## 4. Growing the map (14 → 24 m half-size)
* Ground loop extent, world clamp (`MapHalf − 0.5`), tree ring radius `6.8 … MapHalf − 8`, tree count 22 → 46,
  min spacing 3.2, grass `Extent = MapHalf − 0.4` with density 38 → 16 blades/m² to hold the triangle budget
  (22k blades), field moved to `z < −13`, bushes 22.
* Shadow frustum 20 → 40 m, light eye distance 30, far 60, and shadow map 2048 → 4096 to keep ~100 px/m;
  fog already at 24–75 m still works. Buildings cast shadows (draw them in the shadow pass); grass does not.
* Cost after all of it: 1.13 ms CPU Update+Draw, 0 B/frame.

## 5. Inspecting what you built
Use the scripted playtest: `look x y z; cam yaw pitch dist; idle 0.3; shot name; idle 0.1; …` and tile the
shots. `shot` must end the command batch for that frame or the next `look` is in the picture — the script
runner now stops processing commands after a `shot` until the next frame. Compare daylight (forward) and dusk
(deferred) renders: emissive windows only show in the deferred path.
