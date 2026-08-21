---
name: monogame-enemy-ai
description: TopDownRaid-specific enemy data for the enemy AI technique — Brawler (Scav) and Gunner (Raider) balance, the enemy weapon pool, loot drops and corpse persistence, file locations. The generic state machine, kiting, steering and spawner technique lives in the shared skill.
---
> Generic technique: see the shared skill `monogame-enemy-ai` in C:\temp\game1\.claude\skills.

# TopDownRaid enemy specifics

Files: `Entities/Enemy.cs`, `Entities/EnemyManager.cs`.

## Kinds (balance)
```
Brawler: 60 hp, 250 px/s, aggro 560, lose 900, melee at contact (+6 px), 14 dmg every 0.9 s after a 0.3 s wind-up, bat; style Brawler (Scav)
Gunner : 45 hp, 205 px/s, aggro 680, lose 1000, shoots inside 470 px with LOS, EnemyPistol (0.55 s, 8 dmg, spread 0.08), helmet; style Gunner (Raider)
```
Alert timer 6 s; ranged kiting band 170–320 px.

## Random weapons per gunner
`Enemy` ctor: `var wdef = Rng.Pick(WeaponDef.EnemyPool)` (pistol ×2, rifle, SMG, shotgun — all `Automatic = true`),
`HeldWeapon = wdef.Held`; the rig is built with arms for every gun in the pool. Muzzle flash: `lights.Flash(muzzle, orange, 220, 1.6, 0.08)`.

## Loot and corpses
Death → `FillLoot()` (gun item + 1–3 mags + kind table) into `Enemy.Loot`; `Died` handler spawns
`pickups.SpawnBurst(pos, def.Loot.Roll(), 140)` and a dark red puff. Corpses persist 90 s / fade after being searched
(plain fade-out variant: removed after `DeadTimer > 1.6 s`).

## Spawning
Population target `min(MaxAlive, 4 + Kills/3)`, one spawn every 1.2 s at `world.RandomClearPoint(player.Position, 950, 26)`.
