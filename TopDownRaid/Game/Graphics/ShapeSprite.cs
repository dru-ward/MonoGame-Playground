using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game.Graphics;

/// <summary>
/// A tiny vector "shape list" rasteriser. Describe a sprite once as ordered ellipses / capsules / boxes (each with a
/// colour, a relief height and a dome factor) and rasterise it twice: an anti-aliased, premultiplied ALBEDO and a
/// height field turned into a NORMAL map. Because both come from the same list they always agree.
/// Sprite-local axes: +X = forward (rotation 0), +Y = right (screen down at rotation 0). Origin = texture centre.
/// </summary>
public sealed class ShapeSprite
{
    public enum Kind { Ellipse, Capsule, Box }

    public readonly record struct Shape(Kind Kind, Vector2 A, Vector2 B, float R, Vector3 Color, float Height, float Dome, float Shade = 0.35f);

    private readonly List<Shape> _shapes = new();
    public int Size => Width;
    public int Width { get; }
    public int Height { get; }
    private readonly float _cx, _cy;
    private readonly bool _transpose;
    /// <summary>0..1: multiplies the albedo by (1 - GrimeAmount * noise) for a dirty, uneven surface.</summary>
    public float GrimeAmount { get; set; }
    public float GrimeScale { get; set; } = 0.12f;
    public int GrimeSeed { get; set; }

    public ShapeSprite(int size) : this(size, size, false) { }
    /// <summary>Rectangular sprite. transpose=true swaps local X/Y so a "long along X" prop becomes "long along Y".</summary>
    public ShapeSprite(int width, int height, bool transpose = false)
    {
        _transpose = transpose;
        Width = transpose ? height : width; Height = transpose ? width : height;
        _cx = (Width - 1) * 0.5f; _cy = (Height - 1) * 0.5f;
    }

    private Vector2 P(float x, float y) => _transpose ? new(_cx + y, _cy + x) : new(_cx + x, _cy + y);   // sprite-local -> texel

    // ---- builders (all coordinates are sprite-local, centre = 0,0) ---------------------------------------
    public ShapeSprite Ellipse(float cx, float cy, float rx, float ry, Vector3 color, float height, float dome = 1f, float shade = 0.35f)
    { _shapes.Add(new Shape(Kind.Ellipse, P(cx, cy), _transpose ? new Vector2(ry, rx) : new Vector2(rx, ry), 0, color, height, dome, shade)); return this; }
    public ShapeSprite Circle(float cx, float cy, float r, Vector3 color, float height, float dome = 1f, float shade = 0.35f)
        => Ellipse(cx, cy, r, r, color, height, dome, shade);
    public ShapeSprite Capsule(float x0, float y0, float x1, float y1, float r, Vector3 color, float height, float dome = 1f, float shade = 0.35f)
    { _shapes.Add(new Shape(Kind.Capsule, P(x0, y0), P(x1, y1), r, color, height, dome, shade)); return this; }
    public ShapeSprite Box(float x0, float y0, float x1, float y1, Vector3 color, float height, float dome = 0.3f, float shade = 0.2f)
    {
        var a = P(MathF.Min(x0, x1), MathF.Min(y0, y1)); var b = P(MathF.Max(x0, x1), MathF.Max(y0, y1));
        _shapes.Add(new Shape(Kind.Box, Vector2.Min(a, b), Vector2.Max(a, b), 0, color, height, dome, shade)); return this;
    }

    /// <summary>Anti-aliased coverage (0..1) and normalised radial distance t (0 centre .. 1 edge) of a shape at p.</summary>
    private static float Coverage(in Shape sh, Vector2 p, out float t)
    {
        switch (sh.Kind)
        {
            case Kind.Ellipse:
            {
                var d = (p - sh.A) / sh.B; t = d.Length();
                return MathHelper.Clamp((1f - t) * MathF.Min(sh.B.X, sh.B.Y) + 0.5f, 0f, 1f);
            }
            case Kind.Capsule:
            {
                var ab = sh.B - sh.A; float len2 = MathF.Max(ab.LengthSquared(), 1e-4f);
                float u = MathHelper.Clamp(Vector2.Dot(p - sh.A, ab) / len2, 0f, 1f);
                float d = (p - (sh.A + ab * u)).Length(); t = d / sh.R;
                return MathHelper.Clamp(sh.R - d + 0.5f, 0f, 1f);
            }
            default:
            {
                float dx = MathF.Max(sh.A.X - p.X, p.X - sh.B.X), dy = MathF.Max(sh.A.Y - p.Y, p.Y - sh.B.Y);
                float d = MathF.Max(dx, dy);
                float hx = MathF.Max((sh.B.X - sh.A.X) * 0.5f, 0.5f), hy = MathF.Max((sh.B.Y - sh.A.Y) * 0.5f, 0.5f);
                var c = (sh.A + sh.B) * 0.5f;
                t = MathF.Max(MathF.Abs(p.X - c.X) / hx, MathF.Abs(p.Y - c.Y) / hy);
                return MathHelper.Clamp(-d + 0.5f, 0f, 1f);
            }
        }
    }

    // ---- outline (dark silhouette rim, keeps characters readable on dark floors) -----------------------------
    public bool Outline { get; set; }
    public Vector3 OutlineColor { get; set; } = new(0.02f, 0.02f, 0.025f);
    public float OutlineWidth { get; set; } = 1.2f;
    /// <summary>Clamps how far normals may tilt (min z). Dome rims otherwise become near-vertical and glare under lights.</summary>
    public float MinNormalZ { get; set; } = 0.55f;

    /// <summary>Shared rasterisation: premultiplied colour, coverage and height per texel, then the optional outline.</summary>
    private void Rasterize(float reliefPx, out Vector3[] col, out float[] alpha, out float[] height, out float[] shapeAlpha)
    {
        int w = Width, h = Height;
        col = new Vector3[w * h]; alpha = new float[w * h]; height = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            var p = new Vector2(x, y); Vector3 c = Vector3.Zero; float a = 0f, hh = 0f;
            foreach (var sh in _shapes)
            {
                float cov = Coverage(sh, p, out float t);
                if (cov <= 0f) continue;
                float shade = 1f - sh.Dome * sh.Shade * MathHelper.Clamp(t, 0f, 1f);   // rim darkening for rounded parts
                c = Vector3.Lerp(c, sh.Color * shade, cov);
                float dome = MathF.Sqrt(MathF.Max(0f, 1f - t * t));                    // hemispherical profile
                float shH = sh.Height * MathHelper.Lerp(1f, dome, sh.Dome) * reliefPx;
                hh = MathHelper.Lerp(hh, shH, cov);
                a += cov * (1f - a);
            }
            if (GrimeAmount > 0f && a > 0f)
            {
                float g = TextureFactory.Noise(x * GrimeScale + GrimeSeed * 17.3f, y * GrimeScale + GrimeSeed * 5.1f, 0);
                float g2 = TextureFactory.Noise(x * GrimeScale * 4f, y * GrimeScale * 4f + GrimeSeed, 0);
                c *= 1f - GrimeAmount * (0.7f * g + 0.3f * g2);
            }
            int i = y * w + x; col[i] = c; alpha[i] = a; height[i] = hh;
        }
        shapeAlpha = alpha;
        if (!Outline) return;
        shapeAlpha = (float[])alpha.Clone();                 // coverage WITHOUT the outline (normal map uses this)
        // dilate the silhouette by OutlineWidth: any texel near covered texels gets the outline colour underneath
        int r = (int)MathF.Ceiling(OutlineWidth);
        var oa = new float[w * h];
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float best = 0f;
            for (int dy = -r; dy <= r; dy++)
            for (int dx = -r; dx <= r; dx++)
            {
                int nx = x + dx, ny = y + dy; if (nx < 0 || ny < 0 || nx >= w || ny >= h) continue;
                float d = MathF.Sqrt(dx * dx + dy * dy); if (d > OutlineWidth + 0.5f) continue;
                float falloff = MathHelper.Clamp(OutlineWidth + 0.5f - d, 0f, 1f);
                best = MathF.Max(best, alpha[ny * w + nx] * falloff);
            }
            oa[y * w + x] = best;
        }
        for (int i = 0; i < col.Length; i++)
        {
            float a = alpha[i], o = oa[i] * (1f - a);            // outline only where the shape itself is not opaque
            if (o <= 0f) continue;
            float na = a + o;
            col[i] = (col[i] * a + OutlineColor * o) / na;
            alpha[i] = na;                                       // height stays 0 -> a slope toward the shape edge
        }
    }

    /// <summary>Rasterises the premultiplied albedo. Later shapes paint over earlier ones (painter's order).</summary>
    public Texture2D CreateAlbedo(GraphicsDevice gd)
    {
        Rasterize(8f, out var col, out var alpha, out _, out _);
        var data = new Color[col.Length];
        for (int i = 0; i < data.Length; i++)
        {
            float a = alpha[i]; var c = col[i];
            data[i] = a <= 0f ? Color.Transparent : new Color(c.X * a, c.Y * a, c.Z * a, a);
        }
        var tex = new Texture2D(gd, Width, Height); tex.SetData(data); return tex;
    }

    /// <summary>Rasterises the height field and converts it to a premultiplied, encoded normal map.</summary>
    public Texture2D CreateNormal(GraphicsDevice gd, float reliefPx = 8f, float strength = 0.35f)
    {
        // The outline is albedo-only: its texels get NO normal (alpha 0 here) so they keep the floor's normal and stay
        // dark instead of lighting up as a sloped rim.
        Rasterize(reliefPx, out _, out _, out var height, out var alpha);
        var normals = TextureFactory.HeightToNormal(height, Width, Height, strength, wrap: false);
        var data = new Color[alpha.Length];
        for (int i = 0; i < data.Length; i++)
        {
            float a = alpha[i];
            if (a <= 0f) { data[i] = Color.Transparent; continue; }
            var e = normals[i].ToVector4();
            var n = new Vector3(e.X * 2f - 1f, e.Y * 2f - 1f, e.Z * 2f - 1f);
            if (n.Z < MinNormalZ)                                  // limit the tilt, keep the direction
            {
                float xy = MathF.Sqrt(MathF.Max(1e-6f, n.X * n.X + n.Y * n.Y));
                float k = MathF.Sqrt(1f - MinNormalZ * MinNormalZ) / xy;
                n = new Vector3(n.X * k, n.Y * k, MinNormalZ);
            }
            n = Vector3.Normalize(n);
            data[i] = new Color((n.X * 0.5f + 0.5f) * a, (n.Y * 0.5f + 0.5f) * a, (n.Z * 0.5f + 0.5f) * a, a);
        }
        var tex = new Texture2D(gd, Width, Height); tex.SetData(data); return tex;
    }

    /// <summary>Convenience: albedo + normal in one call.</summary>
    public SpritePair Build(GraphicsDevice gd, float reliefPx = 8f, float strength = 0.35f)
        => new(CreateAlbedo(gd), CreateNormal(gd, reliefPx, strength));
}

/// <summary>An albedo + normal texture pair with a shared origin/scale, ready for the two-pass G-buffer draw.</summary>
public sealed record SpritePair(Texture2D Albedo, Texture2D Normal)
{
    public Vector2 Origin => new(Albedo.Width * 0.5f, Albedo.Height * 0.5f);
    public int Width => Albedo.Width;
}
