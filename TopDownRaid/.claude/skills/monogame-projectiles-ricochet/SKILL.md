---
name: monogame-projectiles-ricochet
description: Bullet/projectile simulation for 2D MonoGame games — pooled bullet structs, sub-stepped segment casts (slab segment-vs-AABB, segment-vs-circle) so fast bullets never tunnel, ricochet off surface normals with energy/damage loss and jitter, faction-based hits, crate breaking, tracer rendering through the particle system, and weapon state (magazine, reload, semi/auto, spread). Use when adding shooting, ricochets or hitscan-like projectiles to a MonoGame game.
---

# Projectiles, ricochet & weapons

## Weapon state machine (`Combat/Weapon.cs`)
```csharp
public sealed record WeaponDef(string Name, HeldWeapon Held, ItemType? Ammo, float FireInterval, float BulletSpeed, float Damage,
    float Spread, float SprintSpread, int MagSize, float ReloadTime, int MaxRicochets, Vector3 Tracer, float MuzzleFlash, bool Automatic);
// presets: WeaponDef.Rifle (auto, 30 rnd, 2 ricochets), Pistol (semi, 12 rnd, 3 ricochets), EnemyPistol = Pistol with { ... }

public sealed class Weapon {
  public int AmmoInMag; public float Cooldown, ReloadTimer, Recoil, Flash; public bool TriggerWasDown;
  public void Update(float dt)  { Cooldown -= dt; Recoil -= 9*dt; Flash -= 14*dt; ReloadTimer -= dt; (all clamped ≥0) }
  public bool BeginReload(int reserve)          // only if reserve > 0 and mag not full
  public int  FinishReloadIfDue(int reserve)    // call every frame; returns rounds taken from the reserve when the timer hits 0
  public bool TryFire(bool triggerDown, float facing, bool sprinting, bool infiniteAmmo, out float bulletAngle)
  // semi-auto: fires only on the trigger edge (triggerDown && !TriggerWasDown); auto: while held
  // returns angle = facing + Rng.Signed(sprinting ? SprintSpread : Spread) and sets Cooldown/Recoil/Flash
}
```
Player usage: `int taken = w.FinishReloadIfDue(reserve); if (taken > 0) Inventory.Remove(ammoType, taken);` and
"hold-to-fire on empty reloads": `if (trigger && w.AmmoInMag <= 0 && !w.IsReloading) TryReload(quiet: !input.LeftPressed);`
Set `TriggerWasDown = true` after swapping weapons so the same click doesn't fire the new one.
AI-held weapons must be `Automatic = true` (a constant `triggerDown = true` never produces an edge → one shot ever).

## Magazines & pellets (current)
`WeaponDef.Mag` = magazine item consumed per reload (`FinishReloadIfDue(spareMags)` returns 1 and loads a full mag);
`Pellets/PelletSpread` for shotguns — the caller loops `w.PelletAngle(ang, i)` and spawns each pellet with life 0.5 s;
`Range` is used by the AI. Enemy variants are `with {}` copies with lower damage/accuracy and `Automatic = true`.

## Bullet pool + sub-stepped update (`Combat/ProjectileSystem.cs`)
```csharp
public struct Bullet { public Vector2 Position, Velocity; public float Life, Damage; public int RicochetsLeft; public Faction Owner; public Vector3 Color; }
// per bullet per frame:
var start = b.Position, end = start + b.Velocity * dt;          // ONE segment per frame — no per-pixel stepping needed
// 1) nearest opposing character along the segment
foreach (var c in characters) if (c.IsAlive && c.Faction != b.Owner && Collision.SegmentVsCircle(start, end, c.Position, c.Radius, out t) && t < bestT) ...
// 2) nearest world hit (crates + arena edge)
bool worldHit = world.CastSegment(start, end, out wt, out normal, out crate);
if (hitChar != null && (!worldHit || charT <= wt)) { hitChar.TakeDamage(dmg, dir, hitPos); sparks; kill; }
else if (worldHit) { ricochet-or-die (below) } else b.Position = end;
```
Because a whole frame is one segment cast, speed 1600 px/s at 60 fps (27 px/frame) can never skip a 96 px crate.

## Ricochet
```csharp
var dir = normalize(b.Velocity); float speed = |b.Velocity|;
float incidence = |dot(dir, normal)|;                    // 1 head-on, 0 grazing
bool canBounce = b.RicochetsLeft > 0 && speed * KeepSpeed >= MinRicochetSpeed;      // 0.72, 350 px/s
bool bounce = canBounce && (incidence < 0.35f || Rng.Chance(0.85f));                  // grazing always bounces
if (bounce) {
    var refl = MathUtil.Reflect(dir, normal);                                          // v - 2(v·n)n
    refl = MathUtil.Rotate(refl, Rng.Signed(0.09f * (0.6f + incidence)));             // jitter, more when head-on
    b.Velocity = refl * speed * 0.72f; b.Damage *= 0.65f; b.RicochetsLeft--;
    b.Position = hitPos + normal * 1.5f;                                               // step off the surface (avoid re-hit)
    particles.Sparks(hitPos, refl, 8, gold, 320f); lights.Flash(hitPos, orange, 140f, 0.9f, 0.12f);
} else { impact sparks + dust puff; kill; }
```
The outward normal comes from the slab test; the arena edge is four half-planes with fixed normals.

## Geometry (`World/Collision.cs`)
- `SegmentVsRect(a, b, rect, out t, out normal)` — slab method per axis; keeps the entry normal of the axis with the
  largest `tmin`; a segment starting inside reports `t = 0`, normal = `-normalize(d)`.
- `SegmentVsCircle(a, b, c, r, out t)` — quadratic; returns the first root in [0,1], or `max(t1,0)` if it starts inside.
- `GameWorld.CastSegment` = nearest crate hit + arena edges (locals for `out` params, local function can't capture them).

## Rendering tracers
Bullets are not particles: each frame `projectiles.DrawTracers()` calls `particles.AddQuad(pos, angle, size 4.5, aspect 6, color)`
(transient quads appended after the live particles, uploaded in the same `DynamicVertexBuffer`). Colours > 1.0 make them bloom.

## Crate breaking / loot from bullets
`ProjectileSystem.CrateBroken` event fires when a lootable crate's `Health` reaches 0 (3 hits); the host rolls the loot
table and spawns a `PickupManager.SpawnBurst`. Non-lootable crates just flash (`crate.HitFlash = 1`).
