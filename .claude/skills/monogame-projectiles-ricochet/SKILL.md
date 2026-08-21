---
name: monogame-projectiles-ricochet
description: Bullet/projectile simulation for 2D MonoGame games — a weapon state machine (magazine, reload, semi/auto trigger edge, spread, pellets), pooled bullet structs updated as one segment cast per frame (slab segment-vs-AABB, segment-vs-circle) so fast bullets never tunnel, ricochet off surface normals with energy/damage loss and angle jitter, owner/faction hit filtering, destructible obstacle hits, and tracer rendering as transient quads through a particle system. Use when adding shooting, ricochets or hitscan-like projectiles to a 2D MonoGame game.
---

# Projectiles, ricochet & weapons

## Weapon state machine
```csharp
public sealed record WeaponDef(string Name, ItemType? Ammo, float FireInterval, float BulletSpeed, float Damage,
    float Spread, float MoveSpread, int MagSize, float ReloadTime, int MaxRicochets, Vector3 Tracer, float MuzzleFlash, bool Automatic);
// keep presets as `with {}` copies of a base def (e.g. an AI variant with lower accuracy and Automatic = true)

public sealed class Weapon {
  public int AmmoInMag; public float Cooldown, ReloadTimer, Recoil, Flash; public bool TriggerWasDown;
  public void Update(float dt)  { Cooldown -= dt; Recoil -= 9*dt; Flash -= 14*dt; ReloadTimer -= dt; (all clamped >= 0) }
  public bool BeginReload(int reserve)          // only if reserve > 0 and mag not full
  public int  FinishReloadIfDue(int reserve)    // call every frame; returns rounds taken from the reserve when the timer hits 0
  public bool TryFire(bool triggerDown, float facing, bool moving, bool infiniteAmmo, out float bulletAngle)
  // semi-auto: fires only on the trigger edge (triggerDown && !TriggerWasDown); auto: while held
  // returns angle = facing + Rng.Signed(moving ? MoveSpread : Spread) and sets Cooldown/Recoil/Flash
}
```
Caller usage: `int taken = w.FinishReloadIfDue(reserve); if (taken > 0) inventory.Remove(ammoType, taken);` and
"hold-to-fire on empty reloads": `if (trigger && w.AmmoInMag <= 0 && !w.IsReloading) TryReload(quiet: !input.LeftPressed);`
Set `TriggerWasDown = true` after swapping weapons so the same click doesn't fire the new one.
AI-held weapons must be `Automatic = true` (a constant `triggerDown = true` never produces an edge -> one shot ever).

## Magazines & pellets
Alternative to loose rounds: `WeaponDef.Mag` = magazine item consumed per reload (`FinishReloadIfDue(spareMags)` returns 1
and loads a full mag). For shotguns add `Pellets/PelletSpread` — the caller loops `w.PelletAngle(ang, i)` and spawns each
pellet with a short life (~0.5 s starting value). A `Range` field is useful for AI engagement distance.

## Bullet pool + one segment per frame
```csharp
public struct Bullet { public Vector2 Position, Velocity; public float Life, Damage; public int RicochetsLeft; public Faction Owner; public Vector3 Color; }
// per bullet per frame:
var start = b.Position, end = start + b.Velocity * dt;          // ONE segment per frame — no per-pixel stepping needed
// 1) nearest opposing character along the segment
foreach (var c in characters) if (c.IsAlive && c.Faction != b.Owner && Collision.SegmentVsCircle(start, end, c.Position, c.Radius, out t) && t < bestT) ...
// 2) nearest world hit (obstacles + world edge)
bool worldHit = world.CastSegment(start, end, out wt, out normal, out obstacle);
if (hitChar != null && (!worldHit || charT <= wt)) { hitChar.TakeDamage(dmg, dir, hitPos); sparks; kill; }
else if (worldHit) { ricochet-or-die (below) } else b.Position = end;
```
Because a whole frame is one segment cast, a bullet at 1600 px/s at 60 fps (27 px/frame) can never skip a 96 px obstacle.

## Ricochet
```csharp
var dir = normalize(b.Velocity); float speed = |b.Velocity|;
float incidence = |dot(dir, normal)|;                    // 1 head-on, 0 grazing
bool canBounce = b.RicochetsLeft > 0 && speed * KeepSpeed >= MinRicochetSpeed;      // starting values: 0.72, 350 px/s
bool bounce = canBounce && (incidence < 0.35f || Rng.Chance(0.85f));                  // grazing always bounces
if (bounce) {
    var refl = MathUtil.Reflect(dir, normal);                                          // v - 2(v·n)n
    refl = MathUtil.Rotate(refl, Rng.Signed(0.09f * (0.6f + incidence)));             // jitter, more when head-on
    b.Velocity = refl * speed * 0.72f; b.Damage *= 0.65f; b.RicochetsLeft--;
    b.Position = hitPos + normal * 1.5f;                                               // step off the surface (avoid re-hit)
    particles.Sparks(hitPos, refl, 8, sparkColor, 320f); lights.Flash(hitPos, flashColor, 140f, 0.9f, 0.12f);
} else { impact sparks + dust puff; kill; }
```
The outward normal comes from the slab test; the world edge is four half-planes with fixed normals.

## Geometry
- `SegmentVsRect(a, b, rect, out t, out normal)` — slab method per axis; keeps the entry normal of the axis with the
  largest `tmin`; a segment starting inside reports `t = 0`, normal = `-normalize(d)`.
- `SegmentVsCircle(a, b, c, r, out t)` — quadratic; returns the first root in [0,1], or `max(t1,0)` if it starts inside.
- `World.CastSegment` = nearest obstacle hit + world edges (copy `out` params to locals — a local function can't capture them).

## Rendering tracers
Bullets are not particles: each frame `projectiles.DrawTracers()` calls `particles.AddQuad(pos, angle, size 4.5, aspect 6, color)`
(transient quads appended after the live particles, uploaded in the same `DynamicVertexBuffer`). Colour values > 1.0 push
them over a bloom threshold.

## Destructible obstacles
Raise an event (e.g. `ObstacleBroken`) when a destructible obstacle's `Health` reaches 0 (a few hits); the host decides
what to spawn. Non-destructible obstacles just set a hit-flash value (`HitFlash = 1`) that decays for the renderer.
