using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CharacterModels;

/// <summary>
/// Ground cover generated into one static mesh: grass blades, a tall golden field band, wildflowers in colour patches
/// and low bushes. Nothing here is skinned or updated on the CPU — wind, travelling gusts and trampling by the
/// player are all done in the vertex shader, driven by a per-vertex "bend weight" stored in colour alpha
/// (0 = rigid root … 127 = whippy tip; values ≥ 128 are the foliage flutter range used by the trees).
/// </summary>
public sealed class Vegetation
{
    public VertexBuffer VertexBuffer = null!;
    public IndexBuffer IndexBuffer = null!;
    public int Triangles, Blades, Flowers;
    public readonly List<(Vector3 pos, float radius)> Bushes = new();

    private readonly List<SkinnedVertex> _v = new();
    private readonly List<int> _i = new();
    private static readonly Vector2 GrassMat = new(0.05f, 0.12f);

    /// <summary>Bend weight → colour alpha. 0 = rigid, 1 = full bend (tips).</summary>
    private static byte Bend(float w) => (byte)MathHelper.Clamp(w * 127f, 0f, 127f);

    public sealed class Options
    {
        public float Extent = 13.6f;            // ground half-size
        public float PlazaRadius = 5.9f;        // keep the paving clear
        public float BladesPerM2 = 38f;
        public float FlowersPerM2 = 1.1f;
        public int Bushes = 10;
        /// <summary>A tall, golden field occupies the band where this returns true (world xz).</summary>
        public Func<float, float, bool> IsField = (x, z) => z < -8.5f;
        /// <summary>Places to leave bare (tree trunks), as (x, z, radius).</summary>
        public List<(float x, float z, float r)> Keepouts = new();
    }

    public static Vegetation Build(GraphicsDevice gd, int seed, Options o)
    {
        var veg = new Vegetation();
        var rnd = new Random(seed);
        float Rand(float a, float b) => a + (float)rnd.NextDouble() * (b - a);

        bool Clear(float x, float z, float margin)
        {
            if (x * x + z * z < (o.PlazaRadius + margin) * (o.PlazaRadius + margin)) return false;
            if (MathF.Abs(x) > o.Extent || MathF.Abs(z) > o.Extent) return false;
            foreach (var (kx, kz, kr) in o.Keepouts)
                if ((x - kx) * (x - kx) + (z - kz) * (z - kz) < (kr + margin) * (kr + margin)) return false;
            return true;
        }

        // ---- Bushes first so grass can thin out under them.
        var bushes = new List<(Vector3 c, float r)>();
        int tries = 0;
        while (bushes.Count < o.Bushes && tries++ < 500)
        {
            float x = Rand(-o.Extent, o.Extent), z = Rand(-o.Extent, o.Extent);
            if (!Clear(x, z, 1.2f) || o.IsField(x, z)) continue;
            bool ok = true;
            foreach (var b in bushes) if (Vector3.DistanceSquared(b.c, new Vector3(x, 0, z)) < 9f) { ok = false; break; }
            if (!ok) continue;
            float r = Rand(0.45f, 0.8f);
            bushes.Add((new Vector3(x, 0, z), r));
            veg.AddBush(new Vector3(x, 0, z), r, rnd);
        }
        veg.Bushes.AddRange(bushes.ConvertAll(b => (b.c, b.r * 0.85f)));

        // ---- Grass: density modulated by low-frequency noise so it grows in clumps, not a uniform carpet.
        float area = (2 * o.Extent) * (2 * o.Extent);
        int bladeCount = (int)(area * o.BladesPerM2);
        for (int n = 0; n < bladeCount; n++)
        {
            float x = Rand(-o.Extent, o.Extent), z = Rand(-o.Extent, o.Extent);
            if (!Clear(x, z, 0.15f)) continue;
            bool field = o.IsField(x, z);
            float clump = Noise(x * 0.45f, z * 0.45f) * 0.6f + Noise(x * 1.7f, z * 1.7f) * 0.4f;     // 0..1
            if (!field && rnd.NextDouble() > 0.25 + clump * 0.9) continue;
            foreach (var b in bushes) if (Vector3.DistanceSquared(b.c, new Vector3(x, 0, z)) < b.r * b.r * 0.6f) goto next;

            float h = field ? Rand(0.55f, 0.9f) : Rand(0.14f, 0.34f) * (0.7f + 0.6f * clump);
            float w = field ? Rand(0.02f, 0.03f) : Rand(0.025f, 0.045f);
            float hue = Noise(x * 0.3f + 7f, z * 0.3f + 3f);
            Color tip, root;
            if (field)
            {
                tip = Lerp(new Color(214, 178, 82), new Color(232, 204, 118), hue);
                root = new Color(120, 112, 54);
            }
            else
            {
                tip = Lerp(new Color(96, 156, 58), new Color(136, 178, 70), hue);
                root = Lerp(new Color(44, 82, 30), new Color(58, 98, 36), hue);
            }
            veg.AddBlade(new Vector3(x, 0, z), h, w, Rand(0, MathHelper.TwoPi), root, tip, field ? 0.75f : 1f);
            // Field: a seed head on top of most stalks.
            if (field && rnd.NextDouble() < 0.7f) veg.AddSeedHead(new Vector3(x, h, z), Rand(0.05f, 0.08f), Lerp(new Color(196, 160, 70), new Color(226, 200, 120), hue), Rand(0, MathHelper.Pi));
            next:;
        }

        // ---- Wildflowers in colour patches: each patch picks one species.
        (Color petal, Color centre, float size)[] species =
        {
            (new Color(250, 246, 230), new Color(240, 200, 60), 0.05f),    // daisy
            (new Color(240, 200, 50), new Color(160, 110, 30), 0.045f),    // buttercup
            (new Color(225, 60, 50), new Color(40, 30, 30), 0.055f),       // poppy
            (new Color(150, 90, 200), new Color(250, 230, 120), 0.04f),    // cornflower/violet
            (new Color(90, 120, 230), new Color(250, 240, 200), 0.04f),    // bluebell
        };
        int flowerCount = (int)(area * o.FlowersPerM2);
        for (int n = 0; n < flowerCount; n++)
        {
            float x = Rand(-o.Extent, o.Extent), z = Rand(-o.Extent, o.Extent);
            if (!Clear(x, z, 0.3f) || o.IsField(x, z)) continue;
            float patch = Noise(x * 0.5f + 31f, z * 0.5f + 17f);
            if (rnd.NextDouble() > MathF.Pow(patch, 2.2f) * 1.6f) continue;             // flowers only where the patch noise peaks
            var sp = species[(int)(Noise(x * 0.11f + 5f, z * 0.11f + 9f) * species.Length * 0.999f)];
            float stem = Rand(0.18f, 0.32f);
            veg.AddFlower(new Vector3(x, 0, z), stem, sp.size * Rand(0.8f, 1.25f), sp.petal, sp.centre, Rand(0, MathHelper.TwoPi));
        }

        (veg.VertexBuffer, veg.IndexBuffer) = veg.Upload(gd);
        veg.Triangles = veg._i.Count / 3;
        return veg;
    }

    // ------------------------------------------------------------------ pieces

    /// <summary>One blade = a tapered quad (root two verts, a mid pair, a tip), both windings so it is visible from either side.</summary>
    private void AddBlade(Vector3 root, float height, float width, float yaw, Color rootCol, Color tipCol, float bendScale)
    {
        var side = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)) * width * 0.5f;
        var lean = new Vector3(-MathF.Sin(yaw), 0, MathF.Cos(yaw)) * height * 0.18f;   // a natural curl away from the face
        var n = Vector3.Normalize(new Vector3(-MathF.Sin(yaw), 1.6f, MathF.Cos(yaw)));   // up-biased normal: lit like the ground, not like a wall
        int b = _v.Count;
        var mid = Lerp(rootCol, tipCol, 0.5f);
        _v.Add(V(root - side, n, rootCol, 0));
        _v.Add(V(root + side, n, rootCol, 0));
        _v.Add(V(root - side * 0.7f + new Vector3(0, height * 0.55f, 0) + lean * 0.4f, n, mid, 0.35f * bendScale));
        _v.Add(V(root + side * 0.7f + new Vector3(0, height * 0.55f, 0) + lean * 0.4f, n, mid, 0.35f * bendScale));
        _v.Add(V(root + new Vector3(0, height, 0) + lean, n, tipCol, 1f * bendScale));
        Quad(b, b + 1, b + 3, b + 2, both: true);
        Tri(b + 2, b + 3, b + 4, both: true);
        Blades++;
    }

    private void AddSeedHead(Vector3 top, float len, Color col, float yaw)
    {
        // A small slanted diamond above the stalk tip (two triangles each way), yawed per stalk so they never all go edge-on.
        var n = Vector3.Up;
        int b = _v.Count;
        var w = new Vector3(MathF.Cos(yaw), 0, MathF.Sin(yaw)) * len * 0.35f;
        _v.Add(V(top, n, col * 0.85f, 1f * 0.75f));
        _v.Add(V(top - w + new Vector3(0, len * 0.5f, 0), n, col, 0.8f));
        _v.Add(V(top + w + new Vector3(0, len * 0.5f, 0), n, col, 0.8f));
        _v.Add(V(top + new Vector3(0, len, 0), n, col, 0.85f));
        Quad(b, b + 1, b + 3, b + 2, both: true);
    }

    private void AddFlower(Vector3 root, float stemH, float size, Color petal, Color centre, float yaw)
    {
        // Stem: a thin blade in stem green.
        AddBlade(root, stemH, 0.012f, yaw, new Color(50, 96, 36), new Color(70, 122, 48), 0.8f);
        Blades--;
        // Head: a flat 6-petal rosette slightly tilted toward the light, plus a raised centre disc.
        var top = root + new Vector3(0, stemH, 0);
        var n = Vector3.Normalize(new Vector3(0.15f, 1f, -0.2f));
        int c = _v.Count;
        _v.Add(V(top + new Vector3(0, 0.004f, 0), n, centre, 0.8f));
        const int petals = 6;
        for (int k = 0; k < petals; k++)
        {
            float a0 = yaw + k * MathHelper.TwoPi / petals, a1 = a0 + MathHelper.TwoPi / petals;
            var p0 = top + new Vector3(MathF.Cos(a0), 0.05f, MathF.Sin(a0)) * size;
            var p1 = top + new Vector3(MathF.Cos(a1), 0.05f, MathF.Sin(a1)) * size;
            var pm = top + new Vector3(MathF.Cos((a0 + a1) * 0.5f), -0.1f, MathF.Sin((a0 + a1) * 0.5f)) * size * 1.25f;
            int b = _v.Count;
            var dark = petal * 0.8f; dark.A = 255;
            _v.Add(V(p0, n, dark, 0.8f)); _v.Add(V(pm, n, petal, 0.85f)); _v.Add(V(p1, n, dark, 0.8f));
            Tri(c, b, b + 1, both: true); Tri(c, b + 1, b + 2, both: true);
        }
        // Centre disc: a small raised quad.
        int d = _v.Count; float r = size * 0.3f;
        _v.Add(V(top + new Vector3(-r, 0.012f, -r), n, centre, 0.8f)); _v.Add(V(top + new Vector3(r, 0.012f, -r), n, centre, 0.8f));
        _v.Add(V(top + new Vector3(r, 0.012f, r), n, centre, 0.8f)); _v.Add(V(top + new Vector3(-r, 0.012f, r), n, centre, 0.8f));
        Quad(d, d + 1, d + 2, d + 3, both: true);
        Flowers++;
    }

    private void AddBush(Vector3 c, float r, Random rnd)
    {
        // A cluster of overlapping lumpy domes built with the same trick as tree foliage, but flattened and darker.
        int lobes = 4 + rnd.Next(3);
        var baseCol = new Color(58 + rnd.Next(-8, 9), 112 + rnd.Next(-10, 11), 46 + rnd.Next(-8, 9));
        for (int k = 0; k < lobes; k++)
        {
            float a = (float)rnd.NextDouble() * MathHelper.TwoPi;
            float d = k == 0 ? 0f : r * (0.3f + (float)rnd.NextDouble() * 0.5f);
            var lc = c + new Vector3(MathF.Cos(a) * d, 0, MathF.Sin(a) * d);
            float lr = k == 0 ? r : r * (0.5f + (float)rnd.NextDouble() * 0.4f);
            float seed1 = (float)rnd.NextDouble() * 10, seed2 = (float)rnd.NextDouble() * 10;
            Dome(lc, new Vector3(lr, lr * 0.8f, lr), baseCol, seed1, seed2, 12, 5);
        }
    }

    /// <summary>Upper half of a lumpy ellipsoid with a baked dark underside gradient and foliage flutter weight.</summary>
    private void Dome(Vector3 c, Vector3 radii, Color col, float s1, float s2, int segs, int stacks)
    {
        var shade = new Color((int)(col.R * 0.45f), (int)(col.G * 0.5f), (int)(col.B * 0.55f));
        int b = _v.Count;
        for (int st = 0; st <= stacks; st++)
        {
            float phi = st / (float)stacks * MathHelper.PiOver2 * 1.15f;      // a bit past the equator so the rim touches the ground
            float y = MathF.Cos(phi), ring = MathF.Sin(phi);
            for (int s = 0; s < segs; s++)
            {
                float th = s / (float)segs * MathHelper.TwoPi;
                var d = new Vector3(ring * MathF.Sin(th), y, ring * MathF.Cos(th));
                float lump = 1f + 0.16f * MathF.Sin(d.X * 5.1f + s1) * MathF.Sin(d.Z * 6.3f + s2) + 0.08f * MathF.Sin(th * 7 + s1);
                var p = c + d * radii * lump;
                p.Y = MathF.Max(p.Y, 0.01f);
                var cc = Lerp(shade, col, MathHelper.Clamp(d.Y * 0.9f + 0.4f, 0, 1));
                var v = new SkinnedVertex { Position = p, Normal = Vector3.Normalize(d + Vector3.Up * 0.3f), Color = cc, Material = GrassMat, BlendIndices = default, BlendWeights = new Vector4(1, 0, 0, 0) };
                v.Color.A = (byte)(200 - 30 * y);        // ≥128: foliage flutter range
                _v.Add(v);
            }
        }
        for (int st = 0; st < stacks; st++)
        {
            int r0 = b + st * segs, r1 = r0 + segs;
            for (int s = 0; s < segs; s++)
            {
                int s1i = (s + 1) % segs;
                Quad(r0 + s, r0 + s1i, r1 + s1i, r1 + s, both: false);
            }
        }
    }

    // ------------------------------------------------------------------ helpers

    private static SkinnedVertex V(Vector3 p, Vector3 n, Color c, float bend)
    {
        c.A = Bend(bend);
        return new SkinnedVertex { Position = p, Normal = n, Color = c, Material = GrassMat, BlendIndices = default, BlendWeights = new Vector4(1, 0, 0, 0) };
    }

    private void Tri(int a, int b, int c, bool both)
    {
        _i.Add(a); _i.Add(b); _i.Add(c);
        if (both) { _i.Add(a); _i.Add(c); _i.Add(b); }
    }

    private void Quad(int a, int b, int c, int d, bool both)
    {
        Tri(a, b, c, both); Tri(a, c, d, both);
    }

    private static Color Lerp(Color a, Color b, float t) => Color.Lerp(a, b, MathHelper.Clamp(t, 0, 1));

    /// <summary>Cheap smooth value noise in 0..1 (bilinear over a hashed lattice).</summary>
    public static float Noise(float x, float y)
    {
        int x0 = (int)MathF.Floor(x), y0 = (int)MathF.Floor(y);
        float tx = x - x0, ty = y - y0;
        tx = tx * tx * (3 - 2 * tx); ty = ty * ty * (3 - 2 * ty);
        float H(int i, int j) { uint h = (uint)(i * 374761393 + j * 668265263); h = (h ^ (h >> 13)) * 1274126177u; return ((h ^ (h >> 16)) & 0xFFFF) / 65535f; }
        return MathHelper.Lerp(MathHelper.Lerp(H(x0, y0), H(x0 + 1, y0), tx), MathHelper.Lerp(H(x0, y0 + 1), H(x0 + 1, y0 + 1), tx), ty);
    }

    private (VertexBuffer, IndexBuffer) Upload(GraphicsDevice gd)
    {
        var vb = new VertexBuffer(gd, SkinnedVertex.Declaration, _v.Count, BufferUsage.WriteOnly);
        vb.SetData(_v.ToArray());
        var ib = new IndexBuffer(gd, IndexElementSize.ThirtyTwoBits, _i.Count, BufferUsage.WriteOnly);
        ib.SetData(_i.ToArray());
        return (vb, ib);
    }
}
