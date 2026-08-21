using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CharacterModels;

public enum TreeStyle { Oak, Maple, Pine, Birch, Palm, Dead }

/// <summary>Global wind: a direction, a strength, and a travelling gust envelope so trees downwind react later.</summary>
public sealed class Wind
{
    public Vector3 Direction = Vector3.Normalize(new Vector3(0.8f, 0, 0.45f));
    public float Strength = 0.5f;        // 0 = still, 1 = fresh breeze, 2 = storm

    /// <summary>Slow gust envelope (0..1) at a world position; gusts sweep along the wind direction.</summary>
    public float Gust(float time, Vector3 pos)
    {
        float travel = Vector3.Dot(pos, Direction) * 0.35f;
        float t = time - travel;
        float g = 0.55f + 0.30f * MathF.Sin(t * 0.47f) + 0.15f * MathF.Sin(t * 1.13f + 1.3f) + 0.10f * MathF.Sin(t * 2.7f + 0.4f);
        return MathHelper.Clamp(g, 0, 1.2f);
    }
}

/// <summary>
/// A tree is a skinned mesh like a character: a trunk chain of bones with branch bones hanging off it.
/// Wind is animated by rotating those bones (big, slow sway) while the vertex shader adds a small
/// high-frequency flutter to vertices whose colour alpha is below 255 (foliage).
/// </summary>
public sealed class Tree
{
    public TreeStyle Style;
    public Vector3 Position;
    public float Yaw;
    public Skeleton Skeleton = null!;
    public VertexBuffer VertexBuffer = null!;
    public IndexBuffer IndexBuffer = null!;
    public int Triangles, Vertices;
    public Matrix World => Matrix.CreateRotationY(Yaw) * Matrix.CreateTranslation(Position);

    /// <summary>Per-bone sway parameters: how flexible the bone is and which chain depth it sits at.</summary>
    internal readonly List<(Bone bone, float flex, int depth, float phase)> Sway = new();
    internal float Phase;

    public void Update(float time, Wind wind)
    {
        // Wind in the tree's local frame (the mesh is yawed by World, bones are in bind/local space).
        var local = Vector3.TransformNormal(wind.Direction, Matrix.CreateRotationY(-Yaw));
        var bendAxis = Vector3.Cross(Vector3.Up, local);
        if (bendAxis.LengthSquared() < 1e-6f) bendAxis = Vector3.Right;
        bendAxis.Normalize();
        float gust = wind.Gust(time, Position);
        float strength = wind.Strength;

        foreach (var (bone, flex, depth, phase) in Sway)
        {
            // Lean downwind with the gust, plus an oscillation that gets faster and larger toward the tips.
            float freq = 1.1f + 0.45f * depth;
            float osc = 0.65f + 0.35f * MathF.Sin(time * freq + phase + Phase);
            float lean = strength * gust * flex * osc * 0.16f;
            // Cross-wind wobble (tips flail sideways a little).
            float wobble = strength * flex * 0.05f * MathF.Sin(time * (freq * 0.73f) + phase * 1.9f + Phase);
            bone.Rotation = Quaternion.CreateFromAxisAngle(bendAxis, lean) * Quaternion.CreateFromAxisAngle(local, wobble);
        }
        Skeleton.Update();
    }
}

/// <summary>Builds tree meshes of several styles. Everything is procedural: skeleton, bark, foliage, colours.</summary>
public static class TreeBuilder
{
    private static readonly Vector2 Bark = new(0.08f, 0.15f);
    private static readonly Vector2 Leaf = new(0.12f, 0.22f);
    private const byte LeafAlpha = 190;    // 255 = rigid; lower = more vertex flutter in the shader
    private const byte NeedleAlpha = 215;

    public static Tree Build(GraphicsDevice gd, TreeStyle style, int seed, float scale = 1f)
    {
        var rnd = new Random(seed);
        var tree = new Tree { Style = style, Phase = (float)rnd.NextDouble() * MathHelper.TwoPi };
        var sk = new Skeleton();
        sk.Add("root", null, Vector3.Zero, Vector3.Up * 0.1f);
        var mb = new MeshBuilder();

        switch (style)
        {
            case TreeStyle.Oak: BuildBroadleaf(mb, sk, tree, rnd, scale, OakPalette, crown: 1.0f, spread: 1.0f); break;
            case TreeStyle.Maple: BuildBroadleaf(mb, sk, tree, rnd, scale * 0.9f, MaplePalette, crown: 0.85f, spread: 1.15f); break;
            case TreeStyle.Pine: BuildPine(mb, sk, tree, rnd, scale); break;
            case TreeStyle.Birch: BuildBirch(mb, sk, tree, rnd, scale); break;
            case TreeStyle.Palm: BuildPalm(mb, sk, tree, rnd, scale); break;
            case TreeStyle.Dead: BuildDead(mb, sk, tree, rnd, scale); break;
        }

        sk.Update();
        tree.Skeleton = sk;
        (tree.VertexBuffer, tree.IndexBuffer) = mb.Upload(gd);
        tree.Triangles = mb.TriangleCount; tree.Vertices = mb.VertexCount;
        return tree;
    }

    // ----------------------------------------------------------------- palettes

    private sealed record Palette(Color Bark, Color BarkDark, Color[] Leaves);
    private static readonly Palette OakPalette = new(new Color(96, 72, 50), new Color(70, 52, 36),
        new[] { new Color(70, 120, 45), new Color(88, 138, 52), new Color(60, 104, 40), new Color(104, 150, 58) });
    private static readonly Palette MaplePalette = new(new Color(88, 66, 48), new Color(62, 46, 34),
        new[] { new Color(200, 88, 34), new Color(226, 140, 40), new Color(178, 54, 30), new Color(236, 178, 52) });
    private static readonly Palette BirchPalette = new(new Color(232, 228, 218), new Color(58, 54, 50),
        new[] { new Color(142, 178, 70), new Color(166, 196, 84), new Color(120, 160, 62) });
    private static readonly Palette PinePalette = new(new Color(78, 54, 38), new Color(56, 38, 28),
        new[] { new Color(34, 78, 48), new Color(42, 92, 54), new Color(28, 66, 42) });
    private static readonly Palette PalmPalette = new(new Color(120, 98, 70), new Color(84, 68, 48),
        new[] { new Color(74, 132, 52), new Color(92, 150, 60), new Color(64, 118, 46) });
    private static readonly Palette DeadPalette = new(new Color(92, 86, 80), new Color(60, 56, 52), Array.Empty<Color>());

    private static Color Pick(Random rnd, Color[] set) => set[rnd.Next(set.Length)];
    private static Color Vary(Random rnd, Color c, int amount)
    {
        int d = rnd.Next(-amount, amount + 1);
        return new Color(Math.Clamp(c.R + d, 0, 255), Math.Clamp(c.G + d, 0, 255), Math.Clamp(c.B + d, 0, 255), c.A);
    }
    private static float Rand(Random rnd, float min, float max) => min + (float)rnd.NextDouble() * (max - min);

    // ------------------------------------------------------------------ trunks

    /// <summary>Adds a chain of trunk bones along a slightly wandering path; returns the bone names and the path points.</summary>
    private static (string[] bones, Vector3[] path) TrunkChain(Skeleton sk, Tree tree, Random rnd, float height, int segments,
                                                               float wander, float flexTop)
    {
        var names = new string[segments];
        var pts = new Vector3[segments + 1];
        pts[0] = Vector3.Zero;
        var dir = Vector3.Up;
        string parent = "root";
        for (int i = 0; i < segments; i++)
        {
            float len = height / segments;
            dir = Vector3.Normalize(dir + new Vector3(Rand(rnd, -wander, wander), 0, Rand(rnd, -wander, wander)) * (i == 0 ? 0.3f : 1f));
            var tail = dir * len;
            names[i] = "t" + i;
            var b = sk.Add(names[i], parent, i == 0 ? Vector3.Zero : sk[parent].TailOffset, tail);
            pts[i + 1] = b.BindTail;
            // Bottom segment is stiff; flexibility grows toward the top.
            float f = MathHelper.Lerp(0.12f, flexTop, (i + 1) / (float)segments);
            tree.Sway.Add((b, f, i, Rand(rnd, 0, MathHelper.TwoPi)));
            parent = names[i];
        }
        return (names, pts);
    }

    /// <summary>Tapered bark tube along a path, 3 rings per segment for smooth bends.</summary>
    private static void TrunkMesh(MeshBuilder mb, Vector3[] path, float r0, float r1, Palette pal, Random rnd, Weighter w,
                                  int sides = 9, float flare = 1.35f, Func<int, int, Color>? ringColor = null, int ringsPerSegment = 3)
    {
        var rings = new List<Ring>();
        int n = path.Length - 1;
        int ringIndex = 0;
        for (int i = 0; i < n; i++)
        {
            for (int k = 0; k < ringsPerSegment; k++)
            {
                if (i > 0 && k == 0) continue;
                float t = (i + k / (float)ringsPerSegment) / n;
                var c = Vector3.Lerp(path[i], path[i + 1], k / (float)ringsPerSegment);
                float r = MathHelper.Lerp(r0, r1, MathF.Pow(t, 0.8f));
                if (t < 0.12f) r *= MathHelper.Lerp(flare, 1f, t / 0.12f);   // root flare
                var col = ringColor?.Invoke(ringIndex, (int)(t * 1000)) ?? Vary(rnd, pal.Bark, 10);
                rings.Add(new Ring(c, r * Rand(rnd, 0.95f, 1.05f), r * Rand(rnd, 0.95f, 1.05f), col, Bark));
                ringIndex++;
            }
        }
        rings.Add(new Ring(path[n], r1 * 0.8f, pal.Bark, Bark));
        mb.Loft(rings, sides, w, Vector3.Backward, capEnd: true, capSteps: 2);
    }

    /// <summary>A branch bone (plus optional child bone) leaving the trunk at a path point; returns the tip position.</summary>
    private static (Vector3 tip, string boneName) Branch(Skeleton sk, Tree tree, Random rnd, string parentBone, Vector3 origin,
                                                         Vector3 dir, float length, int depth,
                                                         float flex, float droop = 0f)
    {
        string name = $"b{sk.Count}";
        var parent = sk[parentBone];
        var mid = dir * length * 0.55f;
        var b = sk.Add(name, parentBone, origin - parent.BindHead, mid);
        tree.Sway.Add((b, flex, depth, Rand(rnd, 0, MathHelper.TwoPi)));
        // Second half bends a little downward (droop) or upward, with its own bone for whippier tips.
        var dir2 = Vector3.Normalize(dir + Vector3.Down * droop + new Vector3(Rand(rnd, -0.15f, 0.15f), Rand(rnd, -0.1f, 0.2f), Rand(rnd, -0.15f, 0.15f)));
        var tipOff = dir2 * length * 0.45f;
        var b2 = sk.Add(name + "e", name, mid, tipOff);
        tree.Sway.Add((b2, flex * 1.6f, depth + 1, Rand(rnd, 0, MathHelper.TwoPi)));
        var tip = b2.BindTail;
        return (tip, name + "e");
    }

    /// <summary>Lumpy foliage blob: an ellipsoid with a per-direction radius modulation so it reads as a mass of leaves.</summary>
    private static void LeafBlob(MeshBuilder mb, Vector3 center, Vector3 radii, Color color, Random rnd, Weighter w, float lump = 0.14f,
                                 byte alpha = LeafAlpha, int segs = 12, int stacks = 8)
    {
        float a = Rand(rnd, 0, 10), b = Rand(rnd, 0, 10), c = Rand(rnd, 0, 10);
        // Fake occlusion: the underside of a leaf mass is darker and bluer than the sunlit top.
        var shade = new Color((int)(color.R * 0.42f), (int)(color.G * 0.48f), (int)(color.B * 0.55f));
        mb.Ellipsoid(center, radii, segs, stacks, color, Leaf, w,
            d => 1f + lump * (MathF.Sin(d.X * 5.3f + a) * MathF.Sin(d.Y * 4.1f + b) * MathF.Sin(d.Z * 6.2f + c))
                    + lump * 0.5f * MathF.Sin(d.X * 11f + d.Z * 9f + c)
                    - 0.08f * MathF.Max(0, -d.Y),   // flatter underside
            alpha: alpha,
            colorFn: d => Color.Lerp(shade, color, MathHelper.Clamp(d.Y * 0.8f + 0.55f, 0, 1)));
    }

    // ------------------------------------------------------------------ styles

    private static void BuildBroadleaf(MeshBuilder mb, Skeleton sk, Tree tree, Random rnd, float s, Palette pal, float crown, float spread)
    {
        float height = Rand(rnd, 2.0f, 2.6f) * s;
        var (trunkBones, path) = TrunkChain(sk, tree, rnd, height, 4, 0.12f, 0.35f);

        int branches = rnd.Next(5, 8);
        var tips = new List<Vector3>();
        var bones = new List<string> { "root" }; bones.AddRange(trunkBones);
        float yaw0 = Rand(rnd, 0, MathHelper.TwoPi);
        for (int i = 0; i < branches; i++)
        {
            float t = Rand(rnd, 0.45f, 0.92f);
            int seg = Math.Min((int)(t * 4), 3);
            var origin = Vector3.Lerp(path[seg], path[seg + 1], t * 4 - seg);
            float yaw = yaw0 + i * MathHelper.TwoPi / branches + Rand(rnd, -0.35f, 0.35f);
            float pitch = Rand(rnd, 0.35f, 0.95f) * (1.1f - t * 0.5f);
            var dir = Vector3.Normalize(new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch) * spread, MathF.Sin(pitch), MathF.Sin(yaw) * MathF.Cos(pitch) * spread));
            float len = Rand(rnd, 0.9f, 1.4f) * s * (0.8f + 0.4f * (1 - t));
            var (tip, _) = Branch(sk, tree, rnd, trunkBones[seg], origin, dir, len, 1, 0.55f, droop: 0.15f);
            tips.Add(tip);
        }
        // Crown bone: the trunk top carries the central foliage mass.
        var all = new List<string>(); foreach (var b in sk.Bones) all.Add(b.Name);
        var w = new Weighter(sk, 2.2f, all.ToArray());

        // Meshes need the weighter, so geometry is emitted after the whole skeleton exists.
        TrunkMesh(mb, path, 0.19f * s, 0.09f * s, pal, rnd, w, sides: 10);
        RebuildBranchMeshes(mb, sk, tree, rnd, pal, w, 0.075f * s);

        var top = path[^1];
        foreach (var tip in tips)
        {
            var r = new Vector3(Rand(rnd, 0.6f, 0.8f), Rand(rnd, 0.45f, 0.6f), Rand(rnd, 0.6f, 0.8f)) * s * crown;
            LeafBlob(mb, tip + new Vector3(0, r.Y * 0.2f, 0), r, Vary(rnd, Pick(rnd, pal.Leaves), 8), rnd, w, lump: 0.17f);
            // A smaller satellite cluster hanging off the side breaks the balloon silhouette.
            var off = new Vector3(Rand(rnd, -0.4f, 0.4f), Rand(rnd, -0.3f, 0.1f), Rand(rnd, -0.4f, 0.4f)) * s;
            LeafBlob(mb, tip + off, r * Rand(rnd, 0.5f, 0.7f), Vary(rnd, Pick(rnd, pal.Leaves), 10), rnd, w, lump: 0.2f, segs: 10, stacks: 6);
        }
        var cr = new Vector3(1.0f, 0.72f, 1.0f) * s * crown;
        LeafBlob(mb, top + new Vector3(0, 0.3f * s, 0), cr, Vary(rnd, Pick(rnd, pal.Leaves), 6), rnd, w, lump: 0.14f, segs: 16, stacks: 10);
        for (int i = 0; i < 4; i++)
        {
            var off = new Vector3(Rand(rnd, -0.7f, 0.7f), Rand(rnd, -0.1f, 0.55f), Rand(rnd, -0.7f, 0.7f)) * s;
            LeafBlob(mb, top + off, cr * Rand(rnd, 0.5f, 0.72f), Vary(rnd, Pick(rnd, pal.Leaves), 8), rnd, w, lump: 0.18f);
        }
    }

    /// <summary>Branch bones were added before the weighter existed; their bark tubes are lofted here from the bone geometry.</summary>
    private static void RebuildBranchMeshes(MeshBuilder mb, Skeleton sk, Tree tree, Random rnd, Palette pal, Weighter w, float radius, int sides = 6)
    {
        foreach (var b in sk.Bones)
        {
            if (!b.Name.StartsWith("b") || b.Name.EndsWith("e")) continue;
            var end = sk[b.Name + "e"];
            var dir = Vector3.Normalize(b.TailOffset);
            var origin = b.BindHead;
            var tip = end.BindTail;
            var rings = new[]
            {
                new Ring(origin - dir * radius * 1.5f, radius * 1.1f, Vary(rnd, pal.BarkDark, 8), Bark),
                new Ring(Vector3.Lerp(origin, b.BindTail, 0.35f), radius * 0.85f, Vary(rnd, pal.Bark, 8), Bark),
                new Ring(b.BindTail, radius * 0.6f, Vary(rnd, pal.Bark, 8), Bark),
                new Ring(Vector3.Lerp(b.BindTail, tip, 0.55f), radius * 0.38f, Vary(rnd, pal.Bark, 8), Bark),
                new Ring(tip, radius * 0.18f, pal.Bark, Bark)
            };
            mb.Loft(rings, sides, w, Vector3.Up, capEnd: true, capSteps: 1);
        }
    }

    private static void BuildPine(MeshBuilder mb, Skeleton sk, Tree tree, Random rnd, float s)
    {
        var pal = PinePalette;
        float height = Rand(rnd, 3.6f, 4.6f) * s;
        int segs = 5;
        var (trunkBones, path) = TrunkChain(sk, tree, rnd, height, segs, 0.035f, 0.45f);
        var all = new List<string>(); foreach (var b in sk.Bones) all.Add(b.Name);
        var w = new Weighter(sk, 2.5f, all.ToArray());
        TrunkMesh(mb, path, 0.16f * s, 0.05f * s, pal, rnd, w, sides: 9, flare: 1.25f);

        // Stacked conical tiers, each lumpy and drooping at the rim. Lower tiers are wider.
        int tiers = rnd.Next(6, 8);
        float yBase = height * 0.22f;
        for (int i = 0; i < tiers; i++)
        {
            float t = i / (float)(tiers - 1);
            float top = MathHelper.Lerp(yBase, height + 0.15f * s, MathF.Pow(t, 0.9f)) + Rand(rnd, -0.05f, 0.05f) * s;
            float radius = MathHelper.Lerp(1.25f, 0.22f, t) * s * Rand(rnd, 0.9f, 1.1f);
            float h = MathHelper.Lerp(1.1f, 0.55f, t) * s;
            int lobes = rnd.Next(5, 8);
            float ph = Rand(rnd, 0, MathHelper.TwoPi);
            var col = Vary(rnd, Pick(rnd, pal.Leaves), 6);
            var dark = new Color((int)(col.R * 0.75f), (int)(col.G * 0.75f), (int)(col.B * 0.75f));
            // Sample the trunk path so tiers follow the trunk's lean.
            float tp = MathHelper.Clamp(top / height, 0, 1) * segs;
            int si = Math.Min((int)tp, segs - 1);
            var axis = Vector3.Lerp(path[si], path[si + 1], tp - si);
            Vector3 Outer(float u, float v)
            {
                float th = u * MathHelper.TwoPi;
                float lump = 1f + 0.22f * MathF.Sin(th * lobes + ph) * v + 0.06f * MathF.Sin(th * lobes * 3 + ph * 2) * v;
                float r = radius * MathF.Pow(v, 0.85f) * lump;
                float y = top - v * h - 0.35f * h * MathF.Pow(v, 3) * (0.5f + 0.5f * MathF.Sin(th * lobes + ph));
                return new Vector3(axis.X + MathF.Sin(th) * r, y, axis.Z + MathF.Cos(th) * r);
            }
            var tipCol = new Color(Math.Min(255, col.R + 50), Math.Min(255, col.G + 60), Math.Min(255, col.B + 30));
            mb.Parametric(20, 6, Outer, (u, v) => Color.Lerp(tipCol, v > 0.85f ? dark : col, MathHelper.Clamp(v * 3f, 0, 1)), Leaf, w, alpha: NeedleAlpha);
            // Underside: rim inward to the trunk, darker.
            Vector3 Under(float u, float v)
            {
                var rim = Outer(u, 1f);
                var centre = new Vector3(axis.X, top - h + 0.3f * h, axis.Z);
                return Vector3.Lerp(rim, centre, v);
            }
            mb.Parametric(20, 1, Under, (u, v) => dark, Leaf, w, alpha: NeedleAlpha);
        }
    }

    private static void BuildBirch(MeshBuilder mb, Skeleton sk, Tree tree, Random rnd, float s)
    {
        var pal = BirchPalette;
        float height = Rand(rnd, 2.9f, 3.6f) * s;
        var (trunkBones, path) = TrunkChain(sk, tree, rnd, height, 5, 0.08f, 0.6f);
        int branches = rnd.Next(6, 9);
        var tips = new List<Vector3>();
        float yaw0 = Rand(rnd, 0, MathHelper.TwoPi);
        for (int i = 0; i < branches; i++)
        {
            float t = Rand(rnd, 0.4f, 0.95f);
            int seg = Math.Min((int)(t * 5), 4);
            var origin = Vector3.Lerp(path[seg], path[seg + 1], t * 5 - seg);
            float yaw = yaw0 + i * MathHelper.TwoPi / branches + Rand(rnd, -0.4f, 0.4f);
            float pitch = Rand(rnd, 0.5f, 1.1f);
            var dir = Vector3.Normalize(new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Sin(yaw) * MathF.Cos(pitch)));
            float len = Rand(rnd, 0.7f, 1.1f) * s;
            var (tip, _) = Branch(sk, tree, rnd, trunkBones[seg], origin, dir, len, 1, 0.9f, droop: 0.6f);
            tips.Add(tip);
        }
        var all = new List<string>(); foreach (var b in sk.Bones) all.Add(b.Name);
        var w = new Weighter(sk, 2.5f, all.ToArray());
        // Banded white bark: every few rings a dark band.
        int bandSeed = rnd.Next(100);
        TrunkMesh(mb, path, 0.11f * s, 0.035f * s, pal, rnd, w, sides: 9, flare: 1.2f,
            ringColor: (ri, _) => ((ri + bandSeed) % 5 < 2) ? Vary(rnd, pal.BarkDark, 12) : Vary(rnd, pal.Bark, 8), ringsPerSegment: 7);
        RebuildBranchMeshes(mb, sk, tree, rnd, new Palette(new Color(200, 196, 186), new Color(90, 86, 80), pal.Leaves), w, 0.04f * s, sides: 5);
        foreach (var tip in tips)
        {
            // Small drooping clusters, several per branch tip, brighter and floatier than oak foliage.
            int n = rnd.Next(2, 4);
            for (int k = 0; k < n; k++)
            {
                var off = new Vector3(Rand(rnd, -0.25f, 0.25f), Rand(rnd, -0.35f, 0.05f), Rand(rnd, -0.25f, 0.25f)) * s;
                var r = new Vector3(Rand(rnd, 0.28f, 0.4f), Rand(rnd, 0.3f, 0.45f), Rand(rnd, 0.28f, 0.4f)) * s;
                LeafBlob(mb, tip + off, r, Vary(rnd, Pick(rnd, pal.Leaves), 10), rnd, w, lump: 0.18f, alpha: 150, segs: 10, stacks: 6);
            }
        }
        var top = path[^1];
        LeafBlob(mb, top + new Vector3(0, 0.1f * s, 0), new Vector3(0.55f, 0.6f, 0.55f) * s, Vary(rnd, Pick(rnd, pal.Leaves), 6), rnd, w, lump: 0.16f, alpha: 150);
    }

    private static void BuildPalm(MeshBuilder mb, Skeleton sk, Tree tree, Random rnd, float s)
    {
        var pal = PalmPalette;
        float height = Rand(rnd, 3.2f, 4.2f) * s;
        // A curved trunk: wander is one-directional so it arcs rather than wiggles.
        var names = new string[6]; var path = new Vector3[7];
        var lean = Vector3.Normalize(new Vector3(Rand(rnd, -1, 1), 0, Rand(rnd, -1, 1))) * Rand(rnd, 0.04f, 0.1f);
        var dir = Vector3.Up; string parent = "root";
        for (int i = 0; i < 6; i++)
        {
            dir = Vector3.Normalize(dir + lean);
            names[i] = "t" + i;
            var b = sk.Add(names[i], parent, i == 0 ? Vector3.Zero : sk[parent].TailOffset, dir * height / 6);
            path[i + 1] = b.BindTail;
            tree.Sway.Add((b, MathHelper.Lerp(0.08f, 0.3f, i / 5f), i, Rand(rnd, 0, MathHelper.TwoPi)));
            parent = names[i];
        }
        var crown = path[6];
        int fronds = rnd.Next(9, 13);
        var frondBones = new List<(string name, Vector3 dir, float len)>();
        float yaw0 = Rand(rnd, 0, MathHelper.TwoPi);
        for (int i = 0; i < fronds; i++)
        {
            float yaw = yaw0 + i * MathHelper.TwoPi / fronds + Rand(rnd, -0.2f, 0.2f);
            float pitch = Rand(rnd, -0.25f, 0.55f);
            var d = Vector3.Normalize(new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Sin(yaw) * MathF.Cos(pitch)));
            float len = Rand(rnd, 1.6f, 2.2f) * s;
            string n = "f" + i;
            var b = sk.Add(n, names[5], sk[names[5]].TailOffset, d * len * 0.5f);
            tree.Sway.Add((b, 1.1f, 6, Rand(rnd, 0, MathHelper.TwoPi)));
            var b2 = sk.Add(n + "e", n, d * len * 0.5f, (d + Vector3.Down * 0.6f) * len * 0.5f);
            tree.Sway.Add((b2, 1.8f, 7, Rand(rnd, 0, MathHelper.TwoPi)));
            frondBones.Add((n, d, len));
        }
        var all = new List<string>(); foreach (var b in sk.Bones) all.Add(b.Name);
        var w = new Weighter(sk, 3f, all.ToArray());

        // Ringed trunk: alternate ring radii to suggest leaf scars.
        var rings = new List<Ring>();
        for (int i = 0; i <= 18; i++)
        {
            float t = i / 18f; float seg = t * 6; int si = Math.Min((int)seg, 5);
            var c = Vector3.Lerp(path[si], path[si + 1], seg - si);
            float r = MathHelper.Lerp(0.17f, 0.11f, t) * s * ((i & 1) == 0 ? 1f : 0.86f) * (t < 0.1f ? 1.25f : 1f);
            rings.Add(new Ring(c, r, Vary(rnd, (i & 1) == 0 ? pal.Bark : pal.BarkDark, 8), Bark));
        }
        rings.Add(new Ring(crown + Vector3.Up * 0.12f * s, 0.14f * s, pal.BarkDark, Bark));
        mb.Loft(rings, 10, w, Vector3.Backward, capEnd: true, capSteps: 2);
        // Coconuts.
        for (int i = 0; i < 3; i++)
            mb.Ellipsoid(crown + new Vector3(Rand(rnd, -0.14f, 0.14f), -0.05f, Rand(rnd, -0.14f, 0.14f)) * s, new Vector3(0.09f) * s, 8, 6, new Color(110, 84, 40), Mat.Wood, w);

        foreach (var (n, d, len) in frondBones)
        {
            var col = Vary(rnd, Pick(rnd, pal.Leaves), 8);
            var side = Vector3.Normalize(Vector3.Cross(d, Vector3.Up));
            float width = Rand(rnd, 0.36f, 0.48f) * s;
            float droop = Rand(rnd, 0.5f, 0.8f);
            int leaflets = 9;
            Vector3 Frond(float u, float v)
            {
                // Spine arcs outward then droops; leaflets are a zig-zag in width along v.
                var spine = crown + d * (len * v) + Vector3.Down * (droop * len * v * v);
                float taper = MathF.Sin(v * MathHelper.Pi * 0.9f + 0.1f) * (1f - 0.35f * v);
                float serr = 0.75f + 0.25f * MathF.Abs(MathF.Sin(v * MathHelper.Pi * leaflets));
                float half = width * taper * serr;
                float x = (u - 0.5f) * 2f;
                // Leaflets angle downward from the midrib (V-shaped cross section).
                return spine + side * (x * half) + Vector3.Down * (MathF.Abs(x) * half * 0.6f);
            }
            mb.Parametric(6, 22, Frond, (u, v) => MathF.Abs(u - 0.5f) < 0.1f ? new Color(col.R * 3 / 4, col.G * 3 / 4, col.B * 3 / 4) : col, Leaf, w, closedU: false, doubleSided: true, alpha: 200);
        }
    }

    private static void BuildDead(MeshBuilder mb, Skeleton sk, Tree tree, Random rnd, float s)
    {
        var pal = DeadPalette;
        float height = Rand(rnd, 2.4f, 3.2f) * s;
        var (trunkBones, path) = TrunkChain(sk, tree, rnd, height, 4, 0.16f, 0.25f);
        // Gnarled, forking branches: two levels, no foliage.
        float yaw0 = Rand(rnd, 0, MathHelper.TwoPi);
        int branches = rnd.Next(4, 7);
        var level1 = new List<(string bone, Vector3 tip, Vector3 dir)>();
        for (int i = 0; i < branches; i++)
        {
            float t = Rand(rnd, 0.35f, 0.95f);
            int seg = Math.Min((int)(t * 4), 3);
            var origin = Vector3.Lerp(path[seg], path[seg + 1], t * 4 - seg);
            float yaw = yaw0 + i * MathHelper.TwoPi / branches + Rand(rnd, -0.5f, 0.5f);
            float pitch = Rand(rnd, 0.2f, 1.0f);
            var dir = Vector3.Normalize(new Vector3(MathF.Cos(yaw) * MathF.Cos(pitch), MathF.Sin(pitch), MathF.Sin(yaw) * MathF.Cos(pitch)));
            float len = Rand(rnd, 0.8f, 1.5f) * s;
            var (tip, bone) = Branch(sk, tree, rnd, trunkBones[seg], origin, dir, len, 1, 0.35f, droop: -0.1f);
            level1.Add((bone, tip, dir));
        }
        var twigs = new List<(Vector3 a, Vector3 b, string bone)>();
        foreach (var (bone, tip, dir) in level1)
        {
            int n = rnd.Next(2, 4);
            for (int k = 0; k < n; k++)
            {
                var d2 = Vector3.Normalize(dir + new Vector3(Rand(rnd, -0.8f, 0.8f), Rand(rnd, 0.1f, 0.9f), Rand(rnd, -0.8f, 0.8f)));
                float len = Rand(rnd, 0.35f, 0.7f) * s;
                var start = Vector3.Lerp(sk[bone].BindHead, tip, Rand(rnd, 0.2f, 1f));
                string tn = $"w{sk.Count}";
                var tb = sk.Add(tn, bone, start - sk[bone].BindHead, d2 * len);
                tree.Sway.Add((tb, 0.7f, 3, Rand(rnd, 0, MathHelper.TwoPi)));
                twigs.Add((start, tb.BindTail, tn));
            }
        }
        var all = new List<string>(); foreach (var b in sk.Bones) all.Add(b.Name);
        var w = new Weighter(sk, 2.2f, all.ToArray());
        TrunkMesh(mb, path, 0.2f * s, 0.07f * s, pal, rnd, w, sides: 9, flare: 1.5f,
            ringColor: (ri, _) => Vary(rnd, ri % 3 == 0 ? pal.BarkDark : pal.Bark, 14));
        RebuildBranchMeshes(mb, sk, tree, rnd, pal, w, 0.06f * s, sides: 6);
        foreach (var (a, b, _) in twigs)
        {
            var d = Vector3.Normalize(b - a);
            mb.Loft(new[]
            {
                new Ring(a - d * 0.03f * s, 0.028f * s, Vary(rnd, pal.BarkDark, 10), Bark),
                new Ring(Vector3.Lerp(a, b, 0.5f), 0.018f * s, Vary(rnd, pal.Bark, 10), Bark),
                new Ring(b, 0.006f * s, pal.Bark, Bark)
            }, 5, w, Vector3.Up, capEnd: true, capSteps: 1);
        }
        // A stump-ish broken top.
        mb.Ellipsoid(path[^1], new Vector3(0.08f, 0.05f, 0.08f) * s, 8, 4, pal.BarkDark, Bark, w);
    }
}
