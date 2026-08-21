---
name: monogame-weapon-attachments-gear
description: Weapon attachment slots, wearable gear, torches as real spot lights, laser sights, melee weapons and grenades for a MonoGame top-down shooter — AttachmentDef/GearDef data records, per-weapon attach points with overlay sprites on the arm layer, multiplier stacking (spread/recoil/flash/noise/range) as products over fitted items, cone-light support in a deferred point-light shader, a segment-cast laser drawn as a transient quad, a grenade system (slide, bounce, fuse, radial blast with knock-back), a melee swing with arc hit-test and arm animation, equip/detach/wear flows in inventory UIs, and loadout persistence. Use when adding gun customisation, armor, throwables or melee to a MonoGame game.
---

# Attachments, gear, torch/laser, grenades, melee

## Data
```csharp
enum AttachSlot { Optic, Muzzle, Tactical, Grip }   enum GearSlot { Helmet, Vest }
record AttachmentDef(ItemType Item, AttachSlot Slot, float SpreadMul=1, RecoilMul=1, FlashMul=1, float RangeAdd=0, bool Torch=false, bool Laser=false, float NoiseMul=1);
record GearDef(ItemType Item, GearSlot Slot, float MaxArmor=0, float Absorb=0, float DamageReduction=0, float SpeedMul=1, HeadGear? Head=null);
static AttachPoints.Get(WeaponKind, AttachSlot) → Vector2? (arm-sprite texels; null = slot not on this gun)
```
`Weapon.Attachments : Dictionary<AttachSlot, ItemType>`; effects are products over fitted items (`SpreadMul`, `RecoilMul`,
`FlashMul`, `NoiseMul`, `RangeAdd` summed, `HasTorch`, `HasLaser` any). `TryAttach(item, out replaced)` checks the slot
exists on the gun; `Detach(slot)`. `WeaponDef.IsMelee` marks melee weapons (no mag, `Range` = reach).

## Where effects plug in
- Fire: `angle = facing + Rng.Signed(spread * SpreadMul)`; `RecoilKick *= RecoilMul`; muzzle light `* FlashMul`;
  spark count scaled by flash; `enemies.AlertNear(pos, baseNoiseRadius * NoiseMul)` — suppressors shrink the alert radius.
- AI range: `weapon.EffectiveRange = Range + RangeAdd`.
- Player damage: helmet `amount *= 1 - DamageReduction`, then vest `absorbed = min(Armor, amount*Absorb)`;
  `Armor` is the vest's durability (`MaxArmor` from the GearDef, repair items refill it, none without a vest); speed `*= SpeedMul`.

## Torch = spot light in the deferred shader
Extend `PointLight` with `Direction`, `ConeOuterDeg/ConeInnerDeg` (`IsSpot`, `ConeCos` → `(cosOuter, cosInner)` or `(-2,-2)` for omni).
HLSL (`ShadePointLight(..., float2 dir, float2 cone)`):
```hlsl
if (cone.x > -1.0) { float c = dot(normalize(P.xy - Lpos.xy), dir); atten *= smoothstep(cone.x, cone.y, c); atten *= saturate(dist / 28.0); }
```
Arrays `LightDirs[]/LightCones[]` for the single-pass path. Because the camera never rotates, world direction ==
screen direction — no transform needed. Player torch: positioned at the muzzle, `Direction = FromAngle(Facing + ArmsAngle)`,
starting values radius 900, 24° outer / 10° inner, intensity 2.4, enabled only while the current weapon `HasTorch`. Giving
some ranged enemies a torch (they roll for one at spawn; it drops with the gun) lets the player see the cone sweeping
before the enemy is visible.

**Toggle**: a `TacticalOn` flag (default true), flipped with a key when the current weapon has a torch or laser; gate
both the cone light and the laser draw on it and show a toast so the player knows why it went dark.

## Laser sight
Segment cast from the muzzle (`world.CastSegment`, then `SegmentVsCircle` against enemies) → length; draw with the
particle system's transient quads: `AddQuad(mid, angle, 2.0, len/2/2.0, color)` + a ~5 px dot at the end.
Lesson learned: the quad's `size` is the half-width in WORLD pixels — 0.8 renders sub-pixel at normal zoom and the
"laser" is effectively invisible. Use ≥ 2 px; and colour channels are clamped at 1.0 by `Color`, so ">1 to bloom"
only works via the bloom threshold, not by overdriving the tint.

## Overlay sprites (attachments visible on the gun)
`Character.ArmOverlays : List<(SpritePair, Vector2 local)>`; drawn right after the arm layer with the same offset/rotation
(`Position + Rotate((armsLocal + local) * Scale, armsRot)`), so recoil/reload/swing carry them. Build one small (~24 px)
shape sprite per attachment kind once. `Player.RefreshVisuals()` rebuilds overlays and swaps head (bare ↔ helmet) and
torso (bare / light vest / heavy vest) layers whenever gear or the current weapon changes — keep alternative layers
pre-built, just reassign `Rig.Head/Rig.Torso` (see monogame-character-rig).

## Grenades
```csharp
Throw(from, dir * clamp(distToCursor*2.1, 180, 760) + vel*0.3, owner)   // friction exp(-2.2 dt) ⇒ lands near the cursor
Update: fuse -= dt; move by one segment; CastSegment hit → reflect * 0.45 + step off the surface; fuse sparkle
Explode: characters within blastRadius+radius take dmg*(0.2+0.8*k) (k = 1 - dist/radius) + knock-back; destructible
         obstacles within a smaller radius break (same event as bullets); enemies.AlertNear(p, r); flash light ~0.35 s; sparks + smoke ring
```
Draw the grenade sprite rotated without normal rotation (`rotateNormals:false`). A bot mode that lobs one when an enemy is
within range lets the whole path be verified headlessly (starting values: throw 180–760 px, blast 150 px, 0.35 s flash).

## Melee swing (player)
Trigger edge → `_meleeTimer = 0.32`; at t=0.14 apply damage to enemies within `Range + radii` inside a 100° arc
(`dot(dirToEnemy, fwd) >= cos 50°`), knock-back ~220, whiff puff if nothing hit; cooldown = `FireInterval`.
Arm animation: `ArmsAngle` −1.0 rad wind-up for the first 40 % of the swing then sweeps to +1.1; damp 40 while swinging,
carry pose −0.3 at rest. Silent (no `AlertNear`).

## UI flows (stash and in-level inventory screens)
- Weapon slots show one mini box per `AttachSlot`; disabled look when the gun lacks that slot; LMB on a filled box →
  detach to bag; attachments also drag in/out (matching `AttachSlot` only — "Wrong slot" status otherwise), and
  RMB-inspecting a gun opens a popup with the same slots drawn large (see monogame-inventory-screen).
- Bag: LMB on an attachment → fit to the selected/current weapon (displaced one returns to the bag); LMB on gear → wear
  (swap); drop = drag out of the panel (RMB is inspect). Gear slots per `GearSlot` with icon + durability.
- Loadout persists `WeaponLoadout { Gun, Attachments }` plus one item per `GearSlot` (JSON: attachments as slot→item strings).
- Containers open into the same loot screen as bodies: `Container.EnsureContents()` rolls once, so shooting or blasting
  it spills the same contents on the floor.

## Pitfalls
- Rebuild overlays on every weapon switch/attach/detach; cache per-attachment sprites once, not per weapon.
- The spot cone needs the `dist/28` fade or the pixel under the torch body blows out.
- Check the bitmap font's glyph set — missing glyphs (e.g. `;`) render as `?`; stick to `-`/`|` in UI strings.
- Keep the HUD weapon panel wide enough: attachment names and grenade count need their own column.
