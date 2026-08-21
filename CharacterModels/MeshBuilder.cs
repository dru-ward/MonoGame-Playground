using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Graphics.PackedVector;

namespace CharacterModels;

/// <summary>Vertex with 4-bone skinning, per-vertex albedo and material params.</summary>
public struct SkinnedVertex : IVertexType
{
    public Vector3 Position;
    public Vector3 Normal;
    public Color Color;
    public Vector2 Material;      // x = specular strength, y = shininess (0..1)
    public Byte4 BlendIndices;
    public Vector4 BlendWeights;

    public static readonly VertexDeclaration Declaration = new(
        new VertexElement(0, VertexElementFormat.Vector3, VertexElementUsage.Position, 0),
        new VertexElement(12, VertexElementFormat.Vector3, VertexElementUsage.Normal, 0),
        new VertexElement(24, VertexElementFormat.Color, VertexElementUsage.Color, 0),
        new VertexElement(28, VertexElementFormat.Vector2, VertexElementUsage.TextureCoordinate, 0),
        new VertexElement(36, VertexElementFormat.Byte4, VertexElementUsage.BlendIndices, 0),
        new VertexElement(40, VertexElementFormat.Vector4, VertexElementUsage.BlendWeight, 0));

    public VertexDeclaration VertexDeclaration => Declaration;
}

/// <summary>Material presets: (specular strength, shininess).</summary>
public static class Mat
{
    public static readonly Vector2 Skin = new(0.18f, 0.25f);
    public static readonly Vector2 Cloth = new(0.04f, 0.08f);
    public static readonly Vector2 Leather = new(0.35f, 0.35f);
    public static readonly Vector2 Metal = new(1.0f, 0.85f);
    public static readonly Vector2 Hair = new(0.3f, 0.45f);
    public static readonly Vector2 Eye = new(0.8f, 0.95f);
    public static readonly Vector2 Wood = new(0.2f, 0.3f);
    public static readonly Vector2 Glow = new(0.0f, 0.0f);
}

/// <summary>Computes skin weights for a vertex position from distance to bone segments.</summary>
public sealed class Weighter
{
    private readonly struct Influence
    {
        public readonly int Bone; public readonly Vector3 Head; public readonly Vector3 Tail;
        public Influence(int b, Vector3 h, Vector3 t) { Bone = b; Head = h; Tail = t; }
    }

    private readonly List<Influence> _influences = new();
    private readonly float _sharpness;

    /// <param name="sharpness">Exponent on distance falloff; higher = crisper joints.</param>
    public Weighter(Skeleton skeleton, float sharpness, params string[] bones)
    {
        _sharpness = sharpness;
        foreach (var name in bones)
        {
            var b = skeleton[name];
            _influences.Add(new Influence(b.Index, b.BindHead, b.BindTail));
        }
    }

    public static Weighter Fixed(Skeleton skeleton, string bone) => new(skeleton, 1, bone);

    public (Byte4 indices, Vector4 weights) Compute(Vector3 p)
    {
        if (_influences.Count == 1)
            return (new Byte4(_influences[0].Bone, 0, 0, 0), new Vector4(1, 0, 0, 0));

        Span<float> w = stackalloc float[_influences.Count];
        for (int i = 0; i < _influences.Count; i++)
        {
            float d = DistanceToSegment(p, _influences[i].Head, _influences[i].Tail);
            w[i] = 1f / (MathF.Pow(d, _sharpness) + 1e-6f);
        }

        Span<int> best = stackalloc int[4];
        Span<float> bw = stackalloc float[4];
        for (int k = 0; k < 4; k++) { best[k] = 0; bw[k] = 0; }
        for (int i = 0; i < _influences.Count; i++)
        {
            for (int k = 0; k < 4; k++)
            {
                if (w[i] > bw[k])
                {
                    for (int m = 3; m > k; m--) { bw[m] = bw[m - 1]; best[m] = best[m - 1]; }
                    bw[k] = w[i]; best[k] = _influences[i].Bone; break;
                }
            }
        }
        float sum = bw[0] + bw[1] + bw[2] + bw[3];
        var weights = new Vector4(bw[0], bw[1], bw[2], bw[3]) / sum;
        return (new Byte4(best[0], best[1], best[2], best[3]), weights);
    }

    private static float DistanceToSegment(Vector3 p, Vector3 a, Vector3 b)
    {
        var ab = b - a;
        float len2 = ab.LengthSquared();
        if (len2 < 1e-8f) return Vector3.Distance(p, a);
        float t = MathHelper.Clamp(Vector3.Dot(p - a, ab) / len2, 0, 1);
        return Vector3.Distance(p, a + ab * t);
    }
}

public struct Ring
{
    public Vector3 Center;
    public float Rx, Rz;
    public Color Color;
    public Vector2 Material;
    public Vector3? Tangent;

    public Ring(Vector3 c, float rx, float rz, Color color, Vector2 material)
    { Center = c; Rx = rx; Rz = rz; Color = color; Material = material; Tangent = null; }
    public Ring(Vector3 c, float r, Color color, Vector2 material) : this(c, r, r, color, material) { }
}

/// <summary>Accumulates procedural geometry into a single skinned mesh.</summary>
public sealed class MeshBuilder
{
    private readonly List<SkinnedVertex> _verts = new();
    private readonly List<int> _indices = new();

    public int VertexCount => _verts.Count;
    public int TriangleCount => _indices.Count / 3;

    // ------------------------------------------------------------------ lofts

    /// <summary>Skins a tube through a sequence of rings, optionally capped with hemispheres.</summary>
    public void Loft(IList<Ring> rings, int segments, Weighter weighter, Vector3 upHint,
                     bool capStart = false, bool capEnd = false, int capSteps = 4)
    {
        var list = new List<Ring>(rings);
        if (list.Count < 2) throw new ArgumentException("Need at least two rings");

        var tangents = new Vector3[list.Count];
        for (int i = 0; i < list.Count; i++)
        {
            if (list[i].Tangent.HasValue) { tangents[i] = Vector3.Normalize(list[i].Tangent.Value); continue; }
            Vector3 prev = list[Math.Max(i - 1, 0)].Center, next = list[Math.Min(i + 1, list.Count - 1)].Center;
            var t = next - prev;
            tangents[i] = t.LengthSquared() < 1e-10f ? Vector3.Up : Vector3.Normalize(t);
        }

        var all = new List<(Ring r, Vector3 t)>();
        if (capStart)
        {
            var r0 = list[0]; var t0 = tangents[0];
            for (int k = capSteps; k >= 1; k--)
            {
                float a = k / (float)capSteps * MathHelper.PiOver2;
                float ca = MathF.Cos(a), sa = MathF.Sin(a);
                float rAvg = (r0.Rx + r0.Rz) * 0.5f;
                var rr = r0; rr.Center = r0.Center - t0 * rAvg * sa; rr.Rx = r0.Rx * ca + 1e-4f; rr.Rz = r0.Rz * ca + 1e-4f;
                all.Add((rr, t0));
            }
        }
        for (int i = 0; i < list.Count; i++) all.Add((list[i], tangents[i]));
        if (capEnd)
        {
            var r1 = list[^1]; var t1 = tangents[^1];
            for (int k = 1; k <= capSteps; k++)
            {
                float a = k / (float)capSteps * MathHelper.PiOver2;
                float ca = MathF.Cos(a), sa = MathF.Sin(a);
                float rAvg = (r1.Rx + r1.Rz) * 0.5f;
                var rr = r1; rr.Center = r1.Center + t1 * rAvg * sa; rr.Rx = r1.Rx * ca + 1e-4f; rr.Rz = r1.Rz * ca + 1e-4f;
                all.Add((rr, t1));
            }
        }

        int baseIndex = _verts.Count;
        Vector3 side = Vector3.Cross(upHint, all[0].t);
        if (side.LengthSquared() < 1e-6f) side = Vector3.Cross(Vector3.Right, all[0].t);
        side.Normalize();
        for (int i = 0; i < all.Count; i++)
        {
            var (ring, t) = all[i];
            side = Vector3.Normalize(side - t * Vector3.Dot(side, t));   // parallel transport
            var up = Vector3.Cross(t, side);
            for (int s = 0; s < segments; s++)
            {
                float ang = s / (float)segments * MathHelper.TwoPi;
                var p = ring.Center + side * (MathF.Cos(ang) * ring.Rx) + up * (MathF.Sin(ang) * ring.Rz);
                _verts.Add(new SkinnedVertex { Position = p, Color = ring.Color, Material = ring.Material });
            }
        }
        for (int i = 0; i < all.Count - 1; i++)
        {
            int r0 = baseIndex + i * segments, r1 = r0 + segments;
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                AddQuad(r0 + s, r1 + s, r1 + s1, r0 + s1);
            }
        }
        FinishPart(baseIndex, weighter);
    }

    // ------------------------------------------------------------- ellipsoids

    /// <summary>UV ellipsoid with optional per-direction radius shaping (shape returns a scale factor).</summary>
    public void Ellipsoid(Vector3 center, Vector3 radii, int segments, int stacks, Color color, Vector2 material,
                          Weighter weighter, Func<Vector3, float>? shape = null, Quaternion? orientation = null)
    {
        int baseIndex = _verts.Count;
        var rot = orientation ?? Quaternion.Identity;
        for (int st = 0; st <= stacks; st++)
        {
            float phi = st / (float)stacks * MathHelper.Pi;
            float y = MathF.Cos(phi), ring = MathF.Sin(phi);
            for (int s = 0; s < segments; s++)
            {
                float theta = s / (float)segments * MathHelper.TwoPi;
                var d = new Vector3(ring * MathF.Sin(theta), y, ring * MathF.Cos(theta));
                float k = shape?.Invoke(d) ?? 1f;
                var p = center + Vector3.Transform(d * radii * k, rot);
                _verts.Add(new SkinnedVertex { Position = p, Color = color, Material = material });
            }
        }
        for (int st = 0; st < stacks; st++)
        {
            int r0 = baseIndex + st * segments, r1 = r0 + segments;
            for (int s = 0; s < segments; s++)
            {
                int s1 = (s + 1) % segments;
                AddQuad(r0 + s, r0 + s1, r1 + s1, r1 + s);   // outward-facing winding
            }
        }
        FinishPart(baseIndex, weighter);
    }

    // ------------------------------------------------------------------ boxes

    /// <summary>Flat-shaded box. Size is full extents.</summary>
    public void Box(Vector3 center, Vector3 size, Color color, Vector2 material, Weighter weighter, Quaternion? orientation = null)
    {
        var rot = orientation ?? Quaternion.Identity;
        var h = size * 0.5f;
        Vector3[] axes = { Vector3.Right, Vector3.Up, Vector3.Backward };
        for (int a = 0; a < 3; a++)
        {
            var u = axes[(a + 1) % 3]; var v = axes[(a + 2) % 3]; var n = axes[a];
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int baseIndex = _verts.Count;
                var nn = n * sign;
                Vector3[] corners =
                {
                    nn * h - u * h - v * h, nn * h + u * h - v * h, nn * h + u * h + v * h, nn * h - u * h + v * h
                };
                for (int i = 0; i < 4; i++)
                {
                    var p = center + Vector3.Transform(corners[i], rot);
                    var (bi, bw) = weighter.Compute(p);
                    _verts.Add(new SkinnedVertex
                    {
                        Position = p, Normal = Vector3.Transform(nn, rot), Color = color, Material = material,
                        BlendIndices = bi, BlendWeights = bw
                    });
                }
                if (sign > 0) AddQuad(baseIndex + 0, baseIndex + 3, baseIndex + 2, baseIndex + 1);
                else AddQuad(baseIndex + 0, baseIndex + 1, baseIndex + 2, baseIndex + 3);
            }
        }
    }

    // -------------------------------------------------------------- internals

    private void AddQuad(int a, int b, int c, int d)
    {
        _indices.Add(a); _indices.Add(b); _indices.Add(c);
        _indices.Add(a); _indices.Add(c); _indices.Add(d);
    }

    /// <summary>Smooth normals (area-weighted) and skin weights for the vertices added since baseIndex.</summary>
    private void FinishPart(int baseIndex, Weighter weighter)
    {
        int count = _verts.Count - baseIndex;
        var normals = new Vector3[count];
        for (int i = _indices.Count - 3; i >= 0; i -= 3)
        {
            int a = _indices[i], b = _indices[i + 1], c = _indices[i + 2];
            if (a < baseIndex) break;
            var pa = _verts[a].Position; var pb = _verts[b].Position; var pc = _verts[c].Position;
            var n = Vector3.Cross(pb - pa, pc - pa);
            normals[a - baseIndex] += n; normals[b - baseIndex] += n; normals[c - baseIndex] += n;
        }
        for (int i = 0; i < count; i++)
        {
            var v = _verts[baseIndex + i];
            var n = normals[i];
            v.Normal = n.LengthSquared() > 1e-12f ? Vector3.Normalize(n) : Vector3.Up;
            (v.BlendIndices, v.BlendWeights) = weighter.Compute(v.Position);
            _verts[baseIndex + i] = v;
        }
    }

    public SkinnedVertex[] Vertices => _verts.ToArray();
    public int[] Indices => _indices.ToArray();

    public (VertexBuffer vb, IndexBuffer ib) Upload(GraphicsDevice device)
    {
        var vb = new VertexBuffer(device, SkinnedVertex.Declaration, _verts.Count, BufferUsage.WriteOnly);
        vb.SetData(_verts.ToArray());
        var ib = new IndexBuffer(device, IndexElementSize.ThirtyTwoBits, _indices.Count, BufferUsage.WriteOnly);
        ib.SetData(_indices.ToArray());
        return (vb, ib);
    }
}
