using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Core;
using Game.Entities;
using Game.Graphics;
using Game.World;

namespace Game.Combat;

public enum Faction { Player, Enemy }

/// <summary>A bullet in flight. Rendered as an elongated emissive quad through the particle system.</summary>
public struct Bullet
{
    public Vector2 Position, Velocity;
    public float Life;                 // seconds remaining
    public float Damage;
    public int RicochetsLeft;
    public Faction Owner;
    public Vector3 Color;
    public float TrailTimer;
    public bool Alive;
}

/// <summary>
/// Simulates bullets with sub-stepped segment casts (no tunnelling), ricochets off crates and the world edge,
/// damages characters of the opposing faction and breaks lootable crates open. Bullets are drawn as tracers.
/// </summary>
public sealed class ProjectileSystem
{
    private const int MaxBullets = 512;
    private readonly Bullet[] _bullets = new Bullet[MaxBullets];
    private int _count;
    private readonly ParticleSystem _particles;
    private readonly LightManager _lights;
    private readonly GameWorld _world;

    public event Action<Crate>? CrateBroken;
    public int ActiveCount => _count;

    // ricochet tuning
    public float RicochetSpeedKeep = 0.72f;      // fraction of speed kept per bounce
    public float RicochetDamageKeep = 0.65f;
    public float RicochetJitter = 0.09f;         // radians of random deflection
    public float MinRicochetSpeed = 350f;        // slower than this: bullet just dies on impact
    public float GrazeAngleBoost = 0.35f;        // shallow hits ricochet more reliably (dot(n, -d) < this ⇒ always bounce)

    public ProjectileSystem(GameWorld world, ParticleSystem particles, LightManager lights)
    { _world = world; _particles = particles; _lights = lights; }

    public void Spawn(Vector2 pos, Vector2 velocity, float damage, int ricochets, Faction owner, Vector3 color, float life = 1.6f)
    {
        if (_count >= MaxBullets) return;
        _bullets[_count++] = new Bullet { Position = pos, Velocity = velocity, Life = life, Damage = damage, RicochetsLeft = ricochets, Owner = owner, Color = color, Alive = true };
    }

    /// <summary>Advances every bullet, resolving hits against the world and the given characters.</summary>
    public void Update(float dt, IReadOnlyList<Character> characters)
    {
        for (int i = 0; i < _count;)
        {
            ref var b = ref _bullets[i];
            b.Life -= dt;
            if (b.Life <= 0f) { _bullets[i] = _bullets[--_count]; continue; }

            var start = b.Position;
            var end = start + b.Velocity * dt;

            // ---- 1. nearest character hit along the segment (opposing faction, alive) --------------------
            Character? hitChar = null; float charT = float.MaxValue;
            foreach (var c in characters)
            {
                if (!c.IsAlive || c.Faction == b.Owner) continue;
                if (Collision.SegmentVsCircle(start, end, c.Position, c.Radius, out float t) && t < charT) { charT = t; hitChar = c; }
            }
            // ---- 2. nearest world hit ----------------------------------------------------------------------
            bool worldHit = _world.CastSegment(start, end, out float wt, out var normal, out var crate);

            if (hitChar != null && (!worldHit || charT <= wt))
            {
                var hitPos = start + (end - start) * charT;
                hitChar.TakeDamage(b.Damage, MathUtil.SafeNormalize(b.Velocity), hitPos);
                _particles.Sparks(hitPos, -MathUtil.SafeNormalize(b.Velocity), 5, hitChar.BloodColor, 180f, 1.4f, 0.4f);
                _bullets[i] = _bullets[--_count]; continue;
            }

            if (worldHit)
            {
                var hitPos = start + (end - start) * wt;
                if (crate != null) HitCrate(crate, hitPos);

                var dir = MathUtil.SafeNormalize(b.Velocity);
                float speed = b.Velocity.Length();
                float incidence = MathF.Abs(Vector2.Dot(dir, normal));            // 1 = head-on, 0 = grazing
                bool canBounce = b.RicochetsLeft > 0 && speed * RicochetSpeedKeep >= MinRicochetSpeed;
                bool bounce = canBounce && (incidence < GrazeAngleBoost || Rng.Chance(0.85f));
                if (bounce)
                {
                    // Reflect, lose energy, add jitter (a little more for head-on hits), and step off the surface.
                    var refl = MathUtil.Reflect(dir, normal);
                    float jitter = Rng.Signed(RicochetJitter * (0.6f + incidence));
                    refl = MathUtil.Rotate(refl, jitter);
                    b.Velocity = refl * speed * RicochetSpeedKeep;
                    b.Damage *= RicochetDamageKeep;
                    b.RicochetsLeft--;
                    b.Position = hitPos + normal * 1.5f;
                    _particles.Sparks(hitPos, refl, 8, new Vector3(1.6f, 1.1f, 0.5f), 320f, 0.9f, 0.3f);
                    _lights.Flash(hitPos, new Vector3(1f, 0.75f, 0.4f), 140f, 0.9f, 0.12f, 30f);
                    i++; continue;
                }
                _particles.Sparks(hitPos, normal, 10, new Vector3(1.5f, 1.0f, 0.45f), 300f, 1.6f, 0.35f);
                _particles.Puff(hitPos, normal, 4, new Vector3(0.45f, 0.36f, 0.26f), 40f, 12f, 0.7f);
                _bullets[i] = _bullets[--_count]; continue;
            }

            b.Position = end;
            i++;
        }
    }

    private void HitCrate(Crate crate, Vector2 hitPos)
    {
        crate.HitFlash = 1f;
        if (!crate.Lootable || crate.Opened) return;
        crate.Health -= 1f;
        if (crate.Health <= 0f) { crate.Opened = true; CrateBroken?.Invoke(crate); }
    }

    /// <summary>Emits one elongated quad per bullet (transient, this frame only).</summary>
    public void DrawTracers()
    {
        for (int i = 0; i < _count; i++)
        {
            ref var b = ref _bullets[i];
            float ang = MathUtil.ToAngle(b.Velocity);
            _particles.AddQuad(b.Position, ang, 4.5f, 6f, b.Color);
        }
    }
}
