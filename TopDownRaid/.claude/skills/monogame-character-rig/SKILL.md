---
name: monogame-character-rig
description: Build readable, animated top-down (bird's-eye) human characters procedurally in MonoGame — a 5-layer rig (shadow, boots, torso, arms+weapon, head) each rotated independently, modular weapons on the arm layer, style records for kit variants (headgear, vest, backpack, gloves, hood, radio, holster), silhouette outlines, elbow-matched arms, gait animation (stride, torso sway, head bob), and arm-layer animation for recoil, reload, weapon swap, melee wind-up/swing and death poses; plus the normal-map pitfalls (outline glare, dome-rim tilt). Use when designing or improving character sprites/rigs for a top-down MonoGame game.
---

# Top-down character rig (procedural, layered)

## Research takeaways that shaped the rig
- Split the body into **independent layers** so weapons are modular and only the arm layer animates
  (Hotline Miami reuses shared leg sprites under many torsos; weapons are separate sprite sets).
- The **head bob and shoulder pivot** carry the walk cycle; keep the silhouette compact and readable —
  "what looks good static becomes noise when animated", so detail should survive motion.
- **Shadows** sell height/depth in top-down; **outlines** keep dark kit legible on dark floors.
Sources: [Sandro Maglione – top-down sprite design](https://www.sandromaglione.com/articles/pixel-art-top-down-game-sprite-design-and-animation),
[SLYNYRD – top-down character animation](https://www.slynyrd.com/blog/2025/3/24/pixelblog-55-top-down-character-animation),
[Hotline Miami spriting guide](https://steamcommunity.com/sharedfiles/filedetails/?id=1404594863).

## Layer stack (`Graphics/CharacterArt.cs`, `Entities/Character.cs`)
All layers are 96×96 texels centred on the character position (so each rotates about the same pivot); drawn at
`SpriteScale = 1.3`. Sprite-local axes: +X forward, +Y right.
```
0 Shadow  soft black ellipse (albedo only: its "normal" texture is fully transparent), offset (+3,+5), rotated with the torso
1 Boots   one boot sprite drawn twice: ±9 texels sideways of MoveFacing, sliding ±7·sin(StridePhase) fore/aft in anti-phase
2 Torso   hips, backpack + straps, torso, vest/pouches/molle or chest strap, radio+antenna, holster, shoulders, UPPER arms to the elbows, hood ring
3 Arms    forearms from the elbows, hands (gloves or skin), the WEAPON under the hands — one sprite per HeldWeapon
4 Head    head + headgear (Hair / Cap+brim / Helmet+NVG mount+strap / Beanie / Hood with face in the opening)
```
Elbows are shared constants (`ElbowR = (7, 12.5)`, `ElbowL = (7, -12.5)`) so upper arms (torso layer) and forearms
(arm layer) meet even though the layers rotate slightly differently.

```csharp
public sealed class CharacterRig { SpritePair Torso, Head, Boot, Shadow; Dictionary<HeldWeapon, SpritePair> Arms; }
var rig = CharacterArt.CreateRig(gd, CharacterStyle.Player, new[] { HeldWeapon.Rifle, HeldWeapon.Pistol });
```
`CharacterStyle` record: Jacket/Sleeve/Skin/Hair colours, Weapon, `HeadGear`, Backpack, Vest(+colour), Pants,
Gloves, GearColor, Radio, Holster, RolledSleeves. Presets: `Player` (PMC: cap, plate carrier, backpack, gloves, radio,
holster), `Brawler` (Scav: hood up, rolled sleeves, bare hands, bat), `Gunner` (Raider: helmet w/ NVG mount, black kit).

## Draw order + motion (Character.Draw)
```csharp
float moving = clamp(Speed/200), stride = sin(StridePhase)*7*moving;      // StridePhase += dt * Speed / 26
BodyFacing = LerpAngle(BodyFacing, Facing, Damp(9, dt));                    // torso lags the aim (in TickCommon)
torsoRot = BodyFacing + sin(StridePhase)*0.07*moving;  torsoScale = 1 + 0.025*sin(2*StridePhase)*moving;   // sway + bob
armsPos  = Position + Rotate((ArmsOffset - (RecoilKick/Scale, 0)) * Scale, Facing + ArmsAngle);
headRot  = Facing + HeadTurn;  headScale = 1 + 0.04*sin(2*StridePhase + 0.5)*moving;
dead:    no boots, arms +1.1 rad, head -0.9 rad and offset (-3,+4), dark tint, fade over 1.5 s
```
`WeaponLocalToWorld(local)` maps arm-layer texels (e.g. `CharacterArt.MuzzleLocal(weapon)`) through the same
offset/rotation, so muzzle flashes and bullets follow recoil and reload animation.

## Arm-layer animation recipes (set `ArmsOffset` texels / `ArmsAngle` radians, lerp with Damp(14–18))
| Action | Offset | Angle |
|---|---|---|
| Recoil | `-RecoilKick` along X (decays 40 px/s) | – |
| Reload (`p = 1 - ReloadTimer/ReloadTime`, `k = sin(pi p)`) | `(-4, 4)·k` | `-0.35·k` |
| Weapon swap (`_swapAnim` 1→0 at 4/s) | `(-5, 3)·a` | `+0.5·a` |
| Sprint | `(-2, 1)·k` | `+0.18·k` |
| Bat wind-up / swing (`_swingTimer` 0.18 s after the hit) | `(-2,0)·windUp` | `-1.1·windUp + 0.9·(swing/0.18)`, damp 40 during swing |
| Melee carry while chasing / idle low-ready | `(-1,1)` / `(-2,1)` | `-0.35` / `+0.25` |

## Outline & normal-map pitfalls (both hit and fixed)
- `ShapeSprite.Outline = true` dilates the silhouette by `OutlineWidth` (1.2) with a near-black colour. Compute it
  **after** the shape pass and give outline texels **no normal** (`shapeAlpha` without outline for the normal map),
  otherwise the height step at the rim becomes a slope that lights up as a bright ring.
- Dome rims (`Dome = 1`) have near-vertical normals; clamp with `MinNormalZ` (0.74 for characters, 0.55 default) —
  keep the direction, limit the tilt — or every shoulder gets a specular halo.
- Verify with the debug views: `GAME1_VIEW=1` (albedo) shows whether an artefact is colour or lighting; `GAME1_VIEW=2`
  shows the normal buffer. Zoom in with `GAME1_ZOOM=2..3` and crop/upscale the PNG.
- Boots peeking out under the shoulders while standing still is normal; keep them dark and low-relief.

## Readability rules that worked
- Slightly larger head than life (radius 9.5 on a 96 sprite), high-contrast head gear (cap/helmet/hood) → instantly
  tells factions apart at a glance; gloves vs bare hands and hood vs helmet do most of the identity work.
- Layer colours: kit 0.2–0.45 luminance (dark kits under a desaturating grade need ~+30 % brightness), skin 0.8+.
- Shade domes toward the rim (`Shade` 0.35–0.5) for volume even where the light is flat.
- One draw per layer; rotated layers in the normal pass cost a batch flush each (`SceneBatch.DrawRotated`) — 3 rotated
  layers × ~12 characters is fine; boots/shadow use `rotateNormals:false`.

## Testing enemies without playing
`GAME1_SPAWN_DIST=150 GAME1_ZOOM=2 GAME1_SHOT_DELAY=1 GAME1_SCREENSHOT=out.png` rings the initial enemies around the
player so all rigs are in one frame; `GAME1_BOT=1` adds firing/reload poses.
