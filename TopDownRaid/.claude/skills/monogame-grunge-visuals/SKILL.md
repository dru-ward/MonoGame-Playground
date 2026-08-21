---
name: monogame-grunge-visuals
description: TopDownRaid-specific styling for the procedural worn-surface technique — the grungy urban night-raid palette, asphalt/oil/lane-line look, the urban prop set and sizes, sodium/fluorescent lamp and fire-barrel colours and light values, colour-grade parameters and kit colours. Use when tuning the TopDownRaid look; the technique itself lives in the shared skill.
---
> Generic technique: see the shared skill `monogame-grunge-visuals` in C:\temp\game1\.claude\skills.

# TopDownRaid grunge styling

Look: grungy urban / night raid (Tarkov-style). Code lives in `Graphics/TextureFactory.cs`
(`CreateAsphaltAlbedo/Normal`, `AsphaltHeight`), `Graphics/PropArt.cs`, `World/PropKind` / `PropDefs`.

## Asphalt tile values
Base albedo 0.30 ± fine noise; cracks -> tar 0.06; potholes -> lighter gravel 0.30; two elliptical oil stains; 1.5 %
gravel specks; worn dashed lane line down the middle of the 512 px tile.

## Urban prop set
| Prop | Size | Recipe |
|---|---|---|
| Container | 224x112 | steel box, 10 px corrugation, door end + handle, rusty end + rust bloom, grime .5 |
| Jersey barrier | 192x56 | wide base box, narrower raised top, chipped ends, lifting slots; 50 % spawn a second segment end-to-end |
| Sandbags | 128x56 | two staggered rows of capsules, dome 1.0, alternating tint |
| Rubble | 96x96 | dust ellipse, rocks, concrete chunks, rebar (thin rust capsules) |
| Fire barrel | 40x40 | rust rim ring, drum top, dark opening; fire = particles + light |
| Lamp base | 32x32 | concrete disc + steel pole cap (non-blocking decor) |
Outdoor props (trees, bushes, wrecked cars, grass tufts) and the grass floor: monogame-outdoor-daylight-map.

## Light set
- Ambient cold and low: `(0.085, 0.09, 0.115)`.
- Sodium lamps: jittered 3x3 grid, colour `(1.0, .74, .42)`, radius 760, height 220, intensity 1.55; every 4th is a
  cold fluorescent flood `(.72, .82, 1.0)`, radius 860; every 3rd buzzes (low value 0.4).
- Fire barrels (one per FireBarrel prop): `(1.0, .55, .22)`, height 55, base intensity 1.3, radius 340 + 50n.
- Player headlamp warm-white, intensity 0.95; muzzle/ricochet flashes unchanged.

## Grade parameters (FinalCombine)
Desaturate 0.30; GradeShadows `(.82, .90, 1.05)`; GradeHighlights `(1.10, 1.02, .90)`; Contrast 1.08; GrainAmount 0.07;
vignette radius .85 / softness .55 / strength .62. Haze puffs dim grey.

## Kit colours
Enemy kits dark (black / olive / grey); player olive / tan.

## Verification
`GAME1_ZOOM=0.7 GAME1_SHOT_DELAY=4 GAME1_SCREENSHOT=out.png`.
