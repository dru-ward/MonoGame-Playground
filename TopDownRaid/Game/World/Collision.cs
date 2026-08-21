using System;
using Microsoft.Xna.Framework;

namespace Game.World;

/// <summary>Geometry queries shared by movement, bullets and AI.</summary>
public static class Collision
{
    /// <summary>
    /// Pushes a circle out of an axis-aligned rectangle and removes the velocity component into the surface so
    /// the mover slides along it. Returns true if a correction was applied.
    /// </summary>
    public static bool ResolveCircleRect(ref Vector2 pos, ref Vector2 vel, float radius, Rectangle r)
    {
        var closest = new Vector2(MathHelper.Clamp(pos.X, r.Left, r.Right), MathHelper.Clamp(pos.Y, r.Top, r.Bottom));
        var diff = pos - closest; float d2 = diff.LengthSquared();
        if (d2 >= radius * radius) return false;
        if (d2 > 1e-6f)
        {
            float d = MathF.Sqrt(d2); var n = diff / d;
            pos += n * (radius - d);
            float into = Vector2.Dot(vel, n); if (into < 0f) vel -= n * into;
        }
        else
        {
            // centre inside the box: eject along the axis of least penetration
            float dl = pos.X - r.Left, dr = r.Right - pos.X, dt = pos.Y - r.Top, db = r.Bottom - pos.Y;
            float m = MathF.Min(MathF.Min(dl, dr), MathF.Min(dt, db));
            if (m == dl) pos.X = r.Left - radius; else if (m == dr) pos.X = r.Right + radius;
            else if (m == dt) pos.Y = r.Top - radius; else pos.Y = r.Bottom + radius;
        }
        return true;
    }

    /// <summary>Separates two circles (both movable) by half the overlap each.</summary>
    public static void SeparateCircles(ref Vector2 a, float ra, ref Vector2 b, float rb)
    {
        var d = b - a; float dist2 = d.LengthSquared(), min = ra + rb;
        if (dist2 >= min * min) return;
        float dist = MathF.Sqrt(MathF.Max(dist2, 1e-6f));
        var n = dist > 1e-3f ? d / dist : new Vector2(1f, 0f);
        float push = (min - dist) * 0.5f;
        a -= n * push; b += n * push;
    }

    /// <summary>
    /// Segment (a→b) vs AABB using the slab method. Returns the entry parameter t (0..1), hit point and outward
    /// surface normal. Segments starting inside the box report t = 0 with a normal pointing back along the ray.
    /// </summary>
    public static bool SegmentVsRect(Vector2 a, Vector2 b, Rectangle r, out float t, out Vector2 normal)
    {
        var d = b - a; t = 0f; normal = Vector2.Zero;
        float tmin = 0f, tmax = 1f; var nmin = Vector2.Zero;
        // X slab
        if (MathF.Abs(d.X) < 1e-6f) { if (a.X < r.Left || a.X > r.Right) return false; }
        else
        {
            float inv = 1f / d.X; float t1 = (r.Left - a.X) * inv, t2 = (r.Right - a.X) * inv; var n1 = new Vector2(-1, 0);
            if (t1 > t2) { (t1, t2) = (t2, t1); n1 = new Vector2(1, 0); }
            if (t1 > tmin) { tmin = t1; nmin = n1; }
            tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        // Y slab
        if (MathF.Abs(d.Y) < 1e-6f) { if (a.Y < r.Top || a.Y > r.Bottom) return false; }
        else
        {
            float inv = 1f / d.Y; float t1 = (r.Top - a.Y) * inv, t2 = (r.Bottom - a.Y) * inv; var n1 = new Vector2(0, -1);
            if (t1 > t2) { (t1, t2) = (t2, t1); n1 = new Vector2(0, 1); }
            if (t1 > tmin) { tmin = t1; nmin = n1; }
            tmax = MathF.Min(tmax, t2);
            if (tmin > tmax) return false;
        }
        t = tmin;
        normal = nmin == Vector2.Zero ? -Vector2.Normalize(d) : nmin;   // started inside
        return true;
    }

    /// <summary>Segment (a→b) vs circle. Returns the first hit parameter t in 0..1.</summary>
    public static bool SegmentVsCircle(Vector2 a, Vector2 b, Vector2 c, float radius, out float t)
    {
        var d = b - a; var f = a - c; t = 0f;
        float A = Vector2.Dot(d, d); if (A < 1e-8f) { return f.LengthSquared() <= radius * radius; }
        float B = 2f * Vector2.Dot(f, d), C = Vector2.Dot(f, f) - radius * radius;
        float disc = B * B - 4f * A * C; if (disc < 0f) return false;
        disc = MathF.Sqrt(disc);
        float t1 = (-B - disc) / (2f * A), t2 = (-B + disc) / (2f * A);
        if (t1 >= 0f && t1 <= 1f) { t = t1; return true; }
        if (t2 >= 0f && t2 <= 1f) { t = MathF.Max(t1, 0f); return true; }   // started inside
        return false;
    }
}
