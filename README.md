# Games

Small game projects, one folder per game.

| Game | What it is |
|---|---|
| [TopDownRaid](TopDownRaid/) | Top-down MonoGame extraction shooter — deferred 2D lighting, raids, loot, stash ([README](TopDownRaid/README.md)) |
| [CharacterModels](CharacterModels/) | Procedural rigged, skinned, animated 3D characters in MonoGame — no assets ([README](CharacterModels/README.md)) |

## Shared skills (`.claude/skills/`)

Game-agnostic methods distilled from the projects above:

| Skill | Covers |
|---|---|
| `monogame-procedural-skinned-mesh` | Custom skinned vertex type, axis-aligned bind skeleton + palette, loft/ellipsoid/box primitives, winding/normals, automatic bone weighting, sockets via bind rotations, OBJ export |
| `monogame-skinning-shader` | HLSL palette skinning, wrap/hemisphere/Blinn-Phong/rim lighting from per-vertex materials, PCF shadow map, grain, tone mapping, DesktopGL pipeline gotchas |
| `monogame-procedural-animation` | Pose/clip model, gait tables for walk/run, Catmull-Rom keys, spring follow-through, two-bone arm IK, action state machine, weapon draw/sheathe, third-person control |
| `monogame-headless-screenshots` | Deterministic offscreen captures via startup options for agent-driven visual iteration |
