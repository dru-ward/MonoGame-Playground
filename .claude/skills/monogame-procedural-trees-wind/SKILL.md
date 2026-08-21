---
name: monogame-procedural-trees-wind
description: Procedural 3D trees with wind animation for MonoGame (or any engine with GPU skinning) — trees built as small skinned rigs (trunk bone chain + branch/frond bones) so bone rotation gives big slow sway, plus a vertex-shader leaf flutter driven by a per-vertex weight stored in colour alpha; six style recipes (oak, autumn maple, pine, birch, palm, dead) from lofted bark tubes, lumpy shaped ellipsoids with baked underside shading, parametric cone tiers and serrated fronds; a travelling gust model; planting with spacing; shadow-frustum and screenshot gotchas. Use when a code-only MonoGame scene needs vegetation, foliage, or anything that should sway in wind without animation assets.
---

# Procedural trees that sway in the wind

Learned building six tree styles for the CharacterModels scene (procedural skinned characters,
forward + deferred lighting). Every tree is generated at start-up: no models, no textures.

## Core idea: a tree is a character
If the project already has GPU skinning (`Skeleton` → `Matrix[]` palette, `Weighter` that weights
vertices by distance to bone segments, a skinning vertex shader), **build the tree as a skinned mesh**:

* `root` (fixed) → trunk chain `t0..tN` (4–6 bones, each `Add(name, parent, offset = parent.TailOffset, tail = dir * len)`),
  wandering direction per segment (`dir = normalize(dir + random xz * wander)`).
* Branch bone `b{n}` from a point on the trunk path + child `b{n}e` for the whippy outer half.
  Palm fronds: one bone per frond + tip bone. Dead trees: a third "twig" level.
* One `Weighter(skeleton, sharpness ≈ 2.2–3, allBoneNames)` for the whole tree; vertex weights then
  blend smoothly across trunk ↔ branch joints and foliage at a branch tip follows that branch.
* Result: zero extra shader work for sway — the existing `Bones[]` palette does it — and shadows, the
  shadow-caster pass and the deferred G-buffer pass all follow for free because they share the vertex shader.

**Order matters**: the `Weighter` needs the *complete* skeleton, so add every bone first, then emit
geometry. Keep branch bone metadata (head, tail) and loft the bark tubes afterwards from the bone
positions (`RebuildBranchMeshes`) instead of interleaving mesh + bone creation.

## Wind model
```csharp
class Wind { Vector3 Direction; float Strength; /* 0 still, 0.7 breezy, 1.3 gale */
  float Gust(float t, Vector3 pos) {            // travelling envelope: downwind trees react later
    float tt = t - Vector3.Dot(pos, Direction) * 0.35f;
    return clamp(0.55f + 0.30f*sin(tt*0.47f) + 0.15f*sin(tt*1.13f+1.3f) + 0.10f*sin(tt*2.7f+0.4f), 0, 1.2f); } }
```
Per tree, per bone (`flex` 0.12 at the trunk base → 0.35–0.6 at the top, 0.55 branches, ×1.6 branch
tips, 1.1–1.8 palm fronds; `depth` = chain level):
```csharp
var local = TransformNormal(wind.Direction, RotationY(-tree.Yaw));   // wind in the tree's local frame!
var bendAxis = normalize(Cross(Up, local));
float freq = 1.1f + 0.45f * depth;                                   // tips oscillate faster
float osc  = 0.65f + 0.35f * sin(t * freq + bonePhase + treePhase);
float lean = strength * gust * flex * osc * 0.16f;                   // radians
float wobble = strength * flex * 0.05f * sin(t * freq * 0.73f + bonePhase * 1.9f);
bone.Rotation = FromAxisAngle(bendAxis, lean) * FromAxisAngle(local, wobble);
```
* Rotations compose down the chain so the top leans most with no extra work; even a 2.5 "storm"
  strength did not tear geometry because each bone only bends a few degrees.
* Remember the tree's yaw: the mesh is rotated by `World`, bones are in local space. Forgetting
  `RotationY(-Yaw)` makes each tree lean in a different world direction.
* Per-bone random phase + per-tree phase, otherwise all trees breathe in sync and look mechanical.

## Leaf flutter in the vertex shader (no extra vertex data)
Vertex colour alpha was unused (the pixel shader was outputting it as the frame's alpha — see gotchas),
so it became a *flutter weight*: 255 = rigid, 190 oak/maple leaves, 150 birch, 200 palm fronds, 215 needles.
```hlsl
float Time; float WindStrength; float3 WindDirection;
void Flutter(inout float4 worldPos, float3 worldNrm, float alpha) {
    float amount = (1.0 - alpha) * WindStrength;  if (amount <= 0.0) return;
    float ph   = dot(worldPos.xyz, float3(3.1, 2.3, 2.7));      // low spatial frequency => neighbours move together
    float wave = sin(Time * 7.0 + ph) * 0.6 + sin(Time * 11.3 + ph * 1.7) * 0.4;
    float gust = 0.6 + 0.4 * sin(Time * 0.9 + dot(worldPos.xyz, WindDirection) * 0.35);
    worldPos.xyz += worldNrm * wave * gust * amount * 0.07 + WindDirection * gust * amount * 0.03;
}
```
Apply **after** skinning and the world transform (so world-position phase differs per tree) and in
**both** the main and shadow-caster vertex shaders or shadows detach from leaves. Set the pixel
shader's output alpha to 1.0 explicitly.

## Geometry recipes (all in metres, tree at origin, +Y up)
Primitives used: `Loft(rings, sides, weighter, upHint, capEnd)`, `Ellipsoid(center, radii, segs, stacks,
color, material, weighter, shape: d => radiusScale, colorFn: d => colour, alpha)`, and a new
`Parametric(segments, stacks, pos(u,v), color(u,v), material, weighter, closedU, doubleSided, alpha)`
with the same winding as the ellipsoid (u = angle with x = sin, z = cos; v from top to bottom ⇒ outward normals).

* **Bark tube**: 3 rings per trunk segment, radius `lerp(r0, r1, t^0.8)`, ×1.35 flare below t = 0.12,
  ±5 % random ellipticity per ring, per-ring colour jitter ±10. Branches start 1.5 radii *inside* the
  parent so the joint is hidden. 9–10 sides for trunks, 5–6 for branches, 5 for twigs.
* **Leaf mass** (oak/maple/birch): ellipsoid with shape
  `1 + lump·sin(5.3x+a)·sin(4.1y+b)·sin(6.2z+c) + 0.5·lump·sin(11x+9z+c) − 0.08·max(0,−y)` (lump 0.14–0.2)
  and **colour gradient baked per vertex**: `Lerp(shade, leaf, clamp(d.y·0.8+0.55))` where
  `shade = leaf × (0.42, 0.48, 0.55)`. That underside darkening is what turned flat "balloons" into
  readable foliage — lighting alone was too soft. One blob per branch tip (+ a smaller satellite blob
  offset sideways/downwards to break the silhouette) plus a large crown blob and 4 medium overlapping ones at the top.
* **Pine**: 6–7 tiers from y = 0.22h to the top; tier radius `lerp(1.25, 0.22, t)`, height `lerp(1.1, 0.55, t)`.
  Outer surface `r = R·v^0.85·(1 + 0.22·sin(θ·lobes+φ)·v + …)`, rim droops with `v³` modulated by the same
  lobes (integer lobe count keeps the seam continuous); a separate 1-stack underside from rim to a point
  under the trunk, darker colour; light tip colour lerped to body colour over the first third. Sample the
  trunk path for each tier's centre so tiers follow a leaning trunk.
* **Birch**: thin trunk (r 0.11 → 0.035), 7 rings per segment so dark bands (`(ring+seed) % 5 < 2`)
  read as bands rather than a smear — with only 3 rings per segment the bands vanished into the gradient.
  Many small bright clusters (0.3–0.45 m), branches droop 0.6.
* **Palm**: one-directional trunk wander (arc, not wiggle), rings alternate radius ×0.86 for leaf scars,
  9–12 fronds: parametric strip along `spine(v) = crown + d·len·v + Down·droop·len·v²`, half-width
  `w·sin(0.9πv+0.1)·(1−0.35v)·(0.75+0.25|sin(9πv)|)` (serration), V cross-section by dropping the edges
  `Down·|x|·half·0.6`, `doubleSided: true`, darker midrib stripe. Coconuts = three small ellipsoids.
* **Dead**: no foliage, 4–6 gnarled branches (pitch 0.2–1.0, droop −0.1) with 2–3 twigs each as their own bones
  (flex 0.7 → they rattle), grey bark with every third ring dark, broken-top ellipsoid.
* **Autumn variant** is the broadleaf recipe with a different palette (reds/oranges/yellows) and
  `crown 0.85, spread 1.15` — a palette record `(Bark, BarkDark, Leaves[])` makes new variants one line.

Cost: 2.5–4k triangles per tree; 22 trees ≈ 70k tris and 44 extra draw calls (shadow + main) — negligible.

## Planting
Rejection-sample positions in an annulus (r 6.2–12.7) with a minimum spacing of 2.6 m, cycle through
the style enum so every kind appears, downgrade 40 % of palms/dead trees to oaks so the rare kinds stay
rare, random yaw and scale 0.85–1.25. Paint the ground outside the plaza as grass (per-tile colour
jitter, slightly uneven slab heights) and a kerb ring — trees on a checkerboard look wrong.
Provide `--trees n`, `--seed n`, `--wind s` and a `--gallery` flag (one of each style in a row) for inspection shots.

## Gotchas
* **Shadow frustum**: the directional shadow was an 8.5 m ortho box around the characters; trees outside
  it cast no shadow and their sway vanished from shadows. Widen to ~20 m and move the light eye back
  (18 m, far 36 m) — 2048² is still ~100 px/m.
* **Alpha in the frame buffer**: with alpha now used as a weight, a pixel shader returning `input.Color.a`
  writes < 1 alpha into the render target, and `SaveAsPng` of a `--shot` target then produces
  semi-transparent leaves in the PNG. Return 1.0.
* **A tree between the camera and the subject** looks like a rendering bug (one huge green blob filling the
  frame). Before debugging, shoot from higher/farther (`--pitch 25 --dist 20`) or use `--gallery`.
* `Random.Next(min, max)` throws when given a negative "amount" — colour jitter helpers must clamp.
* Bash heredocs in this environment mangle `\\` in Python raw strings — write helper scripts with
  forward-slash paths or via the Write tool.
* Warm-up: advance `_time` during the `--warm` loop and call `tree.Update` once afterwards, otherwise the
  screenshot shows trees in bind pose while characters are mid-animation.
