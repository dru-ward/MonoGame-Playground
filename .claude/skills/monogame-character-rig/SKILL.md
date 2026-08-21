---
name: monogame-character-rig
description: Procedural, layered top-down (bird's-eye) character rig for MonoGame — a 5-layer stack (shadow, boots, torso, arms+weapon, head) drawn from one pivot and rotated independently, modular weapons on the arm layer, a style record for kit variants, shared elbow constants so upper arms and forearms meet across layers, gait animation (stride, torso sway, head bob), arm-layer offset/angle recipes for recoil, reload, weapon swap, sprint, melee wind-up/swing and death poses, a local-to-world mapping for muzzle points, and the outline/normal-map pitfalls (outline glare, dome-rim tilt) of normal-mapped sprites. Use when building or improving animated top-down character sprites without authored animation assets in a MonoGame game.
---

# Top-down character rig (procedural, layered)

## Principles
- Split the body into **independent layers** so weapons are modular and only the arm layer animates (the Hotline
  Miami approach: shared leg sprites under many torsos, weapons as separate sprite sets).
- The **head bob and shoulder pivot** carry the walk cycle; keep the silhouette compact — "what looks good static
  becomes noise when animated", so detail must survive motion.
- **Shadows** sell height in top-down; **outlines** keep dark sprites legible on dark floors.
Sources: [Sandro Maglione – top-down sprite design](https://www.sandromaglione.com/articles/pixel-art-top-down-game-sprite-design-and-animation),
[SLYNYRD – top-down character animation](https://www.slynyrd.com/blog/2025/3/24/pixelblog-55-top-down-character-animation),
[Hotline Miami spriting guide](https://steamcommunity.com/sharedfiles/filedetails/?id=1404594863).

## Layer stack
All layers share one texel size (96×96 is a good starting value) centred on the character position so every layer
rotates about the same pivot; draw at a global sprite scale (e.g. 1.3). Sprite-local axes: +X forward, +Y right.
```
0 Shadow  soft black ellipse (albedo only: its normal texture is fully transparent), offset (+3,+5), rotated with the torso
1 Boots   one boot sprite drawn twice: ±9 texels sideways of MoveFacing, sliding ±7·sin(StridePhase) fore/aft in anti-phase
2 Torso   hips, back-worn gear, torso, chest gear, shoulders, UPPER arms to the elbows, hood/collar ring
3 Arms    forearms from the elbows, hands, the WEAPON under the hands — one sprite per held-weapon kind
4 Head    head + headgear variant
```
Elbows are shared constants (starting values `ElbowR = (7, 12.5)`, `ElbowL = (7, -12.5)`) so the upper arms (torso
layer) and forearms (arm layer) meet even though the two layers rotate slightly differently.

```csharp
public sealed class CharacterRig { SpritePair Torso, Head, Boot, Shadow; Dictionary<HeldWeapon, SpritePair> Arms; }
var rig = CharacterArt.CreateRig(gd, style, new[] { HeldWeapon.Rifle, HeldWeapon.Pistol });
```
A `CharacterStyle` record (colours + boolean/enum kit flags: headgear kind, back gear, chest gear, gloves, sleeves...)
drives generation; keep presets as static members so each character kind builds its rig once and shares it.

## Draw order + motion
```csharp
float moving = clamp(Speed/200), stride = sin(StridePhase)*7*moving;      // StridePhase += dt * Speed / 26
BodyFacing = LerpAngle(BodyFacing, Facing, Damp(9, dt));                    // torso lags the aim
torsoRot = BodyFacing + sin(StridePhase)*0.07*moving;  torsoScale = 1 + 0.025*sin(2*StridePhase)*moving;   // sway + bob
armsPos  = Position + Rotate((ArmsOffset - (RecoilKick/Scale, 0)) * Scale, Facing + ArmsAngle);
headRot  = Facing + HeadTurn;  headScale = 1 + 0.04*sin(2*StridePhase + 0.5)*moving;
dead:    no boots, arms +1.1 rad, head -0.9 rad and offset (-3,+4), dark tint, fade over 1.5 s
```
`WeaponLocalToWorld(local)` maps arm-layer texels (e.g. a per-weapon muzzle point) through the same offset/rotation,
so muzzle flashes and projectiles follow recoil and reload animation.

## Arm-layer animation recipes (set `ArmsOffset` texels / `ArmsAngle` radians, lerp with Damp(14–18))
Starting values:
| Action | Offset | Angle |
|---|---|---|
| Recoil | `-RecoilKick` along X (decays 40 px/s) | – |
| Reload (`p = 1 - ReloadTimer/ReloadTime`, `k = sin(pi p)`) | `(-4, 4)·k` | `-0.35·k` |
| Weapon swap (anim 1→0 at 4/s) | `(-5, 3)·a` | `+0.5·a` |
| Sprint | `(-2, 1)·k` | `+0.18·k` |
| Melee wind-up / swing (swing timer 0.18 s after the hit) | `(-2,0)·windUp` | `-1.1·windUp + 0.9·(swing/0.18)`, damp 40 during swing |
| Melee carry while chasing / idle low-ready | `(-1,1)` / `(-2,1)` | `-0.35` / `+0.25` |

## Outline & normal-map pitfalls
- An outline option dilates the silhouette by an outline width (1.2 texels) with a near-black colour. Compute it
  **after** the shape pass and give outline texels **no normal** (use the un-dilated alpha for the normal map),
  otherwise the height step at the rim becomes a slope that lights up as a bright ring.
- Dome shapes have near-vertical normals at the rim; clamp with a minimum normal Z (0.74 for characters, 0.55 for
  general props) — keep the direction, limit the tilt — or every shoulder gets a specular halo.
- Verify with debug views that blit the albedo buffer (is the artefact colour or lighting?) and the normal buffer;
  zoom the camera 2–3× and crop/upscale the screenshot.
- Boots peeking out under the shoulders while standing still is normal; keep them dark and low-relief.

## Readability rules
- Slightly larger head than life (radius 9.5 on a 96 sprite) and high-contrast headgear tell character kinds apart at
  a glance; hand covering and headgear variants do most of the identity work.
- Keep kit luminance around 0.2–0.45 and skin 0.8+ (if a desaturating colour grade is applied, dark kits need roughly
  +30 % brightness to survive it).
- Shade domes toward the rim (shade 0.35–0.5) for volume even under flat light.
- One draw per layer; rotated layers in the normal pass cost a batch flush each (pixel-shader normal rotation) — 3
  rotated layers × ~12 characters is fine; boots/shadow can skip normal rotation.

## Testing rigs without playing
Provide startup knobs (env vars or CLI) to ring all character kinds around the player at a fixed distance, zoom in,
and save a screenshot after a delay; a bot flag that auto-fires/reloads adds the firing and reload poses to the frame.
