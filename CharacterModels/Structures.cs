using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CharacterModels;

/// <summary>Axis-aligned collision box in world space (structures are placed at 90° yaw steps so boxes stay aligned).</summary>
public struct Aabb
{
    public Vector3 Min, Max;
    public Aabb(Vector3 min, Vector3 max) { Min = min; Max = max; }
    public static Aabb FromCenter(Vector3 c, Vector3 size) => new(c - size * 0.5f, c + size * 0.5f);
}

/// <summary>
/// Procedural buildings and props built from boxes and lofts into one static mesh: timber-framed cottages with lit
/// windows, a plank barn, a stone well, a watchtower, fence runs, a dry-stone wall with a gate, barrels and crates.
/// Every piece also registers colliders (boxes or circles) and, where it makes sense, a point light.
/// </summary>
public sealed class Structures
{
    public VertexBuffer VertexBuffer = null!;
    public IndexBuffer IndexBuffer = null!;
    public int Triangles;
    public readonly List<Aabb> Boxes = new();                              // player + camera collision
    public readonly List<(Vector3 pos, float r)> Circles = new();          // round things
    public readonly List<(Vector3 pos, Vector3 color, float radius, float intensity)> Lights = new();
    public readonly List<string> Names = new();

    private readonly MeshBuilder _mb = new();
    private readonly Weighter _w;
    private readonly Random _rnd;

    // Palette
    private static readonly Color Plaster = new(214, 200, 176), PlasterDark = new(186, 172, 150);
    private static readonly Color Timber = new(78, 54, 36), TimberLight = new(112, 82, 54);
    private static readonly Color Slate = new(72, 76, 88), SlateLight = new(92, 96, 108), Thatch = new(168, 138, 78), ThatchDark = new(128, 102, 56);
    private static readonly Color Stone = new(128, 124, 116), StoneDark = new(98, 94, 88), StoneLight = new(152, 148, 140);
    private static readonly Color Plank = new(140, 98, 58), PlankDark = new(112, 78, 46), Iron = new(58, 56, 60);
    private static readonly Color Glass = new(255, 214, 140);              // emissive in the deferred path (Mat.Glow)
    private static readonly Vector2 Wall = new(0.03f, 0.1f), Wood = Mat.Wood, Rock = new(0.12f, 0.2f), Metal = new(0.6f, 0.5f);

    private Structures(Random rnd)
    {
        _rnd = rnd;
        var sk = new Skeleton(); sk.Add("root", null, Vector3.Zero, Vector3.Up);
        _w = Weighter.Fixed(sk, "root");
    }

    public static Structures Build(GraphicsDevice gd, int seed, Action<Structures> layout)
    {
        var s = new Structures(new Random(seed));
        layout(s);
        (s.VertexBuffer, s.IndexBuffer) = s._mb.Upload(gd);
        s.Triangles = s._mb.TriangleCount;
        return s;
    }

    private float Rand(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);
    private Color Vary(Color c, int amt) { int d = _rnd.Next(-amt, amt + 1); return new Color(Math.Clamp(c.R + d, 0, 255), Math.Clamp(c.G + d, 0, 255), Math.Clamp(c.B + d, 0, 255)); }
    private void Box(Vector3 c, Vector3 size, Color col, Vector2 mat, Quaternion? rot = null) => _mb.Box(c, size, col, mat, _w, rot);

    /// <summary>Rotates a local offset (x right, z forward) by a yaw of 0/90/180/270° and adds the origin.</summary>
    private static Vector3 P(Vector3 origin, float yaw, float x, float y, float z)
    {
        var r = Vector3.Transform(new Vector3(x, y, z), Matrix.CreateRotationY(yaw));
        return origin + r;
    }
    private static Vector3 Size(float yaw, Vector3 s) => MathF.Abs(MathF.Sin(yaw)) > 0.5f ? new Vector3(s.Z, s.Y, s.X) : s;

    // ------------------------------------------------------------------ cottage

    /// <summary>Timber-framed cottage: stone plinth, plaster walls with beams, door, glowing windows, pitched roof, chimney.</summary>
    public void Cottage(Vector3 origin, float yaw, float w = 5f, float d = 4f, float h = 2.6f, bool thatched = false, string name = "Cottage")
    {
        Names.Add(name);
        // Plinth and walls.
        Box(P(origin, yaw, 0, 0.2f, 0), Size(yaw, new Vector3(w + 0.2f, 0.4f, d + 0.2f)), StoneDark, Rock);
        Box(P(origin, yaw, 0, 0.4f + h * 0.5f, 0), Size(yaw, new Vector3(w, h, d)), Plaster, Wall);
        Boxes.Add(Aabb.FromCenter(P(origin, yaw, 0, h * 0.5f + 0.2f, 0), Size(yaw, new Vector3(w + 0.25f, h + 0.4f, d + 0.25f))));
        // Timber frame: corner posts, sill/top beams, a few diagonals per wall.
        float t = 0.12f;
        for (int sx = -1; sx <= 1; sx += 2)
        for (int sz = -1; sz <= 1; sz += 2)
            Box(P(origin, yaw, sx * w * 0.5f, 0.4f + h * 0.5f, sz * d * 0.5f), new Vector3(t + 0.06f, h, t + 0.06f), Timber, Wood);
        for (int sz = -1; sz <= 1; sz += 2)
        {
            Box(P(origin, yaw, 0, 0.4f + h - 0.08f, sz * (d * 0.5f + 0.02f)), Size(yaw, new Vector3(w, 0.16f, t)), Timber, Wood);
            Box(P(origin, yaw, 0, 0.48f, sz * (d * 0.5f + 0.02f)), Size(yaw, new Vector3(w, 0.16f, t)), Timber, Wood);
            for (int k = -1; k <= 1; k += 2)
                Box(P(origin, yaw, k * w * 0.32f, 0.4f + h * 0.5f, sz * (d * 0.5f + 0.02f)), Size(yaw, new Vector3(t, h, t)), Timber, Wood);
        }
        for (int sx = -1; sx <= 1; sx += 2)
        {
            Box(P(origin, yaw, sx * (w * 0.5f + 0.02f), 0.4f + h - 0.08f, 0), Size(yaw, new Vector3(t, 0.16f, d)), Timber, Wood);
            Box(P(origin, yaw, sx * (w * 0.5f + 0.02f), 0.48f, 0), Size(yaw, new Vector3(t, 0.16f, d)), Timber, Wood);
        }
        // Door on the front (+z face), windows on the front and sides.
        Box(P(origin, yaw, -w * 0.18f, 0.4f + 1.0f, d * 0.5f + 0.03f), Size(yaw, new Vector3(0.9f, 2.0f, 0.08f)), Timber, Wood);
        Box(P(origin, yaw, -w * 0.18f, 0.4f + 1.0f, d * 0.5f + 0.07f), Size(yaw, new Vector3(0.74f, 1.84f, 0.04f)), TimberLight, Wood);
        Box(P(origin, yaw, -w * 0.18f + 0.28f, 0.4f + 1.0f, d * 0.5f + 0.1f), new Vector3(0.05f, 0.05f, 0.05f), Iron, Metal);
        Window(origin, yaw, w * 0.25f, d * 0.5f + 0.03f, 0, 0.4f + 1.35f);
        Window(origin, yaw, -w * 0.5f - 0.03f, 0, MathHelper.PiOver2, 0.4f + 1.35f);
        Window(origin, yaw, w * 0.5f + 0.03f, 0, MathHelper.PiOver2, 0.4f + 1.35f);
        Window(origin, yaw, 0, -d * 0.5f - 0.03f, 0, 0.4f + 1.35f);
        // Roof: two slabs on the ridge running along x, overhanging, plus gable triangles.
        float ridge = 0.4f + h + d * 0.42f, eave = 0.4f + h;
        float slope = MathF.Atan2(ridge - eave, d * 0.5f + 0.35f);
        float slabLen = MathF.Sqrt((d * 0.5f + 0.35f) * (d * 0.5f + 0.35f) + (ridge - eave) * (ridge - eave)) + 0.15f;
        var roofCol = thatched ? Thatch : Slate; var roofCol2 = thatched ? ThatchDark : SlateLight;
        for (int sz = -1; sz <= 1; sz += 2)
        {
            // NB: XNA's q1 * q2 applies q2 first; we want tilt in the building's local frame, then the yaw -> yaw * tilt.
            var rot = Quaternion.CreateFromAxisAngle(Vector3.Up, yaw) * Quaternion.CreateFromAxisAngle(Vector3.Right, sz * slope);
            var c = P(origin, yaw, 0, (ridge + eave) * 0.5f + 0.05f, sz * (d * 0.25f + 0.1f));
            Box(c, new Vector3(w + 0.7f, thatched ? 0.28f : 0.12f, slabLen), roofCol, thatched ? Mat.Cloth : Rock, rot);
            // Tile rows / thatch bands.
            for (int k = 0; k < 4; k++)
            {
                float f = (k + 0.5f) / 4f;
                var rc = P(origin, yaw, 0, MathHelper.Lerp(ridge, eave, f) + 0.1f, sz * MathHelper.Lerp(0.05f, d * 0.5f + 0.3f, f));
                Box(rc, new Vector3(w + 0.72f, 0.04f, slabLen / 4.5f), k % 2 == 0 ? roofCol2 : Vary(roofCol, 8), thatched ? Mat.Cloth : Rock, rot);
            }
        }
        Box(P(origin, yaw, 0, ridge + 0.08f, 0), Size(yaw, new Vector3(w + 0.8f, 0.16f, 0.3f)), thatched ? ThatchDark : Timber, Wood);
        Gable(origin, yaw, -w * 0.5f, eave, ridge, d); Gable(origin, yaw, w * 0.5f, eave, ridge, d);
        // Chimney.
        Box(P(origin, yaw, w * 0.3f, ridge + 0.25f, -d * 0.1f), new Vector3(0.5f, 1.3f, 0.5f), StoneDark, Rock);
        Box(P(origin, yaw, w * 0.3f, ridge + 0.92f, -d * 0.1f), new Vector3(0.6f, 0.12f, 0.6f), Stone, Rock);
        // Warm light spilling from the front window (deferred path).
        Lights.Add((P(origin, yaw, w * 0.25f, 1.6f, d * 0.5f + 0.6f), new Vector3(1.0f, 0.7f, 0.35f), 4.5f, 2.5f));
    }

    private void Window(Vector3 origin, float yaw, float x, float z, float faceYaw, float y)
    {
        var rot = Quaternion.CreateFromAxisAngle(Vector3.Up, yaw + faceYaw);
        var c = P(origin, yaw, x, y, z);
        Box(c, new Vector3(0.8f, 0.8f, 0.08f), Timber, Wood, rot);
        Box(c + Vector3.Transform(new Vector3(0, 0, 0.03f), rot), new Vector3(0.66f, 0.66f, 0.04f), Glass, Mat.Glow, rot);
        Box(c + Vector3.Transform(new Vector3(0, 0, 0.06f), rot), new Vector3(0.05f, 0.7f, 0.03f), Timber, Wood, rot);
        Box(c + Vector3.Transform(new Vector3(0, 0, 0.06f), rot), new Vector3(0.7f, 0.05f, 0.03f), Timber, Wood, rot);
        // Sill.
        Box(c + Vector3.Transform(new Vector3(0, -0.43f, 0.05f), rot), new Vector3(0.9f, 0.06f, 0.14f), Stone, Rock, rot);
    }

    /// <summary>Gable end: a stack of thinning boxes approximating the triangle, with a beam along the edge.</summary>
    private void Gable(Vector3 origin, float yaw, float x, float eave, float ridge, float d)
    {
        const int rows = 6;
        for (int k = 0; k < rows; k++)
        {
            float f0 = k / (float)rows, f1 = (k + 1) / (float)rows;
            float y = MathHelper.Lerp(eave, ridge, (f0 + f1) * 0.5f);
            float width = d * (1f - (f0 + f1) * 0.5f);
            Box(P(origin, yaw, x, y, 0), Size(yaw, new Vector3(0.16f, (ridge - eave) / rows + 0.01f, width)), k % 2 == 0 ? Plaster : PlasterDark, Wall);
        }
        Box(P(origin, yaw, x, (eave + ridge) * 0.5f, 0), Size(yaw, new Vector3(0.2f, 0.14f, 0.14f)), Timber, Wood);
    }

    // --------------------------------------------------------------------- barn

    public void Barn(Vector3 origin, float yaw, float w = 7f, float d = 9f, float h = 3.4f)
    {
        Names.Add("Barn");
        Box(P(origin, yaw, 0, 0.15f, 0), Size(yaw, new Vector3(w + 0.3f, 0.3f, d + 0.3f)), StoneDark, Rock);
        // Plank walls: horizontal strips alternating tone.
        int strips = (int)(h / 0.28f);
        for (int k = 0; k < strips; k++)
            Box(P(origin, yaw, 0, 0.3f + (k + 0.5f) * h / strips, 0), Size(yaw, new Vector3(w, h / strips + 0.01f, d)), k % 2 == 0 ? Plank : PlankDark, Wood);
        Boxes.Add(Aabb.FromCenter(P(origin, yaw, 0, h * 0.5f + 0.15f, 0), Size(yaw, new Vector3(w + 0.2f, h + 0.3f, d + 0.2f))));
        for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            Box(P(origin, yaw, sx * w * 0.5f, 0.3f + h * 0.5f, sz * d * 0.5f), new Vector3(0.22f, h, 0.22f), Timber, Wood);
        // Big double door on the front, loft hatch above.
        Box(P(origin, yaw, 0, 0.3f + 1.35f, d * 0.5f + 0.04f), Size(yaw, new Vector3(2.6f, 2.7f, 0.08f)), Timber, Wood);
        for (int k = -1; k <= 1; k += 2) Box(P(origin, yaw, k * 0.66f, 0.3f + 1.35f, d * 0.5f + 0.08f), Size(yaw, new Vector3(1.2f, 2.55f, 0.05f)), PlankDark, Wood);
        Box(P(origin, yaw, 0, 0.3f + 1.35f, d * 0.5f + 0.1f), Size(yaw, new Vector3(0.08f, 2.6f, 0.06f)), Iron, Metal);
        Box(P(origin, yaw, 0, 0.3f + h - 0.5f, d * 0.5f + 0.05f), Size(yaw, new Vector3(1.0f, 0.9f, 0.06f)), Timber, Wood);
        // Roof: steeper, with a ridge along z.
        float eave = 0.3f + h, ridge = eave + w * 0.38f;
        float slope = MathF.Atan2(ridge - eave, w * 0.5f + 0.4f);
        float slabLen = MathF.Sqrt((w * 0.5f + 0.4f) * (w * 0.5f + 0.4f) + (ridge - eave) * (ridge - eave)) + 0.2f;
        for (int sx = -1; sx <= 1; sx += 2)
        {
            var rot = Quaternion.CreateFromAxisAngle(Vector3.Up, yaw) * Quaternion.CreateFromAxisAngle(Vector3.Backward, -sx * slope);
            Box(P(origin, yaw, sx * (w * 0.25f + 0.15f), (ridge + eave) * 0.5f + 0.05f, 0), new Vector3(slabLen, 0.14f, d + 0.8f), Slate, Rock, rot);
            for (int k = 0; k < 5; k++)
            {
                float f = (k + 0.5f) / 5f;
                Box(P(origin, yaw, sx * MathHelper.Lerp(0.05f, w * 0.5f + 0.35f, f), MathHelper.Lerp(ridge, eave, f) + 0.12f, 0), new Vector3(slabLen / 5.5f, 0.04f, d + 0.82f), k % 2 == 0 ? SlateLight : Vary(Slate, 8), Rock, rot);
            }
        }
        Box(P(origin, yaw, 0, ridge + 0.1f, 0), Size(yaw, new Vector3(0.3f, 0.18f, d + 0.9f)), Timber, Wood);
        // Gables front/back as box stacks.
        for (int sz = -1; sz <= 1; sz += 2)
            for (int k = 0; k < 7; k++)
            {
                float f = (k + 0.5f) / 7f;
                Box(P(origin, yaw, 0, MathHelper.Lerp(eave, ridge, f), sz * d * 0.5f), Size(yaw, new Vector3(w * (1f - f), (ridge - eave) / 7f + 0.01f, 0.16f)), k % 2 == 0 ? Plank : PlankDark, Wood);
            }
        // Hay bales by the door.
        for (int k = 0; k < 3; k++) Bale(P(origin, yaw, -w * 0.5f + 0.9f + k * 0.1f, 0.35f + (k == 2 ? 0.7f : 0), d * 0.5f + 1.2f + (k == 1 ? 0.9f : 0)), yaw + (k == 1 ? MathHelper.PiOver2 : 0));
    }

    private void Bale(Vector3 c, float yaw)
    {
        var rot = Quaternion.CreateFromAxisAngle(Vector3.Up, yaw);
        Box(c, new Vector3(1.0f, 0.7f, 0.7f), Vary(Thatch, 10), Mat.Cloth, rot);
        Box(c + new Vector3(0, 0.02f, 0), new Vector3(1.02f, 0.06f, 0.72f), TimberLight, Wood, rot);
        Circles.Add((c, 0.55f));
    }

    // --------------------------------------------------------------------- well

    public void Well(Vector3 c)
    {
        Names.Add("Well");
        // Stone ring: blocks around a circle, two courses with staggered joints.
        for (int course = 0; course < 3; course++)
        {
            int n = 12;
            for (int k = 0; k < n; k++)
            {
                float a = (k + (course % 2) * 0.5f) / n * MathHelper.TwoPi;
                var p = c + new Vector3(MathF.Cos(a) * 0.75f, 0.15f + course * 0.3f, MathF.Sin(a) * 0.75f);
                Box(p, new Vector3(0.42f, 0.3f, 0.3f), Vary(course == 2 ? StoneLight : Stone, 14), Rock, Quaternion.CreateFromAxisAngle(Vector3.Up, -a));
            }
        }
        // Dark water disc.
        _mb.Ellipsoid(c + new Vector3(0, 0.2f, 0), new Vector3(0.6f, 0.05f, 0.6f), 16, 4, new Color(18, 28, 40), Mat.Eye, _w);
        // Posts, crossbar, roof, bucket.
        for (int sx = -1; sx <= 1; sx += 2) Box(c + new Vector3(sx * 0.85f, 1.25f, 0), new Vector3(0.14f, 1.9f, 0.14f), Timber, Wood);
        Box(c + new Vector3(0, 1.9f, 0), new Vector3(1.9f, 0.1f, 0.1f), TimberLight, Wood);
        var rot = Quaternion.CreateFromAxisAngle(Vector3.Right, 0.7f);
        for (int sz = -1; sz <= 1; sz += 2)
            Box(c + new Vector3(0, 2.35f, sz * 0.45f), new Vector3(2.3f, 0.08f, 1.2f), Vary(Slate, 6), Rock, Quaternion.CreateFromAxisAngle(Vector3.Right, -sz * 0.7f));
        Box(c + new Vector3(0, 2.72f, 0), new Vector3(2.4f, 0.12f, 0.2f), Timber, Wood);
        Box(c + new Vector3(0, 1.5f, 0), new Vector3(0.02f, 0.8f, 0.02f), Iron, Metal);
        _mb.Loft(new[] { new Ring(c + new Vector3(0, 0.95f, 0), 0.14f, PlankDark, Wood), new Ring(c + new Vector3(0, 1.2f, 0), 0.17f, Plank, Wood) }, 10, _w, Vector3.Backward, capStart: true, capSteps: 1);
        Circles.Add((c, 1.05f));
        Lights.Add((c + new Vector3(0, 1.0f, 0), new Vector3(0.4f, 0.6f, 0.9f), 2.5f, 1.2f));
    }

    // ---------------------------------------------------------------- watchtower

    public void Watchtower(Vector3 c, float yaw = 0)
    {
        Names.Add("Watchtower");
        float h = 5.5f, half = 1.1f;
        for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
        {
            var top = P(c, yaw, sx * half * 0.8f, h, sz * half * 0.8f);
            var bot = P(c, yaw, sx * half * 1.25f, 0, sz * half * 1.25f);
            _mb.Loft(new[] { new Ring(bot, 0.13f, Timber, Wood), new Ring(top, 0.11f, TimberLight, Wood) }, 8, _w, Vector3.Backward, capEnd: true, capSteps: 1);
        }
        // Cross braces at two heights.
        for (int lvl = 1; lvl <= 2; lvl++)
        {
            float y = h * lvl / 3f; float r = MathHelper.Lerp(half * 1.25f, half * 0.8f, lvl / 3f);
            for (int side = 0; side < 4; side++)
            {
                float a = yaw + side * MathHelper.PiOver2;
                Box(P(c, a, 0, y, r), new Vector3(r * 2f, 0.1f, 0.1f), Timber, Wood, Quaternion.CreateFromAxisAngle(Vector3.Up, a));
                Box(P(c, a, 0, y + h / 6f, r), new Vector3(r * 2.6f, 0.08f, 0.08f), TimberLight, Wood, Quaternion.CreateFromAxisAngle(Vector3.Up, a) * Quaternion.CreateFromAxisAngle(Vector3.Backward, 0.6f));
            }
        }
        // Platform, railing, roof.
        Box(P(c, yaw, 0, h + 0.1f, 0), new Vector3(half * 2.4f, 0.2f, half * 2.4f), Plank, Wood, Quaternion.CreateFromAxisAngle(Vector3.Up, yaw));
        for (int side = 0; side < 4; side++)
        {
            float a = yaw + side * MathHelper.PiOver2;
            var rot = Quaternion.CreateFromAxisAngle(Vector3.Up, a);
            Box(P(c, a, 0, h + 0.75f, half * 1.15f), new Vector3(half * 2.4f, 0.08f, 0.08f), Timber, Wood, rot);
            for (int k = -2; k <= 2; k++) Box(P(c, a, k * half * 0.5f, h + 0.45f, half * 1.15f), new Vector3(0.07f, 0.7f, 0.07f), Timber, Wood, rot);
        }
        for (int sx = -1; sx <= 1; sx += 2) for (int sz = -1; sz <= 1; sz += 2)
            Box(P(c, yaw, sx * half, h + 1.5f, sz * half), new Vector3(0.12f, 2.4f, 0.12f), Timber, Wood);
        _mb.Ellipsoid(P(c, yaw, 0, h + 2.6f, 0), new Vector3(half * 1.9f, 0.8f, half * 1.9f), 4, 4, Vary(Slate, 6), Rock, _w,
            d => d.Y > 0 ? 1f : 0.25f, Quaternion.CreateFromAxisAngle(Vector3.Up, yaw + MathHelper.PiOver4));
        // Ladder on the front leg.
        for (int k = 0; k < 12; k++) Box(P(c, yaw, 0, 0.4f + k * 0.42f, half * 1.3f), new Vector3(0.5f, 0.05f, 0.05f), TimberLight, Wood, Quaternion.CreateFromAxisAngle(Vector3.Up, yaw));
        for (int sx = -1; sx <= 1; sx += 2) Box(P(c, yaw, sx * 0.25f, h * 0.5f, half * 1.3f), new Vector3(0.06f, h, 0.06f), Timber, Wood, Quaternion.CreateFromAxisAngle(Vector3.Up, yaw));
        Circles.Add((c, half * 1.5f));
        Lights.Add((P(c, yaw, 0, h + 1.2f, 0), new Vector3(1.0f, 0.6f, 0.25f), 7f, 3.5f));
    }

    // -------------------------------------------------------------- fence & wall

    /// <summary>Post-and-rail fence along a polyline; posts every ~1.8 m, two rails, slight random lean.</summary>
    public void Fence(params Vector3[] points)
    {
        Names.Add("Fence");
        for (int i = 0; i < points.Length - 1; i++)
        {
            var a = points[i]; var b = points[i + 1];
            float len = Vector3.Distance(a, b); var dir = (b - a) / len;
            float yaw = MathF.Atan2(dir.X, dir.Z);
            var rot = Quaternion.CreateFromAxisAngle(Vector3.Up, yaw);
            int posts = Math.Max(2, (int)(len / 1.8f) + 1);
            for (int k = 0; k < posts; k++)
            {
                var p = Vector3.Lerp(a, b, k / (float)(posts - 1));
                Box(p + new Vector3(0, 0.55f, 0), new Vector3(0.12f, 1.1f, 0.12f), Vary(Timber, 10), Wood, rot * Quaternion.CreateFromAxisAngle(Vector3.Right, Rand(-0.03f, 0.03f)));
            }
            Box((a + b) * 0.5f + new Vector3(0, 0.9f, 0), new Vector3(0.07f, 0.1f, len), Vary(TimberLight, 10), Wood, rot);
            Box((a + b) * 0.5f + new Vector3(0, 0.5f, 0), new Vector3(0.07f, 0.1f, len), Vary(TimberLight, 10), Wood, rot);
            Boxes.Add(new Aabb(Vector3.Min(a, b) - new Vector3(0.12f, 0, 0.12f), Vector3.Max(a, b) + new Vector3(0.12f, 1.1f, 0.12f)));
        }
    }

    /// <summary>Dry-stone wall: courses of jittered blocks; an optional gap leaves a gateway with posts.</summary>
    public void StoneWall(Vector3 a, Vector3 b, float height = 1.1f, float gateAt = -1f, float gateWidth = 1.6f)
    {
        Names.Add("Stone wall");
        float len = Vector3.Distance(a, b); var dir = (b - a) / len;
        float yaw = MathF.Atan2(dir.X, dir.Z);
        var rot = Quaternion.CreateFromAxisAngle(Vector3.Up, yaw);
        int courses = (int)(height / 0.26f);
        for (int course = 0; course < courses; course++)
        {
            float y = 0.13f + course * 0.26f;
            float x = course % 2 == 0 ? 0 : 0.25f;
            while (x < len)
            {
                float bl = Rand(0.35f, 0.6f);
                float mid = x + bl * 0.5f;
                bool gate = gateAt >= 0 && MathF.Abs(mid - gateAt) < gateWidth * 0.5f;
                if (!gate)
                {
                    var p = a + dir * mid + new Vector3(0, y + Rand(-0.01f, 0.01f), 0);
                    Box(p, new Vector3(0.42f + Rand(-0.06f, 0.06f), 0.26f, bl - 0.03f), Vary(course == courses - 1 ? StoneLight : Stone, 16), Rock, rot);
                }
                x += bl;
            }
        }
        // Collision: two segments if there is a gate.
        void Seg(float s0, float s1)
        {
            var p0 = a + dir * s0; var p1 = a + dir * s1;
            Boxes.Add(new Aabb(Vector3.Min(p0, p1) - new Vector3(0.25f, 0, 0.25f), Vector3.Max(p0, p1) + new Vector3(0.25f, height, 0.25f)));
        }
        if (gateAt < 0) Seg(0, len);
        else
        {
            Seg(0, gateAt - gateWidth * 0.5f); Seg(gateAt + gateWidth * 0.5f, len);
            for (int k = -1; k <= 1; k += 2)
                Box(a + dir * (gateAt + k * gateWidth * 0.5f) + new Vector3(0, height * 0.5f + 0.25f, 0), new Vector3(0.4f, height + 0.5f, 0.4f), StoneLight, Rock, rot);
        }
    }

    // -------------------------------------------------------------------- props

    public void Barrel(Vector3 c)
    {
        var col = Vary(Plank, 12);
        _mb.Loft(new[]
        {
            new Ring(c + new Vector3(0, 0.02f, 0), 0.27f, col, Wood), new Ring(c + new Vector3(0, 0.45f, 0), 0.32f, col, Wood),
            new Ring(c + new Vector3(0, 0.88f, 0), 0.27f, col, Wood)
        }, 12, _w, Vector3.Backward, capStart: true, capEnd: true, capSteps: 1);
        foreach (float y in new[] { 0.15f, 0.75f })
            _mb.Loft(new[] { new Ring(c + new Vector3(0, y - 0.03f, 0), 0.305f + (y > 0.5f ? 0 : 0.0f), Iron, Metal), new Ring(c + new Vector3(0, y + 0.03f, 0), 0.305f, Iron, Metal) }, 12, _w, Vector3.Backward);
        Circles.Add((c, 0.34f));
    }

    public void Crate(Vector3 c, float size = 0.7f, float yaw = 0)
    {
        var rot = Quaternion.CreateFromAxisAngle(Vector3.Up, yaw);
        Box(c + new Vector3(0, size * 0.5f, 0), new Vector3(size), Vary(Plank, 10), Wood, rot);
        foreach (var e in new[] { new Vector3(1, 1, 0), new Vector3(1, 0, 1), new Vector3(0, 1, 1) })
            for (int s1 = -1; s1 <= 1; s1 += 2) for (int s2 = -1; s2 <= 1; s2 += 2)
            {
                var off = e.X == 0 ? new Vector3(0, s1, s2) : e.Z == 0 ? new Vector3(s1, s2, 0) : new Vector3(s1, 0, s2);
                var sz = e.X == 0 ? new Vector3(size + 0.04f, 0.06f, 0.06f) : e.Z == 0 ? new Vector3(0.06f, 0.06f, size + 0.04f) : new Vector3(0.06f, size + 0.04f, 0.06f);
                Box(c + new Vector3(0, size * 0.5f, 0) + Vector3.Transform(off * size * 0.5f, rot), sz, PlankDark, Wood, rot);
            }
        Circles.Add((c, size * 0.75f));
    }
}
