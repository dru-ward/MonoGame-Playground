using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Core;
using Game.Entities;
using Game.Graphics;
using Game.World;

namespace Game.Combat;

/// <summary>A thrown frag: slides with friction, bounces off props/walls, explodes when the fuse runs out.</summary>
public sealed class Grenade
{
    public Vector2 Position, Velocity;
    public float Fuse;
    public float Spin;
    public float Rotation;
    public Faction Owner;
}

/// <summary>
/// Throwables: friction slide + bounce (segment cast + reflect), fuse, then a radial blast that damages every
/// character (falloff by distance), breaks lootable crates, alerts enemies and lights up the area.
/// </summary>
public sealed class GrenadeSystem
{
    public const float BlastRadius = 150f, BlastDamage = 110f, FuseSeconds = 2.5f;
    private readonly List<Grenade> _live = new();
    private readonly GameWorld _world;
    private readonly ParticleSystem _particles;
    private readonly LightManager _lights;
    private readonly SpritePair _sprite;
    public event Action<Crate>? CrateBroken;
    public event Action<Vector2>? Exploded;
    public IReadOnlyList<Grenade> Live => _live;

    public GrenadeSystem(GameWorld world, ParticleSystem particles, LightManager lights, SpritePair sprite)
    { _world = world; _particles = particles; _lights = lights; _sprite = sprite; }

    public void Throw(Vector2 from, Vector2 velocity, Faction owner)
        => _live.Add(new Grenade { Position = from, Velocity = velocity, Fuse = FuseSeconds, Owner = owner, Spin = Rng.Signed(12f), Rotation = Rng.Angle() });

    public void Update(float dt, IReadOnlyList<Character> characters, EnemyManager enemies)
    {
        for (int i = _live.Count - 1; i >= 0; i--)
        {
            var g = _live[i];
            g.Fuse -= dt;
            g.Rotation += g.Spin * dt;
            g.Velocity *= MathF.Exp(-2.2f * dt);                      // slide friction
            g.Spin *= MathF.Exp(-1.5f * dt);
            var start = g.Position; var end = start + g.Velocity * dt;
            if (g.Velocity.LengthSquared() > 1f && _world.CastSegment(start, end, out float t, out var n, out _))
            {
                var hit = start + (end - start) * t;
                g.Position = hit + n * 2f;
                g.Velocity = MathUtil.Reflect(g.Velocity, n) * 0.45f;   // bounce
                _particles.Sparks(hit, n, 3, new Vector3(0.8f, 0.8f, 0.7f), 120f, 1.5f, 0.2f);
            }
            else g.Position = end;
            // tiny fuse sparkle
            if (Rng.Chance(0.5f)) _particles.Emit(new Particle { Position = g.Position, Velocity = new Vector2(Rng.Signed(20f), -30f), Lifetime = 0.25f, Size = 3f, Aspect = 1f, Color = new Vector3(1.6f, 1.2f, 0.5f), Drag = 1f, Emissive = true });

            if (g.Fuse <= 0f) { Explode(g, characters, enemies); _live.RemoveAt(i); }
        }
    }

    private void Explode(Grenade g, IReadOnlyList<Character> characters, EnemyManager enemies)
    {
        var p = g.Position;
        foreach (var c in characters)
        {
            if (!c.IsAlive) continue;
            var d = c.Position - p; float dist = d.Length();
            if (dist > BlastRadius + c.Radius) continue;
            float k = 1f - MathHelper.Clamp((dist - c.Radius) / BlastRadius, 0f, 1f);
            float dmg = BlastDamage * (0.2f + 0.8f * k);
            c.TakeDamage(dmg, MathUtil.SafeNormalize(d), c.Position);
            c.Velocity += MathUtil.SafeNormalize(d) * 260f * k;
        }
        foreach (var c in _world.Crates)
        {
            if (!c.Lootable || c.Opened) continue;
            var closest = new Vector2(MathHelper.Clamp(p.X, c.Bounds.Left, c.Bounds.Right), MathHelper.Clamp(p.Y, c.Bounds.Top, c.Bounds.Bottom));
            if ((closest - p).Length() < BlastRadius * 0.8f) { c.Opened = true; c.HitFlash = 1f; CrateBroken?.Invoke(c); }
        }
        enemies.AlertNear(p, 900f);
        // FX: flash light, fireball sparks, smoke ring, dust
        _lights.Flash(p, new Vector3(1f, 0.8f, 0.5f), 520f, 4f, 0.35f, 60f);
        _particles.Sparks(p, Vector2.UnitX, 40, new Vector3(1.8f, 1.2f, 0.5f), 520f, MathHelper.Pi, 0.5f);
        for (int i = 0; i < 26; i++)
        {
            var dir = MathUtil.FromAngle(i / 26f * MathHelper.TwoPi);
            _particles.Emit(new Particle { Position = p + dir * 10f, Velocity = dir * (180f + Rng.Float() * 120f), Lifetime = 0.9f + Rng.Float() * 0.6f, Size = 20f + Rng.Float() * 14f, Aspect = 1f,
                Rotation = Rng.Angle(), Spin = Rng.Signed(2f), Color = new Vector3(0.32f, 0.30f, 0.27f), Drag = 2.5f, Gravity = -20f, Emissive = false });
            _particles.Emit(new Particle { Position = p, Velocity = dir * (260f + Rng.Float() * 200f), Lifetime = 0.3f + Rng.Float() * 0.2f, Size = 9f + Rng.Float() * 8f, Aspect = 1.2f,
                Rotation = MathUtil.ToAngle(dir), Color = new Vector3(1.7f, 0.9f, 0.35f), Drag = 3f, Emissive = true });
        }
        Exploded?.Invoke(p);
    }

    public void Draw(SceneBatch batch)
    {
        foreach (var g in _live) batch.DrawRotated(_sprite, g.Position, g.Rotation, 0.7f, Color.White, rotateNormals: false);
    }
}
