# Games

Small game projects, one folder per game.

| Game | What it is |
|---|---|
| [TopDownRaid](TopDownRaid/) | Top-down MonoGame extraction shooter — deferred 2D lighting, raids, loot, stash ([README](TopDownRaid/README.md)) |
| [CharacterModels](CharacterModels/) | Procedural rigged, skinned, animated 3D characters in MonoGame — no assets ([README](CharacterModels/README.md)) |

## Shared skills (`.claude/skills/`)

Game-agnostic methods distilled from the projects above (style, palette and balance specifics stay with each game —
TopDownRaid keeps short stubs under `TopDownRaid/.claude/skills/` that point back here).

| Skill | Covers |
|---|---|
| `monogame-project-setup` | Scaffold/build a 3.8.x DesktopGL project, MGCB content, headless verification |
| `monogame-game-architecture` | Folder layout, records vs instances, context bag, update order, render callbacks |
| `monogame-hlsl-effects` | Writing and loading custom effects, DX/GL profile guards, parameter gotchas |
| `monogame-procedural-textures` | Runtime textures: tiling noise, Sobel normal maps, mips, premultiplied sprites |
| `monogame-deferred-2d-lighting` | Albedo+normal G-buffer, normal-mapped point lights, bloom, composite |
| `monogame-grunge-visuals` | Worn-surface height fields, grime multipliers, flicker lights, colour-grade post |
| `monogame-outdoor-daylight-map` | Bright maps on an ambient-multiplied pipeline, grass tiles, canopy props |
| `monogame-gpu-particles` | Pooled particle simulation and quad building |
| `monogame-hud-pixel-font` | Procedural pixel font, HUD layout and state handling |
| `monogame-character-rig` | Layered top-down sprite rig with gait and arm-layer animation recipes |
| `monogame-topdown-player` | Movement, circle-vs-AABB sliding, twin-stick aim, follow camera |
| `monogame-projectiles-ricochet` | Weapon state machine, segment-cast bullets, ricochet, tracers |
| `monogame-enemy-ai` | Data-driven enemies, state machine, kiting, sidestep steering, spawning |
| `monogame-weapon-attachments-gear` | Attachment/gear records, spot-cone torch, laser, grenades, melee |
| `monogame-inventory-loot` | Item registry, inventory API, loot tables, pickups |
| `monogame-inventory-screen` | Drag/drop inventory and container-transfer UI with reliable hit-testing |
| `monogame-raid-metagame` | Host state machine, persistent profile, level records, session outcomes |
| `monogame-procedural-skinned-mesh` | Code-built skinned meshes: vertex type, skeleton, primitives, auto-weighting, OBJ export |
| `monogame-skinning-shader` | HLSL palette skinning, stylised lighting, PCF shadows, tone mapping |
| `monogame-deferred-3d-lighting` | MRT G-buffer, depth reconstruction, sphere-volume point lights, half-float light buffer, composite |
| `monogame-procedural-trees-wind` | Trees as skinned rigs (six styles), bone sway + vertex-shader leaf flutter, gust model, planting |
| `monogame-procedural-animation` | Gait tables, Catmull-Rom keys, spring follow-through, 2-bone IK, weapon sockets |
| `monogame-zero-alloc-update-draw` | Measure per-frame allocations, the catalogue of hidden garbage in Update/Draw, pools, cached effect/state objects |
| `monogame-scripted-playtest` | Input-script timeline → synthetic KeyboardState, frame recorder + contact sheet, per-frame CSV, scenario library |
| `monogame-headless-screenshots` | Deterministic offscreen captures via startup options for visual iteration |
