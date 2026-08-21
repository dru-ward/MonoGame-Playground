---
name: monogame-raid-metagame
description: TopDownRaid-specific styling and balance for the session meta-game loop (extraction-shooter theme) — raid/extract naming, map record field names and file locations, extraction marker art, HUD copy and colours, the Tarkov-style gear-loss rules and the GAME1_* headless env var names. The generic technique lives in the shared skill.
---

> Generic technique: see the shared skill `monogame-raid-metagame` in C:\temp\game1\.claude\skills.

# TopDownRaid raid loop — game-specific parts

## Naming / files
- States: `Menu, Stash, MapSelect, Raid, Summary`; hub = "Hideout"; session = "Raid"; level = `MapDef` (`World/MapDef.cs`),
  exits = `ExtractDef`; session class `Meta/Raid.cs`; screens `UI/MetaScreens.cs`; profile in `Meta/`.
- Outcomes: `Extracted / Killed / TimedOut`; abandoning with Esc = "MIA" (loses loadout, stash untouched).
- `MapDef.Floor` default `Asphalt`; grass floor + tuft scatter and day colour grade for `Daylight` maps
  (`pipeline.SetGrade(map.Daylight)`, see monogame-outdoor-daylight-map). Assets come from `RaidAssets`.
- `Profile.Gold`, `SelectedMapId`; `EnsureMinimumLoadout()` grants a free pistol + mag.

## Extraction presentation
- Zone marker: `PropArt.CreateExtractMarker(w,h)` — worn painted frame, hazard ticks, X, flare canister.
- Green pulsing point light at the centre + green smoke particles (10/s).
- HUD copy: raid timer top-centre turns amber < 3 min, red < 1 min; compass arrow + "NAME 161M" to the nearest extract;
  "IN EXTRACT: NAME" when inside; hold bar with percentage; "EXTRACTION INTERRUPTED" while it decays.
- Button copy: "DEPLOY [ENTER]" + hint line; `WeaponDef.ShortName` for weapon buttons.

## Headless knobs
`GAME1_STATE=menu|stash|map|raid|summary`, `GAME1_MAP=<id>`, `GAME1_SPAWN_AT_EXTRACT=1`, `GAME1_NOSAVE=1`, `GAME1_SCREENSHOT`.
