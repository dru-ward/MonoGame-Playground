---
name: monogame-enemy-ai
description: Enemy characters for a top-down MonoGame shooter — data-driven EnemyDef presets, an Idle/Chase/Attack/Dead state machine with aggro/lose ranges and a shot-from-afar alert timer, melee wind-up attacks, ranged kiting (distance band + strafe + line-of-sight gating), obstacle sidestep steering, pairwise separation, population-based spawning away from the player, loot drops on death and corpse fade-out. Use when adding enemies, AI behaviour or spawners to a MonoGame game.
---

# Enemy AI (`Entities/Enemy.cs`, `Entities/EnemyManager.cs`)

## Random weapons per gunner
`Enemy` ctor: `var wdef = Rng.Pick(WeaponDef.EnemyPool)` (pistol ×2, rifle, SMG, shotgun — all `Automatic = true`),
`HeldWeapon = wdef.Held` selects the arm-layer sprite; the rig is built with arms for every gun in the pool.
`AttackRange => _weapon?.Def.Range` drives the state machine and the kiting band (`far = 0.68·range, near = 0.36·range`)
so shotgunners rush and riflemen hang back. Shots spawn `Pellets` projectiles via `PelletAngle`.
Death → `FillLoot()` (gun item + 1–3 mags + kind table) into `Enemy.Loot`; corpses persist (90 s / fade after search).

## Data-driven kinds
```csharp
public sealed record EnemyDef(EnemyKind Kind, string Name, float Health, float Speed, float AggroRange, float LoseRange,
    float AttackRange, float AttackDamage, float AttackInterval, float WindUp, int ScoreValue,
    CharacterStyle Style, LootTable Loot, WeaponDef? Weapon);
Brawler: 60 hp, 250 px/s, aggro 560, lose 900, melee at contact (+6 px), 14 dmg every 0.9 s after a 0.3 s wind-up, bat
Gunner : 45 hp, 205 px/s, aggro 680, lose 1000, shoots inside 470 px with LOS, EnemyPistol (0.55 s, 8 dmg, spread 0.08), helmet
```
Bodies are built once per kind (`CharacterArt.CreateBody(gd, def.Style)`) and shared by every instance.

## State machine (per frame)
```csharp
_alertTimer -= dt;                                   // set to 6 s by Aggro() (being shot, bot mode)
switch (State) {
  case Idle:   if (playerAlive && dist < AggroRange && (dist < 200 || world.HasLineOfSight(Position, player.Position))) State = Chase; break;
  case Chase: case Attack:
      if (!playerAlive || (dist > LoseRange && _alertTimer <= 0)) { State = Idle; break; }   // alert timer beats LoseRange
      bool inRange = melee ? dist < Radius + player.Radius + AttackRange
                           : dist < AttackRange && world.HasLineOfSight(...);
      State = inRange ? Attack : Chase; break;
}
```
Gotcha found by testing: `Aggro()` that only sets `State = Chase` flip-flops back to Idle next frame when the enemy is
outside `LoseRange` — hence the alert timer.

## Behaviours
- **Idle**: pick a wander target near `_home` every 2–5 s (35 % chance to just stand), walk at 35 % speed, face it slowly.
- **Chase**: `wanted = SteerToward(player) * Speed`; face the player. Gunners also fire on the move inside 1.3× range with LOS.
- **Attack (melee)**: `_attackTimer` cooldown → accumulate `_windUp`; at `WindUp` deal damage if still in reach; keep pressing
  in at 35 % speed. Expose `WindUpProgress` so the HUD can draw a warning ring.
- **Attack (ranged)**: distance band 170–320 px (`radial = dist > 320 ? toward : dist < 170 ? away : 0`), strafe
  perpendicular with a direction that flips every 1–2.5 s, `wanted = normalize(radial*0.8 + side*0.6) * Speed*0.8`, shoot.
- Movement: `Velocity = MathUtil.Approach(Velocity, wanted, 1800*dt)` then `world.ResolveCircle(ref Position, ref Velocity, Radius)`.
- Damage: `TakeDamage` → `Aggro()` + knock-back `Velocity += hitDir * 60`.

## Obstacle sidestep (cheap, no navmesh)
```csharp
var dir = normalize(target - Position); var probe = Position + dir * (Radius + 60);
if (world.CastSegment(Position, probe, out _, out n, out crate) && crate != null) {
    var slide = new Vector2(-n.Y, n.X); if (Vector2.Dot(slide, target - Position) < 0) slide = -slide;   // side closer to target
    dir = normalize(dir * 0.3f + slide * 0.9f);
}
```

## Ranged enemies reuse the player's Weapon class
**Gotcha (found in play):** the AI holds the trigger permanently, so its `WeaponDef` MUST be `Automatic = true`. A
semi-auto def (edge-triggered `TryFire`) fires exactly once and then never again — "enemies aren't shooting".
`_weapon.FinishReloadIfDue(999); if (_weapon.AmmoInMag <= 0 && !_weapon.IsReloading) { _weapon.BeginReload(999); return; }`
then `TryFire(true, Facing, false, infiniteAmmo:false, out ang)` → `projectiles.Spawn(muzzle, dir*speed, dmg, ricochets, Faction.Enemy, tracer)`
+ `lights.Flash(muzzle, orange, 220, 1.6, 0.08)`. Magazine + reload gives natural firing pauses.

## Manager
- Population target `min(MaxAlive, 4 + Kills/3)`; spawn one every 1.2 s at `world.RandomClearPoint(player.Position, 950, 26)`.
- Pairwise `Collision.SeparateCircles` between alive enemies (O(n²) is fine for ≤ 16); player also pushes enemies.
- `Died` handler: kills++, score += value, `pickups.SpawnBurst(pos, def.Loot.Roll(), 140)`, dark red puff.
- Dead enemies keep drawing (dark tint, no boots) and are removed after `DeadTimer > 1.6 s`; draw dead before alive.
- `HittableCharacters(player)` = player + alive enemies for the projectile system; `ResetAggro()` on player respawn.
