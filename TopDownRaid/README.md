# TopDown — MonoGame deferred-lighting arena shooter (DesktopGL, MonoGame 3.8.5)

A small top-down shooter built on a deferred-style multi-pass renderer. Everything (textures, characters, icons,
font) is generated procedurally at start-up, so it runs with zero external art.

```
dotnet build      # compiles C# and runs MGCB (Content/Shaders/Deferred.fx -> .xnb)
dotnet run
```

## The loop (Tarkov-style)
**Menu → Hideout → Map select → Raid → Summary → Hideout.** Build a loadout from your **stash** (48 slots, persistent),
pick a **map**, deploy, loot, and reach an **extraction zone** before the raid timer runs out — stand inside it for 5 s.
Extract and everything you carry (guns, bag) comes back to the hideout; die or time out and it is all gone, but the stash
is always safe. If the loadout has no gun a free pistol is issued. Profile (stash, loadout, gold, stats) is saved as JSON
in `%LocalAppData%\TopDownRaid\profile.json`.

Maps (`World/MapDef.cs`): **Scrapyard** (easy, 3072², 2 exits, 8 min), **Docks** (medium, 3584², container stacks,
cold floods, 3 exits, 10 min), **Factory** (hard, 2560², barriers + sandbags, barrels for light, 2 exits, 7 min),
**Meadow** (medium, 3584², outdoor daylight: grass floor with scattered tufts, tree lines, bushes, wrecked cars with
lootable boots, bright ambient, no lamps, 3 exits, 9 min). Each
defines seed, size, prop mix, lamps, enemy pressure, ambient and extract positions; the map cards show a real layout preview.

## Gameplay
- **Player**: top-down human built as a 5-layer rig — shadow, striding boots, torso (lags the aim, sways),
  arms+weapon (recoil, reload, swap and sprint animation), head (bob) — with silhouette outlines, cap, plate carrier,
  backpack, radio, holster, gloves. WASD moves, mouse aims (twin-stick), LMB fires, `R` reloads, `Q` swaps weapon,
  `Shift` sprints (wider spread).
- **Enemies** spawn away from you and keep a growing population: **Brawlers** (bat, wind-up melee at contact) and
  **Gunners** (helmet) who spawn with a **random gun** — pistol, rifle, SMG or shotgun — and fight at that weapon's
  range (shotgunners push in, riflemen hang back). They aggro inside their range or when shot (6 s alert) and lose
  interest beyond `LoseRange`.
- **Bodies stay lootable**: a dead enemy keeps an inventory holding his gun (player-grade), 1–3 magazines for it and
  personal effects. Walk up, press `E` → the loot screen opens; take single items or `F` take all. Searched bodies
  fade out; unsearched ones last 90 s.
- **Weapons & magazines**: assault rifle, pistol, SMG (spray), pump shotgun (8 pellets) and a **nail bat** (melee:
  silent 100° swing, knock-back). Ammo is carried as **magazines** — a reload consumes one mag item and loads a full
  magazine. Carry up to 3 weapons; equip from the bag (LMB / hotbar), unequip back into the bag (RMB on a weapon slot).
- **Attachment slots** per weapon (Optic / Muzzle / Tactical / Grip, not every gun has every slot): **Red Dot**
  (-30 % spread, +range), **Suppressor** (tiny flash, enemies hear you from much closer), **Compensator** (-recoil,
  -spread), **Torch** (a real cone light from the muzzle), **Laser** (aiming line to the first obstacle, -spread),
  **Fore Grip** (-recoil). Attachments are drawn on the weapon, drop with enemy guns, and persist in the stash/loadout.
- **Body armor & helmets**: Light Vest (60 armor, absorbs 55 %), Heavy Vest (120, 75 %, slower), Steel/Tac Helmet
  (-15 % / -25 % damage). Vests wear down (plates refill them); worn gear changes your character's head/torso.
- **Grenades** (`G` or hotbar): 2.5 s fuse, slide + bounce off props, 150 px blast with falloff (hurts you too),
  breaks caches, alerts everyone nearby.
- **Bullets** are sub-stepped segments: they hit characters (damage, knock-back, blood sparks) or the world, where
  they **ricochet** off crate faces / arena walls (reflect around the face normal, keep 72 % speed, 65 % damage,
  small random deflection, up to 2–3 bounces, spark burst + flash light) — grazing hits always bounce.
- **Loot**: light-tinted crates and shipping containers (caches: better gear/attachments) open into the **same loot
  screen as bodies** (`E`); shooting or blasting them open spills the same contents on the floor instead. Pickups
  pop out, slide and sparkle; walk up and press `E` to take the nearest one (nothing is auto-collected).
- **Inventory screen** (`Tab`/`I`): weapon slots (ammo + spare mags), 15-slot backpack (first row = hotbar
  `1-5`), hover details with weapon stats, LMB use/equip, **drag & drop** to move/swap slots, stash into or take
  from an open container, re-order weapons, fit attachments or replace an equipped gun (drag out of the panel =
  drop on the floor), RMB **inspect** (guns show their four attachment slots — drag attachments straight in/out),
  Ctrl+LMB stash, and a container column while searching a body (LMB take, `F` take all). Items: 5.56/9mm/SMG mags, shells, Bandage
  (+20), Medkit (+60), Armor Plate (+50 armor, absorbs 60 %), gold coins (score), guns.
- Death: 3 s respawn at the start with full health; score persists.

## Look & atmosphere (grungy urban / night raid)
- Floor: seamless 512 px **cracked asphalt** tile (aggregate grain, sparse masked cracks, expansion joints every slab,
  potholes, oil stains, grime patches, gravel specks, a worn dashed lane line) with a matching normal map.
- Props (`Graphics/PropArt.cs`, `World/PropKind`): wooden crates, corrugated **shipping containers**, concrete
  **jersey barriers** (often paired), **sandbag** lines, **rubble** piles with rebar, **burning barrels** and lamp-post
  bases — all shape-list sprites with grime noise; long props come in both orientations.
- Lighting: cold ambient, a jittered grid of **sodium street lamps** (some buzz/drop out) and cold fluorescent floods,
  flickering **fire barrels** that throw embers and smoke, the player's warm headlamp, muzzle/ricochet flashes.
- Post: desaturation, cool-shadow/warm-highlight split tone, contrast, animated film grain, heavy vignette; drifting haze.

## Build, test, play-test
```
dotnet build TopDownRaid.sln        # game + tests
dotnet test  Game.Tests             # 45 xunit tests: inventory, loot tables, weapons/attachments, collision,
                                    #   camera clamping/zoom, map generation, profile save/load
```
Headless play-tests (no desktop needed): `GAME1_NOSAVE=1 GAME1_LOG=1 GAME1_STATE=raid GAME1_BOT=1 GAME1_MAP=docks timeout 60 dotnet Game/bin/Debug/net9.0/Game.dll`
prints a raid log every 5 s and the outcome; add `GAME1_SPAWN_DIST=500` for a stress ring, `GAME1_SPAWN_AT_EXTRACT=1` for
the extraction path, `GAME1_RES=2560x1440 GAME1_ZOOM=0.5` + `GAME1_SCREENSHOT` for camera checks at other resolutions.

## Controls
| Key | Action |
|---|---|
| `W A S D` / arrows, `Shift` | Move / sprint |
| Mouse, LMB | Aim, fire (rifle auto, pistol semi) |
| `R`, `Q` | Reload, swap weapon |
| `E` | Search body / open lootable crate / pick up floor item |
| `G` | Throw grenade (toward the cursor) |
| `T` | Toggle torch / laser attachment on the current weapon |
| `Tab` / `I`, `1-5` | Inventory screen, use hotbar slot |
| `F` (in loot screen) | Take all |
| Wheel | Zoom |
| `Space`, `Esc` | Pause, abandon raid (counts as MIA) / back in menus |
| `F1` `F2` `F3` `F4` | Wire-frame particles, scissor rects, bloom, per-light ⇄ single-pass lighting |
| `F5`–`F10` | Debug views: Albedo, Normal, Light, Scene, Bloom, Final |
| `F11`, `F12` | Toggle debug text, screenshot |

Headless verification env vars: `GAME1_SCREENSHOT=<png>` (save after `GAME1_SHOT_DELAY` s and exit),
`GAME1_VIEW=1..6`, `GAME1_ZOOM=<f>`, `GAME1_BOT=1` (auto-aim/fire at the nearest enemy, enemies aggro at once),
`GAME1_SPAWN_DIST=<px>` (ring the first enemies around the player for rig inspection), `GAME1_UI=inv|loot` (open the inventory/loot screen),
`GAME1_STATE=menu|stash|map|raid|summary` (start in that state; `raid` auto-builds a loadout), `GAME1_MAP=<id>`,
`GAME1_SPAWN_AT_EXTRACT=1` (spawn inside the first extract to test the flow), `GAME1_NOSAVE=1` (don't touch the profile).

## Project structure
```
TopDownGame.cs        host state machine: Menu/Stash/MapSelect/Raid/Summary; owns the pipeline, particles, screens
Meta/       Profile (stash/loadout/stats, JSON save), Raid (one deployment: world, lights, enemies, projectiles, pickups, player, camera, extraction, timer)
Program.cs
Core/       Camera2D (non-rotating follow camera), InputState (edge detection), MathUtil, Rng
Graphics/   RenderPipeline (RTs + passes), GraphicsStates (blend/sampler/rasterizer), SceneBatch (2-pass sprite API,
            rotates normals for rotated sprites), ParticleSystem (DynamicVertexBuffer), LightManager (persistent +
            transient lights), TextureFactory (floor/crate/noise/normal maps/mips), ShapeSprite (shape-list rasteriser
            -> albedo + normal, outlines), CharacterArt (layered human rig: torso/arms/head/boots/shadow, weapons,
            headgear/kit styles), PropArt (urban props), PixelFont (5x7 bitmap font)
World/      MapDef (maps + extract defs), GameWorld (map-driven props, lootable crates, extract zones, collision,
            segment casts, LOS, spawn points), Collision (slab segment-vs-AABB, segment-vs-circle, circle push-out)
Entities/   Character (base: health, aim vs move facing, boots+body draw), Player, Enemy (+EnemyDef, AI states),
            EnemyManager (population, separation), GameContext (shared systems bag)
Combat/     WeaponDef/Weapon (guns + bat + enemy variants, magazine reloads, pellets, attachments), Gear (AttachmentDef/GearDef/
            AttachPoints), ProjectileSystem (bullets, ricochet, damage, crate breaking), GrenadeSystem (throw, bounce, blast)
Items/      ItemDef/ItemStack, Inventory, LootTable + PickupManager, ItemArt (procedural icons)
UI/         Hud (bars, ammo/mags, hotbar, prompts, enemy bars, crosshair, raid timer, extract compass + hold bar),
            InventoryScreen (weapons/bag/container/details), MetaScreens (menu, hideout/stash+loadout, map select
            with previews, summary), UiDraw (shared primitives)
Content/    Content.mgcb, Shaders/Deferred.fx
.claude/skills/   reusable know-how extracted from this project (see below)
```

## Rendering pipeline (`Graphics/RenderPipeline.cs`, `Content/Shaders/Deferred.fx`)
```
Pass 1  AlbedoRT   SceneBatch albedo pass (floor tiled with wrap sampler, pickups, crates, characters)
Pass 2  NormalRT   same draw calls, normal textures; rotated sprites go through SpriteNormalRotate (PS-only)
Pass 3  LightRT    Clear(ambient); per light: additive blend + scissor rect, PointLight (N·L, Blinn-Phong)
Pass 4  SceneRT    Composite (albedo*diffuse + spec) then particles/tracers additively (DynamicVertexBuffer)
Pass 5/6 BloomA/B  bright pass ½ res, separable Gaussian ×2
Pass 7  backbuffer FinalCombine (bloom + vignette) or debug Blit, then the unlit HUD
```
The camera never rotates ⇒ view space == RT pixel space ⇒ lights are transformed on the CPU and the light shader
is matrix-free. Full-screen quads use pixel-space vertices with `CreateOrthographicOffCenter(0,W,H,0,0,1)`.

## MGCB setup
`Content/Content.mgcb` builds `Shaders/Deferred.fx` (EffectImporter/EffectProcessor, `/platform:DesktopGL`).
Real textures can be added with TextureImporter/TextureProcessor; for normal maps use `ColorKeyEnabled=False`,
`PremultiplyAlpha=False`, `TextureFormat=Color`. Rebuild content only: `dotnet mgcb /@:Content/Content.mgcb`.

## Skills (`.claude/skills/*/SKILL.md`)
`monogame-project-setup`, `monogame-game-architecture`, `monogame-hlsl-effects`, `monogame-deferred-2d-lighting`,
`monogame-gpu-particles`, `monogame-procedural-textures`, `monogame-topdown-player`, `monogame-projectiles-ricochet`,
`monogame-enemy-ai`, `monogame-inventory-loot`, `monogame-hud-pixel-font`, `monogame-grunge-visuals`, `monogame-character-rig`,
`monogame-inventory-screen`, `monogame-raid-metagame`, `monogame-weapon-attachments-gear`.
