---
name: monogame-outdoor-daylight-map
description: TopDownRaid-specific values for the Meadow daylight map — MapDef fields, ambient and grade numbers used, prop sizes and loot settings for the car wreck, grass tuft density, and the GAME1 headless knobs. The generic daylight-on-a-night-pipeline technique lives in the shared skill.
---
> Generic technique: see the shared skill `monogame-outdoor-daylight-map` in C:\temp\game1\.claude\skills.

# TopDownRaid Meadow map specifics

- `MapDef` (`World/MapDef.cs`) gained `FloorKind Floor = FloorKind.Asphalt, bool Daylight = false`; Meadow sets
  `Floor = Grass`, `Daylight = true`, `Ambient = (1.10, 1.06, 0.92)` (warm sun), `LampGrid = 0`, no FireBarrel weight.
  Applied at raid start in `Meta/Raid.cs`: `pipeline.Ambient = new Vector4(map.Ambient, 0f); pipeline.SetGrade(map.Daylight)`.
- `RenderPipeline.SetGrade(bool)` night values: Desaturate 0.30, Shadows (0.82,0.90,1.05), Highlights (1.10,1.02,0.90),
  Contrast 1.08, Grain 0.07, Vignette 0.62 (the grungy look); daylight: 0.10 / (0.96,0.99,0.98) / (1.06,1.03,0.95) /
  1.04 / 0.035 / 0.28.
- Grass tile colours (`Graphics/TextureFactory.cs`): lush (0.18,0.30,0.11), dry (0.34,0.37,0.14), dirt (0.31,0.25,0.16),
  daisy (0.72,0.70,0.55).
- Props (`Graphics/PropArt.cs`, `PropDefs`): Tree 56×56 + DrawInflate 62; Bush 64×64 + 8; CarWreck 228×104 `IsLong`,
  lootable 40 % titled "CAR BOOT", Health 6 (sturdier than a crate). Draw culling margin widened 120 → 180 for canopies.
  `PropKind.Grass` tufts: `Size²/26000` appended to `world.Decor` when `map.Floor == Grass`.
- Extraction flares are the only placed lights on Meadow; headlamp stays on and is invisible.
- Verify: `GAME1_STATE=raid GAME1_MAP=<id> GAME1_ZOOM=0.8 GAME1_SHOT_DELAY=4 GAME1_SCREENSHOT=out.png`.
