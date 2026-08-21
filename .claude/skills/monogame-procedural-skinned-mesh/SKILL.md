---
name: monogame-procedural-skinned-mesh
description: Build rigged, GPU-skinnable 3D character meshes entirely in code in MonoGame (no FBX/content models) — custom IVertexType with blend indices/weights, an axis-aligned bind-pose skeleton with inverse-bind palette, loft/ellipsoid/box primitives with correct winding and smooth normals, automatic bone weighting from distance-to-bone-segment, per-vertex material channels, and OBJ export. Use when a MonoGame project needs characters, creatures or props generated procedurally, or when imported models are not an option.
---

# Procedural skinned meshes in MonoGame

Facts and methods from building five rigged, animated humanoids (~10k tris, 24 bones each) with zero asset files.

## Why generate instead of import
* MonoGame has no runtime model-*creation* API and `Model` is read-only once loaded; the FBX importer is brittle.
* Everything a skinned model needs can be produced at startup: `VertexBuffer` + `IndexBuffer` with a custom
  `IVertexType`, a `Matrix[]` bone palette, and an `Effect` that reads blend indices/weights.
* Build once at load (a few ms per character), keep the `List<Vertex>` / `List<int>` around for export/debug.

## Vertex format (56 bytes)
```csharp
public struct SkinnedVertex : IVertexType {
    public Vector3 Position; public Vector3 Normal; public Color Color;     // albedo (sRGB)
    public Vector2 Material;            // x = specular strength, y = shininess 0..1  (any per-vertex params you like)
    public Byte4 BlendIndices; public Vector4 BlendWeights;
    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0,  VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color,   VertexElementUsage.Color, 0),
        new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(36, VertexElementFormat.Byte4,   VertexElementUsage.BlendIndices, 0),
        new VertexElement(40, VertexElementFormat.Vector4, VertexElementUsage.BlendWeight, 0));
    public VertexDeclaration VertexDeclaration => Declaration;
}
```
* `Byte4` indices arrive in the shader as un-normalised floats (`float4 BlendIndices : BLENDINDICES0`) and can index
  the bone array directly — same trick `SkinnedEffect` uses.
* Per-vertex colour + a small "material" vector replaces textures entirely for stylised characters; colours are
  sRGB, so convert to linear in the shader (`pow(c, 2.2)`) before lighting.

## Skeleton
* Keep the **bind pose axis-aligned**: every bone's bind transform is translation only. Then animation rotations
  are expressed in a fixed, understandable frame ("arms hang along -Y, spine points +Y, feet point +Z") and
  `InverseBind = Translation(-bindHead)`.
* Bone = `{Parent, LocalOffset, TailOffset, BindRotation, Rotation, Translation}`; add parents before children so
  a single forward loop computes `World = Local * ParentWorld` and `Palette[i] = InverseBind * World`.
* `BindRotation` (constant local rotation applied after the animated rotation) is how to model *sockets*: a
  weapon bone under the hand rotated to a natural grip, or sheath sockets on the chest/hips. Animation never writes
  those bones, so the constant rotation survives `Pose.ApplyTo`.
* Store `BindHead`/`BindTail` per bone — the auto-weighter needs segments, not points. Give leaf bones an explicit
  tail (hands, head, feet, toes).
* Bone budget for `vs_3_0` (OpenGL backend): `float4x3 Bones[64]` = 192 constant registers, comfortably inside the
  256 limit. 72 (SkinnedEffect's number) also fits; more needs `float4x3` and care.

## Primitives that cover a whole character
All primitives append to one vertex/index list, then `FinishPart(baseIndex, weighter)` computes smooth normals
and weights for just the vertices added. Overlapping parts (arm plunging into torso) are fine visually — no
boolean ops needed.

**Loft** (the workhorse): a tube through a list of rings `{Center, Rx, Rz, Color, Material, Tangent?}`.
* Tangent per ring = `normalize(next - prev)` unless overridden; build the frame with **parallel transport**
  (`side = normalize(side - t * dot(side, t))` each ring) so twisting never flips along curved paths (bows, tails).
* Per-ring colour/material → sleeves, boot cuffs, gradients along a limb come for free.
* Rounded ends: prepend/append rings at `center ∓ t · r·sin(a)` with radius `r·cos(a)` for `a = k/n · π/2`.
  A final ring of radius ~0 is fine (degenerate quads contribute zero area to normals).
* Explicit `Tangent = Up` on every ring lets rings share a centre with different radii → flat discs/annuli
  (hat brims, shield rims, pedestals) without special-casing.
* Elliptical rings (`Rx ≠ Rz`) make hands (thin in X, wide in Z), torsos, feet.

**Ellipsoid** with a shape function `float k(Vector3 dir)` scaling the radius per direction:
* Skull: narrow the jaw (`k *= 1 - 0.2·max(0,-y)·(1-0.55·max(0,z))`), add chin/brow bumps, flatten the back.
* Hair, helmets, hoods, beards: return a scale **< 1 (0.75–0.8) where the item should not exist** — those vertices
  collapse inside the skull and are hidden by depth, giving a clean hairline/face opening without clipping
  geometry. Blend the edge with a smoothstep over ~0.1 of `dir` for a soft boundary.
* Small sinusoidal perturbations (`0.025·sin(x·23)·sin(z·17)`) turn a smooth cap into tufted hair.

**Box**: flat-shaded, six faces with their own vertices; use for blades, guards, buckles, backpacks.

## Winding and normals — the bug to avoid
* With `CullCounterClockwise`, every primitive must emit CCW-from-outside triangles. A UV sphere generated
  top→bottom with rings going `+Z → +X` is *left-handed* relative to a loft whose rings go `side → up` along `+t`;
  one of them will be inside-out. Symptom: you can see **through** the near side into the inside of the far side,
  and the object still looks plausibly lit because the far side's inward normals face the camera.
  Check every primitive from behind at least once.
* Normals: accumulate un-normalised face cross products per vertex (area weighting is free) and normalise at the
  end. Rings wrap with modulo (no duplicated seam vertex) so the seam is smooth.

## Automatic skin weights
For a part, pass the list of bones allowed to influence it; for each vertex:
```
w_i = 1 / (dist(p, segment_i)^sharpness + ε)     // sharpness 4 for limbs, 2.5–3 for cloth/ponytails/pauldrons
keep top 4, normalise
```
* Distance to the bone *segment* (head→tail) gives 50/50 at a joint and ~25:1 two radii away with sharpness 4 —
  smooth elbows/knees with no hand painting.
* Restrict the allowed set per part so torso vertices never pick up arm bones: torso → hips/spine/chest/neck;
  arm → clav/arm/fore/hand; foot → shin/foot/toe; robe → hips/spine/chest + both thighs (skirt follows legs).
* Rigid attachments use a single bone (`Weighter.Fixed`): weapons, buckles, eyes.

## Proportions that read as human (metres, height 1.8)
hips 0.95, spine +0.12, chest +0.18, neck +0.20, head +0.08 (head radius ≈ 0.11–0.125); shoulders ±0.21 at 1.42;
upper arm 0.30, forearm 0.27, hand 0.17; hip joints ±0.10; thigh 0.44, shin 0.42, ankle 0.06, foot forward 0.16.
Scale everything by `Height/1.8`, limb radii additionally by a `Bulk` factor, and widths by `Shoulders`/`Hips`
factors — that alone produces a knight, a ranger and a barbarian silhouette from one builder.

## Character spec pattern
A plain data class (`Height, Bulk, Shoulders, Hips, HeadSize, palette colours, HeadGear enum, Weapon enum,
Sleeves enum, flags: Beard, Ponytail, Pauldrons, ChestPlate, Shield, Robe, Quiver, Backpack, Gloves`) drives one
builder. New archetypes are data; new gear is ~5 lines placing rings/ellipsoids/boxes in bind-pose metres and
naming the bones that may move it.

## Export
Write `v x y z r g b` (Blender reads the colour extension), `vn`, and `f a//a b//b c//c` (1-based). Bind pose,
metres, Y up — it loads straight into any DCC for inspection.

## Performance notes
* ~30k vertices / 51k triangles for five characters renders trivially; vertex count is dominated by 24-segment
  rings — use 10–16 segments for small parts (fingers, arrows, straps).
* Keep one VB/IB per character (one draw call each) rather than per part.
