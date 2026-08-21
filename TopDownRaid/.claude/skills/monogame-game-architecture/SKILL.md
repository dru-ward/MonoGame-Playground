---
name: monogame-game-architecture
description: Structure a MonoGame game beyond a single Game1.cs — namespaced folders (Core/Graphics/World/Entities/Combat/Items/UI), a thin Game host, a GameContext bag of shared systems, a RenderPipeline that takes draw callbacks, a SceneBatch abstraction for two-pass G-buffer drawing, a Character base with independent aim/move facing, and headless test knobs. Use when a MonoGame prototype outgrows one file or when adding new gameplay systems to this project.
---

# MonoGame game architecture (this project's layout)

```
TopDownGame.cs      Game subclass: creates systems in LoadContent, fixed Update order, Draw = pipeline callbacks
Core/               Camera2D, InputState (edge detection), MathUtil (Damp/LerpAngle/Approach/Reflect), Rng
Graphics/           RenderPipeline, GraphicsStates, SceneBatch, ParticleSystem, LightManager, TextureFactory,
                    ShapeSprite, CharacterArt, PixelFont
World/              GameWorld (level data + spatial queries), Collision (pure geometry)
Entities/           Character (base), Player, Enemy + EnemyDef, EnemyManager, GameContext
Combat/             WeaponDef/Weapon, ProjectileSystem
Items/              ItemDef/ItemStack, Inventory, LootTable, PickupManager, ItemArt
UI/                 Hud
```
Rules that kept it manageable:
- **Data records for tuning** (`WeaponDef`, `EnemyDef`, `ItemDef`, `CharacterStyle`) are `sealed record`s with static
  presets (`WeaponDef.Rifle`, `EnemyDef.Gunner`) — instances hold only mutable state (`Weapon`, `Enemy`).
- **`GameContext`** is a `required init` bag (World, Particles, Lights, Projectiles, Pickups, Camera, Input, Player,
  Enemies, Score/Time). Entities get `Update(dt, ctx)`; no long constructor chains, no service locator.
- **Events over references** for cross-system reactions: `Character.Died`, `Inventory.ItemAdded`,
  `ProjectileSystem.CrateBroken` — the host subscribes and spawns loot/particles.
- **`csproj`**: `<RootNamespace>TopDown</RootNamespace>`, `<Nullable>enable</Nullable>`; `Program.cs` = `using var game = new TopDown.TopDownGame(); game.Run();`

## Meta layer (added later)
The host became a state machine (Menu/Stash/MapSelect/Raid/Summary) and the in-raid systems moved into `Meta/Raid`
(created per deployment from `Profile.Loadout`, returns an outcome). See monogame-raid-metagame.

## Update order (host)
```csharp
_input.Update(IsActive, vp.Bounds); HandleDebugKeys();
_player.Update(dt, _ctx);                       // input -> movement, aim, fire (spawns bullets), loot, hotbar
_enemies.Update(dt, _ctx);                      // AI, spawning, separation
_projectiles.Update(dt, _enemies.HittableCharacters(_player));   // player + alive enemies
_pickups.Update(dt, _player.Position, _player.IsAlive, _player.Collect, _world);
_lights.Update(dt); _particles.Update(dt);
_camera.Follow(target, dt, viewportSize, GameWorld.Size);
```
Clamp `dt` (`MathF.Min(dt, 1/20f)`) so window drags/breakpoints don't launch bullets through walls.

## Render host: pipeline with callbacks
```csharp
_particles.BeginFrameVertices(); _projectiles.DrawTracers();          // transient quads for this frame
var visible = _camera.VisibleWorld;
var lights = _lights.GetActive(visible.Min - pad, visible.Max + pad);   // culled + strongest first
_pipeline.RenderFrame(_camera.View, _camera.Zoom,
    drawScene:    batch => DrawScene(batch, visible),   // called TWICE (albedo pass, normal pass)
    lights:       lights,
    drawEmissive: p => _particles.Draw(p.CurrentView, p.Width, p.Height, p.States.AdditiveLight, raster),
    drawOverlay:  sb => _hud.Draw(sb, _ctx, ...));      // unlit, after post-processing
```
`SceneBatch` hides the two-pass detail: `DrawTiled(pair, rect)`, `DrawRect(pair, rect, tint)`, `Draw(pair, pos, scale)`,
`DrawRotated(pair, pos, rot, scale, tint, rotateNormals)`. It picks `.Albedo` or `.Normal` from a `SpritePair` and, in the
normal pass, flushes rotated sprites through the `SpriteNormalRotate` pixel shader. Tints apply to the albedo pass only.

## Character base (aim ≠ movement)
```csharp
public abstract class Character {
  public Vector2 Position, Velocity; public float Facing /*aim*/, MoveFacing /*feet*/, Radius = 24, Health, MaxHealth;
  public Faction Faction; public bool IsAlive => Health > 0; public float HitFlash, StridePhase, RecoilKick, DeadTimer;
  protected SpritePair Body, Boot;
  public event Action<float, Vector2>? Damaged; public event Action? Died;
  public virtual void TakeDamage(float amount, Vector2 hitDir, Vector2 hitPos) { ...; if (Health <= 0) { OnDeath(); Died?.Invoke(); } }
  protected void TickCommon(float dt) { HitFlash/RecoilKick decay; MoveFacing lerps to velocity; StridePhase += dt * Speed / 26f; }
  public virtual void Draw(SceneBatch b) { two boots (rotated to MoveFacing, ±side, ±stride) then body (rotated to Facing, recoil kick, tint) }
  public Vector2 LocalToWorld(Vector2 texels) => Position + MathUtil.Rotate(texels * SpriteScale, Facing);   // muzzle etc.
}
```

## Headless test knobs (env vars, read once)
`GAME1_SCREENSHOT` (save + exit), `GAME1_SHOT_DELAY`, `GAME1_VIEW`, `GAME1_ZOOM`, `GAME1_BOT=1` (player auto-aims and
fires at the nearest enemy; enemies aggro immediately). Run `GAME1_BOT=1 GAME1_ZOOM=0.75 GAME1_SHOT_DELAY=7 GAME1_SCREENSHOT=out.png timeout 60 dotnet bin/Debug/net9.0/Game1.dll`
then Read the PNG (crop/upscale with PIL to inspect sprites). This is how the aggro/lose-range flip-flop and the
missing auto-reload were found without a visible desktop.

## Migration tips (from a monolith)
- Move code file-by-file with its `using`s; build after each layer (Core → Graphics → World → Items → Combat → Entities → UI → host).
- `out` params can't be captured by local functions — copy to locals, assign back at the end (see `GameWorld.CastSegment`).
- Delete the old file last; keep a backup copy outside the project (`../game1_backup/Game1.cs`).
