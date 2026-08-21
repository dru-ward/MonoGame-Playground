---
name: monogame-outdoor-daylight-map
description: Add a bright outdoor daylight map to a 2D top-down MonoGame game whose deferred/ambient lighting pipeline was tuned for dark interiors — why ambient must be at or above 1.0 AND the colour grade must switch together, a per-map day/night grade preset, a seamless procedural grass floor tile (sward patches, worn dirt tracks, speckle details) built on periodic noise, canopy props whose sprite draws larger than their collision box (with the culling-margin fix), cheap solid bush cover, long two-orientation vehicle props, walk-through decor scatter, and which placed lights to leave out. Use when a MonoGame game with a deferred or ambient-multiplied lighting pipeline needs an outdoor, field, forest or daytime map alongside its dark ones.
---

# Outdoor daylight map on a night-tuned deferred pipeline

A pipeline tuned for dark maps (ambient ~0.08 plus placed lamps) does not become daylight with "ambient 0.5". Two
things must change together: **ambient ≥ 1.0** (it multiplies albedo, and albedo art averages 0.2–0.35, so 0.55 still
looks like dusk) and the **colour grade**, because a grade tuned for dark scenes (strong desaturation, cool shadows,
heavy vignette) squashes a bright scene back into mud. Verify with a screenshot after each change — a first attempt
that only raised ambient still read as night until both were fixed.

## 1. Per-map knobs (grow the map definition record with defaults)
```csharp
record MapDef(..., FloorKind Floor = FloorKind.Default, bool Daylight = false);   // old maps untouched
// daylight map: Ambient slightly warm and > 1 (starting value (1.10, 1.06, 0.92)) · lamp grid 0 (placement loop doesn't run)
pipeline.Ambient = new Vector4(map.Ambient, 0f);  pipeline.SetGrade(map.Daylight);   // at map start
```
```csharp
public void SetGrade(bool daylight) {   // roles only: pick values to match your game's look
  Desaturate       daylight ? low  : higher;          // daylight keeps saturation
  GradeShadows     daylight ? near-neutral : tinted;  // the dark-interior grade usually tints shadows; neutralise it outdoors
  GradeHighlights  daylight ? slightly warm : as tuned for interiors;
  Contrast / Grain / Vignette: all lower in daylight (vignette roughly halved) or the map reads as dusk.
}
```

## 2. Grass floor tile (512 px, seamless — same periodic-noise scheme as any other tiling floor; see monogame-procedural-textures)
```csharp
GrassHeight: lumps = Noise(u*24)*0.10 + Noise(u*64)*0.06; blades = Noise(u*192)*0.06;
             dirt = clamp((Noise(u*3 + off) - 0.60)/0.14);      // low-freq worn-patch mask
             h = 0.5 + lumps + blades - dirt*0.14;              // normal strength ~1.2, wrap:true
Albedo:  col = lerp(lushGreen, dryGreen, Noise(u*6));            // sward patches (starting values (0.18,0.30,0.11) / (0.34,0.37,0.14))
         *(0.80 + 0.45*blades) * (0.88 + 0.24*lumpNoise);
         lerp to dirtBrown by dirt * (0.65 + 0.5*Noise(u*40))   // noisy patch edge (starting value (0.31,0.25,0.16))
         hash speckles: >0.992 → ×1.35 bright fleck; <0.0012 → pale flower fleck;
                        >0.9905 && dirt>0.3 → grey stone (only in bare patches)
```
The dirt patches read as worn tracks and break up the green; without them the floor is a flat billiard table.

## 3. Nature / roadside props
- **Tree — collision is the trunk, the sprite is the canopy.** `PropDefs.Size(Tree)` = the trunk box (what generation,
  collision, bullets and AI see; 56×56 is a starting value) plus `PropDefs.DrawInflate(Tree)` px per side (~62); the
  texture is built at the inflated size and the draw call inflates the bounds rect:
  `dr = c.Bounds; dr.Inflate(inf, inf); DrawRect(sprite, dr)`. Characters can walk and shoot under the canopy edge.
  **Widen the draw-culling margin** to cover the overhang (e.g. 120 → 180) or canopies pop at the screen edge.
  Canopy recipe: dark skirt circle, main crown (dome 0.65), 6 rim lobes on a ring (`cos/sin(i·60°) * 0.30w`),
  2–3 highlight clusters offset toward the light direction, one shaded lobe opposite.
- **Bush** 64×64 solid (+8 draw inflate): 3 overlapping ellipses + one highlight — cheap cover.
- **Long vehicle wreck** (~228×104, `IsLong`, both orientations via a transpose flag): tires as dark boxes poking past
  the shell, body shell box, bonnet/roof/boot panels in 3 brightnesses (roof lightest), dark glass boxes between them,
  2–3 rust ellipses. Making some of them lootable containers with a different silhouette makes the map read less
  copy-pasted.
- **Grass tufts** = decor, never solid: decor entries appended to `world.Decor` when `map.Floor == Grass`
  (`Size²/26000` of them, anywhere random, is a starting density); sprite = 8 thin capsules radiating from the base.
  Decor draws before pickups/containers, so items dropped on a tuft stay visible.

## 4. What NOT to add
No lamp grid, no fire/glow props, no cold floods — any remaining placed lights (objective markers, flares) become the
only ones and still read clearly in daylight. A player headlamp/torch can stay enabled; it is simply invisible against
ambient > 1 (no need to special-case it). Keep haze/dust particles: non-emissive grey is barely visible in daylight and
harmless.

## Verification
Render the map headlessly (state/map/zoom/shot-delay env knobs + screenshot, see monogame-headless-screenshots) and
look at the PNG: grass clearly green, dirt patches visible, canopies rounded with readable lobes, vehicles identifiable,
no lamp pools. If tests iterate `MapDef.All`, the new map is picked up automatically.
