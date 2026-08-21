---
name: monogame-topdown-player
description: Add a controllable top-down character to a MonoGame game — WASD movement with acceleration/friction and frame-rate-independent damping, sprint, circle-vs-AABB collision with sliding and least-penetration ejection, twin-stick mouse aiming with separate aim/move facing and anti-phase foot stride, a trigger lockout so UI clicks never fire the weapon, muzzle position from a sprite-local offset with recoil, a smoothed look-ahead follow camera with wheel zoom clamped to the world, a shape-list sprite rasterised to matching albedo + Sobel normal map, and a SpriteBatch pixel shader that rotates normals with the sprite. Use when a MonoGame project needs a movable/aiming/shooting character and follow camera.
---

# Top-down player + follow camera

## Movement (per frame, `dt` seconds)
```csharp
sealed class Player { public const float Radius=24, WalkSpeed=330, SprintSpeed=560, Accel=2600, Friction=9;   // starting values
                      public Vector2 Position, Velocity; public float Facing, MoveFacing, StridePhase, BobTime; public bool IsSprinting; }

var k = Keyboard.GetState(); var input = Vector2.Zero;
if (k.IsKeyDown(Keys.W) || k.IsKeyDown(Keys.Up))    input.Y -= 1;   // screen y is down
if (k.IsKeyDown(Keys.S) || k.IsKeyDown(Keys.Down))  input.Y += 1;
if (k.IsKeyDown(Keys.A) || k.IsKeyDown(Keys.Left))  input.X -= 1;
if (k.IsKeyDown(Keys.D) || k.IsKeyDown(Keys.Right)) input.X += 1;
p.IsSprinting = k.IsKeyDown(Keys.LeftShift) || k.IsKeyDown(Keys.RightShift);

if (input != Vector2.Zero)
{
    input.Normalize();                                                    // no faster diagonals
    var wanted = input * (p.IsSprinting ? Player.SprintSpeed : Player.WalkSpeed);
    var delta = wanted - p.Velocity; float maxStep = Player.Accel * dt;   // move velocity toward wanted, capped
    if (delta.Length() > maxStep) delta = Vector2.Normalize(delta) * maxStep;
    p.Velocity += delta;
}
else { p.Velocity *= MathF.Exp(-Player.Friction * dt); if (p.Velocity.LengthSquared() < 4) p.Velocity = Vector2.Zero; }
p.Position += p.Velocity * dt;
ResolveCollisions();
if (p.Velocity.LengthSquared() > 100)                                    // only turn while actually moving
    p.MoveFacing = LerpAngle(p.MoveFacing, MathF.Atan2(p.Velocity.Y, p.Velocity.X), 1 - MathF.Exp(-14 * dt));

static float LerpAngle(float a, float b, float t) => a + MathHelper.WrapAngle(b - a) * t;
```
`1 - exp(-k*dt)` lerp factors and `exp(-friction*dt)` damping are frame-rate independent. The capped velocity step is a
generic `Approach(current, wanted, maxStep)` helper worth factoring out.

**Aim facing != move facing**: `Facing` follows the mouse (twin-stick, below), `MoveFacing` lerps to the velocity. The
body is drawn rotated to `Facing`; feet are drawn under it rotated to `MoveFacing`, offset +/-9 px sideways and sliding
+/-7 px fore/aft in anti-phase with `StridePhase += dt * Speed / 26` (one stride per 26 px of travel). If feet use a
flat/tiny-relief normal map they don't need the normal-rotation shader and cost no batch flush.

## Circle vs AABB collision with sliding
```csharp
for (int iter = 0; iter < 2; iter++)                     // 2 passes settle corners between adjacent boxes
foreach (var r in boxes)
{
    var closest = new Vector2(MathHelper.Clamp(p.Position.X, r.Left, r.Right), MathHelper.Clamp(p.Position.Y, r.Top, r.Bottom));
    var diff = p.Position - closest; float d2 = diff.LengthSquared();
    if (d2 >= Player.Radius * Player.Radius) continue;
    if (d2 > 1e-6f)
    {
        float d = MathF.Sqrt(d2); var n = diff / d;
        p.Position += n * (Player.Radius - d);
        float into = Vector2.Dot(p.Velocity, n); if (into < 0) p.Velocity -= n * into;   // slide along the wall
    }
    else   // centre inside the box: eject along the axis of least penetration
    {
        float dl = p.Position.X - r.Left, dr = r.Right - p.Position.X, dt_ = p.Position.Y - r.Top, db = r.Bottom - p.Position.Y;
        float m = MathF.Min(MathF.Min(dl, dr), MathF.Min(dt_, db));
        if (m == dl) p.Position.X = r.Left - Player.Radius; else if (m == dr) p.Position.X = r.Right + Player.Radius;
        else if (m == dt_) p.Position.Y = r.Top - Player.Radius; else p.Position.Y = r.Bottom + Player.Radius;
    }
}
p.Position = Vector2.Clamp(p.Position, new Vector2(Player.Radius), new Vector2(WorldSize - Player.Radius));
```
Run `ResolveCollisions()` a few times at spawn so the player never starts inside a box. Expose it as
`world.ResolveCircle(ref pos, ref vel, r)` so enemies and other movers share it.

## Follow camera with look-ahead and wheel zoom
```csharp
int scroll = mouse.ScrollWheelValue - _prevScroll; _prevScroll = mouse.ScrollWheelValue;
if (scroll != 0) _targetZoom = MathHelper.Clamp(_targetZoom * MathF.Pow(1.15f, scroll / 120f), 0.5f, 2.5f);
_zoom = MathHelper.Lerp(_zoom, _targetZoom, 1 - MathF.Exp(-8 * dt));
var target = p.Position + p.Velocity * 0.25f;                                       // lead in travel direction
var cam = Vector2.Lerp(_cam, target, 1 - MathF.Exp(-5 * dt));
var half = new Vector2(vp.Width, vp.Height) / (2 * _zoom);
_cam = Vector2.Clamp(cam, half, new Vector2(WorldSize) - half);                     // never show outside the floor
_view = Matrix.CreateTranslation(-_cam.X, -_cam.Y, 0) * Matrix.CreateScale(_zoom, _zoom, 1) * Matrix.CreateTranslation(vp.Width/2f, vp.Height/2f, 0);
// screen -> world (e.g. mouse): Vector2.Transform(screenPos, Matrix.Invert(_view))
```

## Drawing (works with a normal-mapped G-buffer pipeline)
```csharp
float bob = 1 + 0.05f * MathF.Sin(p.BobTime * 2) * MathHelper.Clamp(p.Speed / Player.WalkSpeed, 0, 1);
float scale = Player.Radius * 2.3f / tex.Width * bob;
sb.Draw(normalPass ? playerNormal : playerAlbedo, p.Position, null, Color.White, p.Facing,
        new Vector2(tex.Width, tex.Height) * 0.5f, scale, SpriteEffects.None, 0);
```
- Sprite art points +X at rotation 0 (a readable "front" feature such as a visor wedge helps). `Facing = Atan2(vy, vx)`
  matches SpriteBatch's clockwise-on-screen rotation directly.
- Rotating a sprite breaks "tangent space == screen space" for normal maps **unless the normal map is radially symmetric**
  (a dome) — then rotation is free. Otherwise rotate n.xy in a shader (below).
- Attach a `PointLight { FollowPlayer = true }` (radius ~430, height ~110 as starting values) and emit dust particles behind
  the player at a rate proportional to speed (`-normalize(velocity)` offset, 0.5–1 s lifetime).

## Twin-stick aiming + firing
```csharp
// aim: body/gun face the cursor; movement stays on WASD
var aimWorld = Vector2.Transform(new Vector2(mouse.X, mouse.Y), Matrix.Invert(_view));
var toAim = aimWorld - p.Position;
if (toAim.LengthSquared() > 4) p.Facing = LerpAngle(p.Facing, MathF.Atan2(toAim.Y, toAim.X), 1 - MathF.Exp(-25 * dt));

// weapon timers (all decay per frame): FireCooldown, Recoil (0..1), Flash (0..1)
if (trigger && p.FireCooldown <= 0) FireWeapon();
// trigger lockout: arm while any UI is open (and at spawn), release only once LMB is UP — otherwise the click
// that closes an inventory/menu (or a "start" button) fires the gun on the very next frame:
// if (uiOpen) locked = true; else if (!LeftDown) locked = false;  trigger = LeftDown && !locked && !uiOpen;

// muzzle in world space from a sprite-local offset (+X forward, +Y right), includes recoil pull-back
public Vector2 MuzzleWorld(float scale) { float c = Cos(Facing), s = Sin(Facing);
    var l = new Vector2(MuzzleOffset - Recoil * 6, MuzzleSide) * scale; return Position + new Vector2(l.X*c - l.Y*s, l.X*s + l.Y*c); }
```
Magazine/reload/semi-auto state, and proper segment-cast bullets with ricochet, are covered in monogame-projectiles-ricochet;
the ammo reserve is simply the inventory. A minimal alternative is bullets as particles with `IsBullet = true, Aspect = 5`
(elongated quad along Rotation), speed ~1500 px/s, colour > 1 so they bloom, sub-stepped so they never tunnel:
```csharp
var step = p.Velocity * dt; int n = 1 + (int)(step.Length() / 12f); var sub = step / n;
for (int k = 0; k < n; k++) { p.Position += sub; if (outOfWorld || anyBox.Contains(p.Position)) { SpawnImpact(p.Position - sub, p.Velocity); kill; break; } }
```
Muzzle flash = a `PointLight` whose `Intensity = p.Flash * 2.5` (Flash set to 1 on fire, `-= 14*dt`); recoil kicks the
sprite `-Facing * Recoil * 4px`. Sprint widens the random spread. Draw a screen-space crosshair with pixel rectangles
additively into the scene RT (unlit). Keep light count ≤ `MAX_LIGHTS` (scene lights + player light + muzzle).
Damage mitigation (armor absorbing a fraction) and a respawn timer that resets enemy aggro are cheap to add here.

## Character sprite from a shape list (albedo + normal map agree)
Describe the character once as ordered shapes (ellipse / capsule / box) with colour, base height and "dome" amount, then
rasterise twice: colour with painter's order and AA coverage; height field → Sobel normal map (`HeightToNormal`, strength
~0.35 on ~8px relief). Order: feet, torso ellipse (wide across Y), strap, shoulders, weapon parts, arms as capsules to grip
and fore-end, hands, head then hair cap offset backwards, face patch forwards.
`ShapeCoverage()` returns AA coverage and a normalised radial `t` (0 centre..1 edge) used for `sqrt(1-t²)` domes.
A layered rig (shadow/feet/torso/arms+weapon/head as separate sprites with arm-layer animation) is the natural next step.

## Rotating a detailed normal map with the sprite
A rotated sprite's normal map stays in texture space. Use a **pixel-shader-only** technique with SpriteBatch (it keeps its
own vertex shader) and rotate the decoded normal by (cos,sin) of the sprite rotation; input/output stay premultiplied:
```hlsl
float2 NormalRotation;   // (cos, sin)
float4 SpriteNormalRotatePS(float4 color : COLOR0, float2 uv : TEXCOORD0) : COLOR0
{
    float4 c = tex2D(AlbedoSampler, uv); float a = c.a;                 // s0 = sprite texture bound by SpriteBatch
    float3 n = (c.rgb / max(a, 1e-4)) * 2 - 1;
    float2 r = float2(n.x*NormalRotation.x - n.y*NormalRotation.y, n.x*NormalRotation.y + n.y*NormalRotation.x);
    return float4((normalize(float3(r, n.z)) * 0.5 + 0.5) * a, a) * color.a;
}
technique SpriteNormalRotate { pass P0 { PixelShader = compile PS_SHADERMODEL SpriteNormalRotatePS(); } }
```
```csharp
_effect.Parameters["NormalRotation"].SetValue(new Vector2(MathF.Cos(f), MathF.Sin(f)));
_effect.CurrentTechnique = _effect.Techniques["SpriteNormalRotate"];
sb.Begin(SpriteSortMode.Deferred, BlendState.AlphaBlend, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone, _effect, _view);
sb.Draw(playerNormal, pos, null, Color.White, f, origin, scale, SpriteEffects.None, 0); sb.End();
```
(Only needed in the normal pass; the albedo pass draws normally. Each parameter change forces a batch flush.)

## Input pitfalls
- Never bind debug toggles to `W`/`S`/`A`/`D` — use F-keys; keep UI keys (Tab/E/Q/R/number row) off the movement cluster.
- Edge-detect toggles with a previous `KeyboardState` (`IsKeyDown(k) && !prev.IsKeyDown(k)`), read movement as held keys.
- Disable/skip a mouse-driven light when `!IsActive` or the cursor is outside `Viewport.Bounds`.
