---
name: monogame-raid-metagame
description: Session-based meta-game loop for MonoGame — a host state machine (Menu → Hub/stash+loadout → Level select → Session → Summary), a persistent Profile (stash inventory, loadout, currency, stats) saved as JSON by enum-name strings so renames degrade gracefully, data-driven level definitions (size, seed, spawn weights, lighting, enemy pressure, timer, relative exit positions) with cached layout previews, a Session class that owns every in-level system and yields an outcome (succeeded / killed / timed out), hold-to-complete exit zones with decaying progress, and environment-variable knobs to jump straight to any state for headless verification. Use when adding menus, a hub/stash, multiple levels, persistence or an enter/exit session loop to a MonoGame game.
---

# Session meta-game loop

## State machine (host)
```csharp
enum GameState { Menu, Hub, LevelSelect, Session, Summary }
Update: switch(_state) { Menu → _menu.Update() → Hub|Quit; Hub → Deploy|ChooseLevel|Back; LevelSelect → Confirm|Back;
                          Session → UpdateSession(); Summary → Continue → Hub }
Draw:   Session ? pipeline.RenderFrame(session.DrawScene, session.ActiveLights, …, overlay: HUD + inventory screen)
                : Clear + one SpriteBatch (PointClamp!) → screen.Draw(sb, profile, mouse, w, h, time)
```
Screens return an `Action` enum from `Update(input, …)` and draw themselves; they never mutate game state directly
except the profile (stash ⇄ loadout moves, selected level). Keep `IsMouseVisible = true` outside sessions.

## Profile (persistent)
```csharp
class Profile { Inventory Stash(48); Loadout Loadout { List<ItemType> Weapons(≤3); Inventory Bag(15) }; Stats; int Currency; string SelectedLevelId;
  MoveToLoadout(stashSlot) / MoveBagToStash(i) / MoveWeaponToStash(i) / StashAll()
  EnsureMinimumLoadout()   // free starter weapon + ammo if none → a session is always possible
  ReturnFromSession(weapons, bag, currency, kills)  // success: carried gear → loadout; stats++
  LoseSession(kills)                                // killed / abandoned: loadout cleared, stash untouched
  Save()/Load()  // System.Text.Json DTO with (Type string, Count) per slot → %LocalAppData%/<Game>/profile.json
}
```
Items are stored by `ItemType.ToString()` so renames/removals degrade gracefully (`Enum.TryParse`). A fresh profile gets a
starter kit. Save after every session, on leaving the hub and in `OnExiting`; provide a `<GAME>_NOSAVE=1` env var for tests.
(Sizes 48/15/3 are starting values.)

## LevelDef (data) → World.Generate(level, spawn)
```csharp
record LevelDef(Id, Name, Difficulty, Description, Seed, Size, PropCount, LampGrid, ColdLampChance, StartEnemies, MaxAlive,
              GunnerChance, SpawnMinDistance, Vector3 Ambient, float Minutes, ExitDef[] Exits, Dictionary<PropKind,float> PropWeights,
              FloorKind Floor = FloorKind.Default, bool Daylight = false)
record ExitDef(Name, RelX, RelY, Width=160, Height=160)   // relative → works for any Size
```
Optional record params with defaults are the cheap way to grow the record — old levels never change. `LampGrid: 0` simply
skips the lamp loop (daylight levels have no lamps). The session picks the floor sprite pair from shared assets by
`level.Floor`; the host sets `pipeline.Ambient` AND the colour grade when a session starts.
`World.Size` is an instance value (never a const — everything that clamps/culls reads `world.Size`). Generation order:
exits first (kept clear of props with a ~60 px margin), lamp grid, weighted props, barrier pairing. `LevelPreview.Get(level)`
generates the world once and caches relative rectangles for a small level-card minimap (120 px starting size).

## Session class
Owns World, Lights, Projectiles, Pickups, Player (built from the Loadout, `RespawnEnabled=false`), EnemyManager
(MaxAlive/GunnerChance/SpawnMinDistance from the level), Camera, GameContext, environment FX and the exit state. Shared
assets (floor, props, icons, particle system, device) are passed in, not re-created per session.
```csharp
Update(dt, input, viewportSize, paused):
  CurrentZone = World.ExitAt(player.Position);
  ExitProgress = zone != null ? min(1, p + dt/zone.HoldSeconds) : max(0, p - dt*0.6);     // decays when you step out
  Outcome = progress >= 1 ? Succeeded : !alive && deadFor > 3 s ? Killed : TimeLeft <= 0 ? TimedOut : None;
```
The host reacts to `session.Outcome != None` → `EndSession()`: fill the summary, `profile.ReturnFromSession/LoseSession`,
save, drop the session object. Esc during a session = abandon = loss — cheap anti-softlock.

## Exit presentation (generic)
- A zone marker sprite drawn under props, plus a pulsing point light and a particle emitter at the centre so it reads from afar.
- HUD: session timer top-centre with colour change near the end, compass arrow + distance to the nearest exit, an
  "inside zone" label, a hold bar with percentage, and an "interrupted" notice while progress decays.

## Headless verification knobs (host)
`<GAME>_STATE=menu|hub|level|session|summary` (session auto-builds a loadout from the stash), `<GAME>_LEVEL=<id>`,
`<GAME>_SPAWN_AT_EXIT=1` (spawn in exit 0 → the whole exit → summary → profile flow runs in ~6 s),
`<GAME>_NOSAVE=1`. Capture screenshots at delays of ~1.5 s (screens) / 3 s (exiting) / 8 s (summary)
(see monogame-headless-screenshots).

## Gotchas
- Draw menu screens with `SamplerState.PointClamp`; a wrap/linear sampler smears a small pixel font. Tile the backdrop by
  hand instead of relying on wrap.
- Long button labels overflow at scale 2: measure or shorten (a `ShortName` on defs, a key hint on a second line).
- `string.Format` custom TimeSpan formats with `\\:` are painful in interpolation — format minutes/seconds manually.
- When the player is at a level corner (exit), the camera clamps — expected; don't "fix" it by letting the view leave the floor.
