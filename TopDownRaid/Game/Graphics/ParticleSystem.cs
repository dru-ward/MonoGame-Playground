using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Core;

namespace Game.Graphics;

/// <summary>CPU-side particle state; expanded to 4 vertices per frame.</summary>
public struct Particle
{
    public Vector2 Position, Velocity;
    public float   Age, Lifetime, Size, Rotation, Spin;
    public Vector3 Color;
    public float   Aspect;        // quad length multiplier along Rotation (0/1 = square, >1 = streak)
    public float   Drag;          // per-second velocity damping (0.9 = default puffs)
    public float   Gravity;       // screen-space +Y acceleration (negative = rises)
    public bool    Emissive;      // if false the particle is dim "dust" (still additive, just darker)
}

/// <summary>
/// Pooled particle system streamed to the GPU through a DynamicVertexBuffer of VertexPositionColorTexture with a
/// static IndexBuffer; one DrawIndexedPrimitives per frame. Also accepts immediate-mode quads (bullet tracers)
/// via <see cref="AddQuad"/> that live for a single frame.
/// </summary>
public sealed class ParticleSystem : IDisposable
{
    public const int MaxParticles = 6144;                       // ×4 verts = 24576 < 65536 (16-bit indices)
    private const int MaxQuads = MaxParticles + 512;            // particles + transient quads

    private readonly Particle[] _particles = new Particle[MaxParticles];
    private int _live;
    private readonly VertexPositionColorTexture[] _verts = new VertexPositionColorTexture[MaxQuads * 4];
    private int _quadCount;                                     // quads written this frame (particles + transient)
    private readonly DynamicVertexBuffer _vb;
    private readonly IndexBuffer _ib;
    private readonly BasicEffect _effect;
    private readonly GraphicsDevice _gd;

    public int LiveCount => _live;
    public int Capacity => MaxParticles;

    public ParticleSystem(GraphicsDevice gd, Texture2D particleTexture)
    {
        _gd = gd;
        _vb = new DynamicVertexBuffer(gd, VertexPositionColorTexture.VertexDeclaration, MaxQuads * 4, BufferUsage.WriteOnly);
        var indices = new ushort[MaxQuads * 6];
        for (int i = 0; i < MaxQuads; i++)
        {
            int v = i * 4, n = i * 6;
            indices[n + 0] = (ushort)(v + 0); indices[n + 1] = (ushort)(v + 1); indices[n + 2] = (ushort)(v + 2);
            indices[n + 3] = (ushort)(v + 0); indices[n + 4] = (ushort)(v + 2); indices[n + 5] = (ushort)(v + 3);
        }
        _ib = new IndexBuffer(gd, IndexElementSize.SixteenBits, indices.Length, BufferUsage.WriteOnly);
        _ib.SetData(indices);
        _effect = new BasicEffect(gd) { TextureEnabled = true, VertexColorEnabled = true, LightingEnabled = false, Texture = particleTexture };
    }

    // ------------------------------------------------------------------------------------------------ emission
    public void Emit(in Particle p) { if (_live < MaxParticles) _particles[_live++] = p; }

    /// <summary>Directional spark burst (impacts, muzzle, ricochets).</summary>
    public void Sparks(Vector2 pos, Vector2 dir, int count, Vector3 color, float speed = 300f, float spread = 1.2f, float life = 0.3f)
    {
        float baseAng = MathUtil.ToAngle(dir);
        for (int i = 0; i < count; i++)
        {
            float a = baseAng + Rng.Signed(spread);
            Emit(new Particle
            {
                Position = pos, Velocity = MathUtil.FromAngle(a) * (speed * (0.5f + Rng.Float())),
                Lifetime = life * (0.5f + Rng.Float()), Size = 3f + Rng.Float() * 4f, Aspect = 3f, Rotation = a,
                Color = color, Drag = 2.5f, Gravity = 0f, Emissive = true,
            });
        }
    }

    /// <summary>Soft dust / smoke puff (dim, slow, expands via size ramp).</summary>
    public void Puff(Vector2 pos, Vector2 dir, int count, Vector3 color, float speed = 50f, float size = 12f, float life = 0.7f)
    {
        float baseAng = MathUtil.ToAngle(dir);
        for (int i = 0; i < count; i++)
        {
            float a = baseAng + Rng.Signed(1.6f);
            Emit(new Particle
            {
                Position = pos, Velocity = MathUtil.FromAngle(a) * (speed * (0.4f + Rng.Float())),
                Lifetime = life * (0.6f + Rng.Float() * 0.8f), Size = size * (0.7f + Rng.Float() * 0.6f), Aspect = 1f,
                Rotation = a, Spin = Rng.Signed(3f), Color = color, Drag = 1.5f, Gravity = -20f, Emissive = false,
            });
        }
    }

    /// <summary>Glowing embers rising from a light source.</summary>
    public void Ember(Vector2 pos, Vector3 color)
    {
        float a = Rng.Angle(); float spd = 40f + Rng.Float() * 90f;
        Emit(new Particle
        {
            Position = pos + MathUtil.FromAngle(a) * Rng.Float() * 12f, Velocity = MathUtil.FromAngle(a) * spd + new Vector2(0, -60f),
            Lifetime = 1.6f * (0.6f + Rng.Float() * 0.8f), Size = 10f + Rng.Float() * 18f, Aspect = 1f, Rotation = a,
            Spin = Rng.Signed(2f), Color = color, Drag = 0.9f, Gravity = -25f, Emissive = true,
        });
    }

    /// <summary>Immediate-mode quad for this frame only (used for bullet tracers). World space.</summary>
    public void AddQuad(Vector2 pos, float rotation, float size, float aspect, Vector3 color)
    {
        if (_quadCount >= MaxQuads) return;
        WriteQuad(_quadCount++, pos, rotation, size, aspect, new Color(color.X, color.Y, color.Z, 1f));
    }

    // ------------------------------------------------------------------------------------------------ simulation
    public void Update(float dt)
    {
        for (int i = 0; i < _live;)
        {
            ref var p = ref _particles[i];
            p.Age += dt;
            if (p.Age >= p.Lifetime) { _particles[i] = _particles[--_live]; continue; }   // swap-remove keeps it dense
            p.Velocity *= MathF.Max(0f, 1f - p.Drag * dt);
            p.Velocity.Y += p.Gravity * dt;
            p.Position += p.Velocity * dt;
            p.Rotation += p.Spin * dt;
            i++;
        }
    }

    /// <summary>Call once per frame BEFORE any AddQuad: converts live particles into vertices (world space).</summary>
    public void BeginFrameVertices()
    {
        _quadCount = 0;
        for (int i = 0; i < _live; i++)
        {
            ref var p = ref _particles[i];
            float life = p.Age / p.Lifetime;
            float fade = MathF.Sin(life * MathF.PI);                        // 0 -> 1 -> 0
            float size = p.Emissive ? p.Size * (0.5f + 0.5f * (1f - life)) : p.Size * (0.6f + 0.8f * life);
            float bright = p.Emissive ? fade : fade * 0.55f;
            var c = new Color(p.Color.X * bright, p.Color.Y * bright, p.Color.Z * bright, bright);   // premultiplied
            WriteQuad(_quadCount++, p.Position, p.Rotation, size, MathF.Max(p.Aspect, 1f), c);
        }
    }

    private void WriteQuad(int index, Vector2 pos, float rotation, float size, float aspect, Color c)
    {
        float cs = MathF.Cos(rotation) * size, sn = MathF.Sin(rotation) * size;
        var right = new Vector2(cs, sn) * aspect;
        var up    = new Vector2(-sn, cs);
        int v = index * 4;
        _verts[v + 0] = new VertexPositionColorTexture(new Vector3(pos - right - up, 0f), c, new Vector2(0, 0));
        _verts[v + 1] = new VertexPositionColorTexture(new Vector3(pos + right - up, 0f), c, new Vector2(1, 0));
        _verts[v + 2] = new VertexPositionColorTexture(new Vector3(pos + right + up, 0f), c, new Vector2(1, 1));
        _verts[v + 3] = new VertexPositionColorTexture(new Vector3(pos - right + up, 0f), c, new Vector2(0, 1));
    }

    // ------------------------------------------------------------------------------------------------ drawing
    /// <summary>Uploads this frame's quads (Discard = no GPU stall) and draws them additively with the camera view.</summary>
    public void Draw(Matrix view, int targetW, int targetH, BlendState blend, RasterizerState rasterizer)
    {
        if (_quadCount == 0) return;
        _vb.SetData(_verts, 0, _quadCount * 4, SetDataOptions.Discard);

        _gd.BlendState = blend;
        _gd.DepthStencilState = DepthStencilState.None;
        _gd.RasterizerState = rasterizer;
        _gd.SamplerStates[0] = SamplerState.LinearClamp;
        _effect.World = Matrix.Identity;
        _effect.View = view;
        _effect.Projection = Matrix.CreateOrthographicOffCenter(0, targetW, targetH, 0, 0, 1);   // pixel space, y down
        _gd.SetVertexBuffer(_vb);
        _gd.Indices = _ib;
        foreach (var pass in _effect.CurrentTechnique.Passes)
        {
            pass.Apply();
            _gd.DrawIndexedPrimitives(PrimitiveType.TriangleList, 0, 0, _quadCount * 2);
        }
        _gd.SetVertexBuffer(null);
        _gd.Indices = null;
    }

    public void Dispose() { _vb.Dispose(); _ib.Dispose(); _effect.Dispose(); }
}
