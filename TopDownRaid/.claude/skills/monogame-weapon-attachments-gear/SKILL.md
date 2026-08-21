---
name: monogame-weapon-attachments-gear
description: TopDownRaid-specific item roster, balance numbers, file locations, key bindings and UI copy for weapon attachments, armor, torch/laser, grenades and melee. The generic technique lives in the shared skill.
---

> Generic technique: see the shared skill `monogame-weapon-attachments-gear` in C:\temp\game1\.claude\skills.

# TopDownRaid attachments & gear — game-specific parts

## Files
`Combat/Gear.cs` (AttachmentDef/GearDef/AttachPoints), `Combat/GrenadeSystem.cs`, `Entities/Player.cs` (melee, torch,
`RefreshVisuals`), `AttachmentArt.CreateAll` (24 px overlay sprites), `Crate.EnsureContents()` for containers/caches.
Attach points are keyed by `HeldWeapon`.

## Item roster & balance
- Attachments: Optic (spread .70, +80 range) · Suppressor (flash .15, noise .4) · Compensator (recoil .65, spread .85) ·
  Torch · Laser (spread .75) · Grip (recoil .70). Slots drawn as O/M/T/G mini boxes.
- Gear: VestLight 60 armor / 55 % absorb · VestHeavy 120 / 75 %, speed .88 · HelmetSteel −15 % · HelmetTac −25 %.
  Armor plates refill vest durability. Gear slots labelled HELMET / VEST.
- Melee weapon: the bat (`WeaponDef.IsMelee`).
- Noise: gunfire alerts enemies within `720 * NoiseMul` px; grenades alert within 900 px.
- Torch: radius 900, 24°/10°, intensity 2.4; enemy gunners roll a torch 35 % of the time.
- Grenade blast: 150 px + target radius, damage `110*(0.2+0.8*k)`, lootable crates within 120 px break, flash light 520 px
  for 0.35 s; bot mode throws when an enemy is inside 420 px.
- Overlay art: red dot, suppressor tube, slotted compensator, torch with bright lens, laser box, vertical grip; laser
  drawn red.

## Keys & copy
- `T` toggles `Player.TacticalOn` (torch + laser); toast "Torch off".
- Head layer swaps cap ↔ helmet; torso bare / light vest / heavy vest.
