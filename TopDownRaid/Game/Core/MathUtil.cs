using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game.Core;

/// <summary>Small math helpers shared by every system.</summary>
public static class MathUtil
{
    /// <summary>Frame-rate independent smoothing factor: use as Lerp(a, b, Damp(k, dt)).</summary>
    public static float Damp(float k, float dt) => 1f - MathF.Exp(-k * dt);

    /// <summary>Lerp between angles taking the shortest way round.</summary>
    public static float LerpAngle(float a, float b, float t) => a + MathHelper.WrapAngle(b - a) * t;

    public static Vector2 FromAngle(float a) => new(MathF.Cos(a), MathF.Sin(a));
    public static float   ToAngle(Vector2 v) => MathF.Atan2(v.Y, v.X);

    /// <summary>Rotates v by angle a (radians).</summary>
    public static Vector2 Rotate(Vector2 v, float a)
    {
        float c = MathF.Cos(a), s = MathF.Sin(a);
        return new Vector2(v.X * c - v.Y * s, v.X * s + v.Y * c);
    }

    /// <summary>Moves value toward target by at most maxDelta.</summary>
    public static float Approach(float value, float target, float maxDelta)
        => value < target ? MathF.Min(value + maxDelta, target) : MathF.Max(value - maxDelta, target);

    /// <summary>Moves a vector toward a target vector by at most maxDelta (used for acceleration-limited steering).</summary>
    public static Vector2 Approach(Vector2 value, Vector2 target, float maxDelta)
    {
        var d = target - value; float len = d.Length();
        return len <= maxDelta || len < 1e-5f ? target : value + d / len * maxDelta;
    }

    public static Vector2 SafeNormalize(Vector2 v) => v.LengthSquared() > 1e-8f ? Vector2.Normalize(v) : Vector2.Zero;

    /// <summary>Reflects an incoming direction/velocity around a surface normal.</summary>
    public static Vector2 Reflect(Vector2 v, Vector2 n) => v - 2f * Vector2.Dot(v, n) * n;
}

/// <summary>Deterministic-seedable random helpers used by gameplay and art generation.</summary>
public static class Rng
{
    private static Random _r = new(1234);
    public static void Seed(int seed) => _r = new Random(seed);
    public static float Float() => (float)_r.NextDouble();
    public static float Range(float min, float max) => min + (max - min) * Float();
    public static int   Int(int minInclusive, int maxExclusive) => _r.Next(minInclusive, maxExclusive);
    public static float Angle() => Float() * MathHelper.TwoPi;
    public static float Signed(float amplitude) => (Float() - 0.5f) * 2f * amplitude;
    public static bool  Chance(float p) => Float() < p;
    public static Vector2 InCircle(float radius) { float a = Angle(), r = MathF.Sqrt(Float()) * radius; return new Vector2(MathF.Cos(a) * r, MathF.Sin(a) * r); }
    public static Vector2 UnitVector() => MathUtil.FromAngle(Angle());
    public static T Pick<T>(IReadOnlyList<T> list) => list[_r.Next(list.Count)];
}
