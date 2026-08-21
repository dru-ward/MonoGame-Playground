---
name: monogame-game-architecture
description: Structure a MonoGame game beyond a single Game1.cs — namespaced folders (Core/Graphics/World/Entities/Combat/Items/UI), a thin Game host with a fixed update order and clamped dt, sealed-record data definitions with static presets separated from mutable instances, a GameContext bag of shared systems passed to Update, events for cross-system reactions, a RenderPipeline that takes draw callbacks, a SceneBatch abstraction for two-pass G-buffer drawing, a Character base with independent aim/move facing, headless test knobs, and tips for migrating a monolith file-by-file. Use when a MonoGame prototype outgrows one file or when adding new gameplay systems to an existing layered project.
---

# MonoGame game architecture (layered layout)

```
<Game>.cs           Game subclass: creates systems in LoadContent, fixed Update order, Draw = pipeline callbacks
Core/               Camera2D, InputState (edge detection), MathUtil (Damp/LerpAngle/Approach/Reflect), Rng
Graphics/           RenderPipeline, GraphicsStates, SceneBatch, ParticleSystem, LightManager, TextureFactory,
                    ShapeSprite, CharacterArt, PixelFont
World/              GameWorld (level data + spatial queries), Collision (pure geometry)
Entities/           Character (base), Player, Enemy + EnemyDef, EnemyManager, GameContext
Combat/             WeaponDef/Weapon, ProjectileSystem
Items/              ItemDef/ItemStack, Inventory, LootTable, PickupManager, ItemArt
UI/                 Hud
```
Rules that keep it manageable:
- **Data records for tuning** (`WeaponDef`, `EnemyDef`, `ItemDef`, `CharacterStyle`) are `sealed record`s with static
  presets (`WeaponDef.Rifle`, `EnemyDef.Gunner`) — instances hold only mutable state (`Weapon`, `Enemy`).
- **`GameContext`** is a `required init` bag (World, Particles, Lights, Projectiles, Pickups, Camera, Input, Player,
  Enemies, Score/Time). Entities get `Update(dt, ctx)`; no long constructor chains, no service locator.
- **Events over references** for cross-system reactions: `Character.Died`, `Inventory.ItemAdded`,
  `ProjectileSystem.ObstacleBroken` — the host subscribes and spawns loot/particles.
- **`csproj`**: set `<RootNamespace>` to the game's namespace, `<Nullable>enable</Nullable>`; `Program.cs` is
  `using var game = new MyGame(); game.Run();`
- If a meta layer (menus, persistent profile, per-session setup) is added later, make the host a state machine and move
  the in-session systems into a separate object created per session that returns an outcome.

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
normal pass, flushes rotated sprites through a normal-rotating pixel shader. Tints apply to the albedo pass only.
(See monogame-deferred-2d-lighting for the pipeline itself.)

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
Useful set: `<PREFIX>_SCREENSHOT` (save + exit), `<PREFIX>_SHOT_DELAY`, `<PREFIX>_VIEW` (debug buffer to blit),
`<PREFIX>_ZOOM`, `<PREFIX>_BOT=1` (player auto-aims and fires at the nearest enemy; enemies aggro immediately). Run e.g.
`X_BOT=1 X_ZOOM=0.75 X_SHOT_DELAY=7 X_SCREENSHOT=out.png timeout 60 dotnet bin/Debug/net9.0/Game.dll` then read the PNG
(crop/upscale with PIL to inspect sprites). This is how AI state flip-flops and a missing auto-reload were found without a
visible desktop. See monogame-headless-screenshots.

## Migration tips (from a monolith)
- Move code file-by-file with its `using`s; build after each layer (Core → Graphics → World → Items → Combat → Entities → UI → host).
- `out` params can't be captured by local functions — copy to locals, assign back at the end (typical in segment-cast helpers).
- Delete the old file last; keep a backup copy outside the project.
