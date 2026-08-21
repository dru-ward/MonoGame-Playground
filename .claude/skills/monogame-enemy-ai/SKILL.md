---
name: monogame-enemy-ai
description: Enemy characters for a top-down MonoGame action game — data-driven EnemyDef records with shared per-kind bodies, an Idle/Chase/Attack/Dead state machine with aggro/lose ranges and an alert timer that survives being shot from beyond lose range, melee wind-up attacks with an exposed progress value, ranged kiting (distance band + flipping strafe + line-of-sight gating), a cheap probe-and-slide obstacle sidestep with no navmesh, pairwise circle separation, population-based spawning away from the player, death events for loot drops and corpse fade-out. Includes the automatic-fire gotcha when AI reuses the player's weapon class. Use when adding enemies, AI behaviour or spawners to a MonoGame game.
---

# Enemy AI

## Data-driven kinds
```csharp
public sealed record EnemyDef(EnemyKind Kind, string Name, float Health, float Speed, float AggroRange, float LoseRange,
    float AttackRange, float AttackDamage, float AttackInterval, float WindUp, int ScoreValue,
    CharacterStyle Style, LootTable Loot, WeaponDef? Weapon);
```
Example shape of two kinds (numbers are a worked example, not a recommendation):
- Melee kind: ~60 hp, 250 px/s, aggro 560, lose 900, attacks at contact (+6 px), 14 dmg every 0.9 s after a 0.3 s wind-up.
- Ranged kind: ~45 hp, 205 px/s, aggro 680, lose 1000, shoots inside 470 px with LOS; its `WeaponDef` gives fire interval, damage and spread.

Bodies/rigs are built once per kind (`CharacterArt.CreateBody(gd, def.Style)`) and shared by every instance.
`AttackRange => _weapon?.Def.Range` drives both the state machine and the kiting band (`far = 0.68·range,
near = 0.36·range`) so short-range weapons rush and long-range weapons hang back. If ranged enemies pick a random
weapon from a pool, build the rig with an arm layer for every weapon in the pool and set the held-weapon enum from the pick.

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
Gotcha found by testing: an `Aggro()` that only sets `State = Chase` flip-flops back to Idle next frame when the enemy is
outside `LoseRange` — hence the alert timer.

## Behaviours
- **Idle**: pick a wander target near `_home` every 2–5 s (35 % chance to just stand), walk at 35 % speed, face it slowly.
- **Chase**: `wanted = SteerToward(player) * Speed`; face the player. Ranged kinds also fire on the move inside 1.3× range with LOS.
- **Attack (melee)**: `_attackTimer` cooldown → accumulate `_windUp`; at `WindUp` deal damage if still in reach; keep pressing
  in at 35 % speed. Expose `WindUpProgress` so the HUD can draw a warning ring.
- **Attack (ranged)**: distance band (e.g. 170–320 px: `radial = dist > far ? toward : dist < near ? away : 0`), strafe
  perpendicular with a direction that flips every 1–2.5 s, `wanted = normalize(radial*0.8 + side*0.6) * Speed*0.8`, shoot.
- Movement: `Velocity = MathUtil.Approach(Velocity, wanted, 1800*dt)` then `world.ResolveCircle(ref Position, ref Velocity, Radius)`.
- Damage: `TakeDamage` → `Aggro()` + knock-back `Velocity += hitDir * 60`.

## Obstacle sidestep (cheap, no navmesh)
```csharp
var dir = normalize(target - Position); var probe = Position + dir * (Radius + 60);
if (world.CastSegment(Position, probe, out _, out n, out obstacle) && obstacle != null) {
    var slide = new Vector2(-n.Y, n.X); if (Vector2.Dot(slide, target - Position) < 0) slide = -slide;   // side closer to target
    dir = normalize(dir * 0.3f + slide * 0.9f);
}
```

## Ranged enemies reusing the player's Weapon class
**Gotcha (found in play):** the AI holds the trigger permanently, so its `WeaponDef` MUST be `Automatic = true`. A
semi-auto def (edge-triggered `TryFire`) fires exactly once and then never again — "enemies aren't shooting".
```csharp
_weapon.FinishReloadIfDue(reserveAmmo);
if (_weapon.AmmoInMag <= 0 && !_weapon.IsReloading) { _weapon.BeginReload(reserveAmmo); return; }
if (_weapon.TryFire(true, Facing, false, infiniteAmmo:false, out ang))
    projectiles.Spawn(muzzle, dir*speed, dmg, ricochets, Faction.Enemy, tracer);  // + a short muzzle light flash (~0.08 s)
```
Magazine + reload gives natural firing pauses. Multi-projectile weapons spawn `Pellets` projectiles per shot with
per-pellet spread.

## Manager
- Population target `min(MaxAlive, base + Kills/3)` (starting values: base 4); spawn one every 1.2 s at
  `world.RandomClearPoint(player.Position, minDistance, radius)` so spawns are never in view.
- Pairwise `Collision.SeparateCircles` between alive enemies (O(n²) is fine for ≤ 16); the player also pushes enemies.
- `Died` handler: kills++, score += value, drop `def.Loot.Roll()` as a pickup burst, spawn a puff.
- Dead enemies keep drawing (dark tint, no boots) and are removed after `DeadTimer` exceeds a fade time (1.6 s), or
  persist as searchable corpses (e.g. 90 s, fading after being searched); draw dead before alive.
- `HittableCharacters(player)` = player + alive enemies for the projectile system; `ResetAggro()` on player respawn.
