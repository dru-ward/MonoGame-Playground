---
name: monogame-raid-metagame
description: Extraction-shooter meta-game for MonoGame (Tarkov-style loop) — a host state machine (Menu → Hideout/stash+loadout → Map select → Raid → Summary), a persistent Profile (stash inventory, loadout, gold, stats) saved as JSON, data-driven MapDefs (size, seed, prop weights, lighting, enemy pressure, timer, relative extract positions) with cached layout previews, a Raid session class that owns every in-raid system and yields an outcome (extracted / killed / timed out), extraction zones with hold-to-extract, compass and timer HUD, and headless knobs to jump to any state. Use when adding menus, a hub/stash, multiple maps, persistence or an extract/raid loop to a MonoGame game.
---

# Raid meta-game loop (`Meta/`, `UI/MetaScreens.cs`, `World/MapDef.cs`)

## State machine (host)
```csharp
enum GameState { Menu, Stash, MapSelect, Raid, Summary }
Update: switch(_state) { Menu → _menu.Update() → Stash|Quit; Stash → Deploy|ChooseMap|Back; MapSelect → Confirm|Back;
                          Raid → UpdateRaid(); Summary → Continue → Stash }
Draw:   Raid ? pipeline.RenderFrame(raid.DrawScene, raid.ActiveLights, …, overlay: HUD + inventory screen)
             : Clear + one SpriteBatch (PointClamp!) → screen.Draw(sb, profile, mouse, w, h, time)
```
Screens return an `Action` enum from `Update(input, …)` and draw themselves; they never mutate game state directly
except the profile (stash ⇄ loadout moves, selected map). Keep `IsMouseVisible = true` outside raids.

## Profile (persistent)
```csharp
class Profile { Inventory Stash(48); Loadout Loadout { List<ItemType> Weapons(≤3); Inventory Bag(15) }; Stats; int Gold; string SelectedMapId;
  MoveToLoadout(stashSlot) / MoveBagToStash(i) / MoveWeaponToStash(i) / StashAll()
  EnsureMinimumLoadout()   // free pistol + mag if no gun → a raid is always possible
  ReturnFromRaid(weapons, bag, gold, kills)  // extracted: carried gear → loadout; stats++
  LoseRaid(kills)                            // killed / MIA: loadout cleared, stash untouched
  Save()/Load()  // System.Text.Json DTO with (Type string, Count) per slot → %LocalAppData%/<Game>/profile.json
}
```
Items are stored by `ItemType.ToString()` so renames/removals degrade gracefully (`Enum.TryParse`). A fresh profile gets a
starter kit. Save after every raid, on leaving the hideout and in `OnExiting`; `GAME1_NOSAVE=1` for tests.

## MapDef (data) → GameWorld.Generate(map, spawn)
```csharp
record MapDef(Id, Name, Difficulty, Description, Seed, Size, PropCount, LampGrid, ColdLampChance, StartEnemies, MaxAlive,
              GunnerChance, SpawnMinDistance, Vector3 Ambient, float RaidMinutes, ExtractDef[] Extracts, Dictionary<PropKind,float> PropWeights)
record ExtractDef(Name, RelX, RelY, Width=160, Height=160)   // relative → works for any Size
```
`GameWorld.Size` is an instance value (never a const — everything that clamps/culls reads `world.Size`). Generation:
extracts first (kept clear of props with a 60 px margin), lamp grid, weighted props, barrier pairing. `MapPreview.Get(map)`
generates the world once and caches relative rectangles for the 120 px map-card minimap.

## Raid session (`Meta/Raid.cs`)
Owns World, Lights, Projectiles, Pickups, Player (built from the Loadout, `RespawnEnabled=false`), EnemyManager
(MaxAlive/GunnerChance/SpawnMinDistance from the map), Camera, GameContext, environment FX (lamps/barrels/haze) and the
extraction state. Shared assets (`RaidAssets`: floor, props, icons, particle system, device) are passed in.
```csharp
Update(dt, input, viewportSize, paused):
  CurrentZone = World.ExtractAt(player.Position);
  ExtractProgress = zone != null ? min(1, p + dt/zone.HoldSeconds) : max(0, p - dt*0.6);     // decays when you step out
  Outcome = progress >= 1 ? Extracted : !alive && deadFor > 3 s ? Killed : TimeLeft <= 0 ? TimedOut : None;
```
The host reacts to `raid.Outcome != None` → `EndRaid()`: fill the summary, `profile.ReturnFromRaid/LoseRaid`, save, drop
the raid object. Esc during a raid = abandon = MIA (loses gear) — cheap anti-softlock.

## Extraction presentation
- Zone marker: `PropArt.CreateExtractMarker(w,h)` (worn painted frame, hazard ticks, X, flare canister) drawn under props.
- Green pulsing point light at the centre + green smoke particles (10/s) so it reads from afar.
- HUD: raid timer top-centre (turns amber < 3 min, red < 1 min), compass arrow + "NAME 161M" to the nearest extract,
  "IN EXTRACT: NAME" when inside, hold bar with percentage, "EXTRACTION INTERRUPTED" while it decays.

## Headless verification knobs (host)
`GAME1_STATE=menu|stash|map|raid|summary` (raid auto-builds a loadout from the stash), `GAME1_MAP=<id>`,
`GAME1_SPAWN_AT_EXTRACT=1` (spawn in extract 0 → the whole extract → summary → profile flow runs in ~6 s),
`GAME1_NOSAVE=1`. Capture with `GAME1_SCREENSHOT` at delays 1.5 s (screens) / 3 s (extracting) / 8 s (summary).

## Gotchas
- Draw menu screens with `SamplerState.PointClamp`; a wrap/linear sampler smears the 5×7 pixel font. Tile the backdrop by
  hand instead of relying on wrap.
- Long button labels overflow at scale 2: measure or shorten (`WeaponDef.ShortName`, "DEPLOY [ENTER]" + a hint line).
- `string.Format` custom TimeSpan formats with `\\:` are painful in interpolation — format minutes/seconds manually.
- When the player is at a map corner (extract), the camera clamps — expected; don't "fix" it by letting the view leave the floor.
