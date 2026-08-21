---
name: monogame-character-rig
description: TopDownRaid-specific styling for the procedural top-down character rig — kit presets (PMC/Scav/Raider), faction identity cues, kit colour notes, file locations and the GAME1_* test knobs. The generic rig technique lives in the shared skill.
---
> Generic technique: see the shared skill `monogame-character-rig` in C:\temp\game1\.claude\skills.

# TopDownRaid character rig specifics

Files: `Graphics/CharacterArt.cs`, `Entities/Character.cs`. Layers are 96×96, `SpriteScale = 1.3`,
`ElbowR = (7, 12.5)`, `ElbowL = (7, -12.5)`, `MinNormalZ` 0.74 for characters.

## Kit / style presets
`CharacterStyle` record: Jacket/Sleeve/Skin/Hair colours, Weapon, `HeadGear`, Backpack, Vest(+colour), Pants,
Gloves, GearColor, Radio, Holster, RolledSleeves.
- `Player` — PMC: cap, plate carrier, backpack, gloves, radio, holster.
- `Brawler` — Scav: hood up, rolled sleeves, bare hands, bat.
- `Gunner` — Raider: helmet with NVG mount, black kit.
Torso layer holds backpack + straps, vest/pouches/molle or chest strap, radio + antenna, holster.
Head layer variants: Hair / Cap+brim / Helmet+NVG mount+strap / Beanie / Hood with face in the opening.

## Faction readability
Cap vs hood vs helmet, and gloves vs bare hands, tell factions apart. Dark military kits under the desaturating
grunge grade need ~+30 % brightness (kit luminance 0.2–0.45).

## Test knobs
`GAME1_SPAWN_DIST=150 GAME1_ZOOM=2 GAME1_SHOT_DELAY=1 GAME1_SCREENSHOT=out.png` rings the initial enemies around the
player; `GAME1_BOT=1` adds firing/reload poses; `GAME1_VIEW=1` albedo, `GAME1_VIEW=2` normal buffer.
