---
name: monogame-weapon-attachments-gear
description: Weapon attachment slots, wearable armor, torches as real spot lights, laser sights, melee weapons and grenades for a MonoGame top-down shooter — AttachmentDef/GearDef data, per-weapon attach points with overlay sprites on the arm layer, multiplier stacking (spread/recoil/flash/noise/range), cone-light support in the deferred lighting shader, a GrenadeSystem (slide, bounce, fuse, radial blast), a melee swing with arc hit-test and arm animation, equip/detach/wear flows in the stash and raid inventory UIs, and loadout persistence. Use when adding gun customisation, armor, throwables or melee to a MonoGame game.
---

# Attachments, gear, torch/laser, grenades, melee (`Combat/Gear.cs`, `Combat/GrenadeSystem.cs`, `Entities/Player.cs`)

## Data
```csharp
enum AttachSlot { Optic, Muzzle, Tactical, Grip }   enum GearSlot { Helmet, Vest }
record AttachmentDef(ItemType Item, AttachSlot Slot, float SpreadMul=1, RecoilMul=1, FlashMul=1, float RangeAdd=0, bool Torch=false, bool Laser=false, float NoiseMul=1);
// Optic .70 spread +80 range · Suppressor flash .15 noise .4 · Compensator recoil .65 spread .85 · Torch · Laser spread .75 · Grip recoil .70
record GearDef(ItemType Item, GearSlot Slot, float MaxArmor=0, float Absorb=0, float DamageReduction=0, float SpeedMul=1, HeadGear? Head=null);
// VestLight 60/55% · VestHeavy 120/75% speed .88 · HelmetSteel -15% · HelmetTac -25%
static AttachPoints.Get(HeldWeapon, AttachSlot) → Vector2? (arm-sprite texels; null = slot not on this gun)
```
`Weapon.Attachments : Dictionary<AttachSlot, ItemType>`; effects are products over fitted items (`SpreadMul`, `RecoilMul`,
`FlashMul`, `NoiseMul`, `RangeAdd`, `HasTorch`, `HasLaser`). `TryAttach(item, out replaced)` checks the slot exists on
the gun; `Detach(slot)`. `WeaponDef.IsMelee` marks the bat (no mag, `Range` = reach).

## Where effects plug in
- Fire: `angle = facing + Rng.Signed(spread * SpreadMul)`; `RecoilKick *= RecoilMul`; muzzle light `* FlashMul`;
  spark count scaled by flash; `enemies.AlertNear(pos, 720 * NoiseMul)` — suppressors shrink the alert radius.
- AI range: `weapon.EffectiveRange = Range + RangeAdd`.
- Player damage: helmet `amount *= 1 - DamageReduction`, then vest `absorbed = min(Armor, amount*Absorb)`;
  `Armor` is the vest's durability (`MaxArmor` from the GearDef, plates refill it, none without a vest); speed `*= SpeedMul`.

## Torch = spot light in the deferred shader
`PointLight` gained `Direction`, `ConeOuterDeg/ConeInnerDeg` (`IsSpot`, `ConeCos` → `(cosOuter, cosInner)` or `(-2,-2)` for omni).
HLSL (`ShadePointLight(..., float2 dir, float2 cone)`):
```hlsl
if (cone.x > -1.0) { float c = dot(normalize(P.xy - Lpos.xy), dir); atten *= smoothstep(cone.x, cone.y, c); atten *= saturate(dist / 28.0); }
```
Arrays `LightDirs[]/LightCones[]` for the single-pass path. Because the camera never rotates, world direction ==
screen direction — no transform needed. Player torch: at the muzzle, `Direction = FromAngle(Facing + ArmsAngle)`,
radius 900, 24°/10°, intensity 2.4, enabled only while the current weapon `HasTorch`. Enemy gunners roll a torch 35 % of
the time (you see the cone sweeping before you see them); it drops with the gun.

## Laser sight
Segment cast from the muzzle (`world.CastSegment`, then `SegmentVsCircle` against enemies) → length; draw with the
particle system's transient quads: `AddQuad(mid, angle, 0.8, len/2/0.8, red)` + a dot at the end. Colour > 1 so it blooms.

## Overlay sprites (attachments visible on the gun)
`Character.ArmOverlays : List<(SpritePair, Vector2 local)>`; drawn right after the arm layer with the same offset/rotation
(`Position + Rotate((armsLocal + local) * Scale, armsRot)`), so recoil/reload/swing carry them. `AttachmentArt.CreateAll`
builds 24 px shape sprites (red dot, suppressor tube, slotted compensator, torch with bright lens, laser box, vertical
grip). `Player.RefreshVisuals()` rebuilds overlays and swaps head (cap ↔ helmet) and torso (bare / light vest / heavy vest)
layers whenever gear or the current weapon changes — keep alternative layers pre-built, just reassign `Rig.Head/Rig.Torso`.

## Grenades (`GrenadeSystem`)
```csharp
Throw(from, dir * clamp(distToCursor*2.1, 180, 760) + vel*0.3, owner)   // friction exp(-2.2 dt) ⇒ lands near the cursor
Update: fuse -= dt; move by one segment; CastSegment hit → reflect * 0.45 + step off the surface; fuse sparkle
Explode: characters within 150+radius take 110*(0.2+0.8*k) (k = 1 - dist/radius) + knock-back; lootable crates within
         120 px break (CrateBroken event → spill); enemies.AlertNear(p, 900); flash light 520 px 0.35 s; sparks + smoke ring
```
Drawn with the grenade icon via `SceneBatch.DrawRotated(..., rotateNormals:false)`. Bot mode lobs one when an enemy is
inside 420 px (headless verification of the whole path).

## Melee swing (player)
Trigger edge → `_meleeTimer = 0.32`; at t=0.14 apply damage to enemies within `Range + radii` inside a 100° arc
(`dot(dirToEnemy, fwd) >= cos 50°`), knock-back 220, whiff puff if nothing hit; cooldown = `FireInterval`.
Arm animation: `ArmsAngle` −1.0 rad wind-up for the first 40 % of the swing then sweeps to +1.1; damp 40 while swinging,
carry pose −0.3 at rest. Silent (no `AlertNear`).

## UI flows (both screens)
- Weapon slots show 4 mini boxes (O/M/T/G); disabled look when the gun lacks that slot; LMB on a filled box → detach to bag.
- Bag: LMB on an attachment → fit to the selected/current weapon (displaced one returns to the bag); LMB on gear → wear
  (swap); RMB → drop (raid) / back to stash (hideout). Gear slots: HELMET / VEST with icon + durability.
- Loadout persists `WeaponLoadout { Gun, Attachments }`, `Helmet`, `Vest` (JSON: attachments as slot→item strings).
- Containers (crates/caches) open into the same loot screen as bodies: `Crate.EnsureContents()` rolls once, shooting or
  blasting a crate spills the same contents on the floor.

## Pitfalls
- Rebuild overlays on every weapon switch/attach/detach; cache per-attachment sprites once, not per weapon.
- The spot cone needs `dist/28` fade or the pixel under the torch body blows out.
- Missing font glyphs (`;`) render as `?` — stick to `-`/`|` in UI strings.
- Keep the HUD weapon panel wide enough: attachment names and grenade count need their own column.
