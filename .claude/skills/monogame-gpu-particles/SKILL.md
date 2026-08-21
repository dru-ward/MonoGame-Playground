---
name: monogame-gpu-particles
description: High-throughput 2D particle system in MonoGame — a dense swap-remove pool of particle structs streamed each frame to a DynamicVertexBuffer of VertexPositionColorTexture (SetDataOptions.Discard) with a static 16-bit IndexBuffer and one DrawIndexedPrimitives call through BasicEffect with additive blend and a pixel-space orthographic projection matching SpriteBatch; includes emitter helpers (sparks, puffs, embers), transient per-frame quads for tracers, the premultiplied soft-circle texture, and the wireframe-toggle gotcha. Use when SpriteBatch particles are too slow, when particles must render into a post-processed scene target, or when demonstrating vertex buffers.
---

# DynamicVertexBuffer particle system

Layout: CPU pool of `Particle` structs (dense, swap-remove) → 4 `VertexPositionColorTexture` per particle written to a
`DynamicVertexBuffer` with `SetDataOptions.Discard` each frame → one indexed draw with a static `IndexBuffer`.

## Resources (LoadContent)
```csharp
const int MaxParticles = 4096;                       // ×4 verts = 16384 < 65536 ⇒ 16-bit indices OK
_vb = new DynamicVertexBuffer(gd, VertexPositionColorTexture.VertexDeclaration, MaxParticles * 4, BufferUsage.WriteOnly);
var idx = new ushort[MaxParticles * 6];
for (int i = 0; i < MaxParticles; i++) { int v = i*4, n = i*6;
    idx[n]=(ushort)v; idx[n+1]=(ushort)(v+1); idx[n+2]=(ushort)(v+2); idx[n+3]=(ushort)v; idx[n+4]=(ushort)(v+2); idx[n+5]=(ushort)(v+3); }
_ib = new IndexBuffer(gd, IndexElementSize.SixteenBits, idx.Length, BufferUsage.WriteOnly); _ib.SetData(idx);
_fx = new BasicEffect(gd) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false, Texture = softCircleTex };
```

## Simulation with dense compaction
```csharp
struct Particle { public Vector2 Position, Velocity; public float Age, Lifetime, Size, Rotation, Spin; public Vector3 Color; }
Particle[] _p = new Particle[MaxParticles]; int _live;

// emit with a per-emitter accumulator (rate * dt), cap at MaxParticles, reset accumulator if it banks up
// simulate:
for (int i = 0; i < _live;)
{
    ref var p = ref _p[i];
    p.Age += dt;
    if (p.Age >= p.Lifetime) { _p[i] = _p[--_live]; continue; }   // swap-with-last, no re-check needed for i
    p.Velocity *= 1f - 0.9f * dt; p.Velocity.Y -= 25f * dt; p.Position += p.Velocity * dt; p.Rotation += p.Spin * dt;
    i++;
}
```

## Vertex build (world space; camera applied on the GPU) + upload
```csharp
for (int i = 0; i < _live; i++)
{
    ref var p = ref _p[i];
    float life = p.Age / p.Lifetime, fade = MathF.Sin(life * MathF.PI), size = p.Size * (0.5f + 0.5f*(1-life));
    var c = new Color(p.Color.X*fade, p.Color.Y*fade, p.Color.Z*fade, fade);       // premultiplied for additive
    float cs = MathF.Cos(p.Rotation)*size, sn = MathF.Sin(p.Rotation)*size;
    var right = new Vector2(cs, sn); var up = new Vector2(-sn, cs); int v = i*4;
    _verts[v+0] = new(new Vector3(p.Position - right - up, 0), c, new Vector2(0,0));
    _verts[v+1] = new(new Vector3(p.Position + right - up, 0), c, new Vector2(1,0));
    _verts[v+2] = new(new Vector3(p.Position + right + up, 0), c, new Vector2(1,1));
    _verts[v+3] = new(new Vector3(p.Position - right + up, 0), c, new Vector2(0,1));
}
if (_live > 0) _vb.SetData(_verts, 0, _live * 4, SetDataOptions.Discard);   // Discard = driver renames buffer, no stall
```

## Draw (into the scene RT, additive so it blooms)
```csharp
gd.BlendState = _additive; gd.DepthStencilState = DepthStencilState.None;
gd.RasterizerState = wireframe ? _wire : RasterizerState.CullNone; gd.SamplerStates[0] = SamplerState.LinearClamp;
_fx.World = Matrix.Identity; _fx.View = cameraView;
_fx.Projection = Matrix.CreateOrthographicOffCenter(0, targetW, targetH, 0, 0, 1);   // pixel-space, y down (== SpriteBatch)
gd.SetVertexBuffer(_vb); gd.Indices = _ib;
foreach (var pass in _fx.CurrentTechnique.Passes) { pass.Apply(); gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _live * 2); }
gd.SetVertexBuffer(null); gd.Indices = null;
```

## Extended API (emitter helpers)
```csharp
struct Particle { Vector2 Position, Velocity; float Age, Lifetime, Size, Rotation, Spin; Vector3 Color; float Aspect, Drag, Gravity; bool Emissive; }
Emit(in Particle p);                                          // raw
Sparks(pos, dir, count, color, speed=300, spread=1.2, life=0.3);   // streaks (Aspect 3), Drag 2.5 - impacts, muzzle, ricochet
Puff(pos, dir, count, color, speed=50, size=12, life=0.7);         // dim dust/smoke, grows over life, rises (Gravity -20)
Ember(pos, color);                                             // rising glow from ambient lights
AddQuad(pos, rotation, size, aspect, color);                  // TRANSIENT quad for this frame only (bullet tracers)
Update(dt); BeginFrameVertices();  /* then AddQuad calls */  Draw(view, w, h, blendState, rasterizerState);
```
Emissive particles fade with `sin(life*pi)` and shrink; non-emissive ("dust") are drawn at 55 % brightness and grow.
Size the buffer at `MaxParticles + 512` quads so transient tracers never evict particles (starting values).

## Notes
- Particle texture: radial `a = smoothstep(1-d)²`, stored premultiplied `(a,a,a,a)`.
- Wire-frame mode on additive soft quads shows only the bright diagonal ⇒ looks like "streaks". If particles suddenly look
  like thin lines, a wireframe toggle key was probably pressed (don't bind toggles to WASD).
- Emitters attached to moving lights/characters: colour = light colour; dust should be dim and desaturated with a short
  lifetime, spawned behind the mover with `-normalize(velocity)`.
