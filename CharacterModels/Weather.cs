using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CharacterModels;

/// <summary>
/// Rain streaks, ground splashes and wind-blown falling leaves. All are CPU particles rendered as
/// camera-facing (or flat) quads through a BasicEffect after the lit scene, so they are unlit and cheap.
/// </summary>
public sealed class Weather
{
    private struct Drop { public Vector3 Pos, Vel; }
    private struct Splash { public Vector3 Pos; public float Age; }
    private struct LeafP { public Vector3 Pos, Vel; public float Spin, Angle, Age, Life, Phase; public Color Color; }

    public bool Raining;
    public float RainDensity = 1f;           // 1 = steady rain
    public bool Leaves = true;

    private readonly List<Drop> _drops = new();
    private readonly List<Splash> _splashes = new();
    private readonly List<LeafP> _leaves = new();
    private readonly Random _rnd = new(99);
    private readonly BasicEffect _fx;
    private VertexPositionColor[] _verts = new VertexPositionColor[6 * 4096];
    private int _vertCount;
    private float _leafTimer;

    private const int MaxDrops = 2600;
    private const float Gravity = 9.8f;

    public Weather(GraphicsDevice gd)
    {
        _fx = new BasicEffect(gd) { VertexColorEnabled = true, LightingEnabled = false, TextureEnabled = false, FogEnabled = false };
    }

    private float Rand(float a, float b) => a + (float)_rnd.NextDouble() * (b - a);

    /// <param name="center">Where to keep the rain volume (the camera target).</param>
    /// <param name="leafSources">Tree crowns that shed leaves (position, crown height, leaf colours).</param>
    public void Update(float dt, Vector3 center, Wind wind, IReadOnlyList<(Vector3 pos, float height, Color[] colors)> leafSources)
    {
        // ---- rain
        int target = Raining ? (int)(MaxDrops * RainDensity) : 0;
        const float half = 14f, top = 13f;
        if (_drops.Count < target)
        {
            int spawn = Math.Min(target - _drops.Count, 120);
            for (int i = 0; i < spawn; i++)
                _drops.Add(new Drop
                {
                    Pos = new Vector3(center.X + Rand(-half, half), Rand(0.5f, top), center.Z + Rand(-half, half)),
                    Vel = new Vector3(0, -Rand(8.5f, 11f), 0) + wind.Direction * wind.Strength * Rand(1.5f, 3f)
                });
        }
        for (int i = _drops.Count - 1; i >= 0; i--)
        {
            var d = _drops[i];
            d.Pos += d.Vel * dt;
            if (d.Pos.Y <= 0f || !Raining && _rnd.NextDouble() < 0.02)
            {
                if (d.Pos.Y <= 0f && _splashes.Count < 600 && _rnd.NextDouble() < 0.35)
                    _splashes.Add(new Splash { Pos = new Vector3(d.Pos.X, 0.01f, d.Pos.Z), Age = 0 });
                if (Raining && _drops.Count <= target)
                {
                    // Recycle at the top, re-centred on the camera so the volume follows the player.
                    d.Pos = new Vector3(center.X + Rand(-half, half), top, center.Z + Rand(-half, half));
                    _drops[i] = d;
                }
                else _drops.RemoveAt(i);
                continue;
            }
            _drops[i] = d;
        }
        for (int i = _splashes.Count - 1; i >= 0; i--)
        {
            var s = _splashes[i]; s.Age += dt;
            if (s.Age > 0.28f) _splashes.RemoveAt(i); else _splashes[i] = s;
        }

        // ---- leaves: shed from crowns in proportion to wind, tumble, drift, settle and fade.
        if (Leaves && wind.Strength > 0.05f && leafSources.Count > 0)
        {
            _leafTimer += dt * wind.Strength * 2.2f * leafSources.Count;
            while (_leafTimer > 1f && _leaves.Count < 400)
            {
                _leafTimer -= 1f;
                var src = leafSources[_rnd.Next(leafSources.Count)];
                if (src.colors.Length == 0) continue;
                var ang = Rand(0, MathHelper.TwoPi);
                _leaves.Add(new LeafP
                {
                    Pos = src.pos + new Vector3(MathF.Cos(ang) * Rand(0.3f, 1.1f), src.height + Rand(-0.6f, 0.4f), MathF.Sin(ang) * Rand(0.3f, 1.1f)),
                    Vel = wind.Direction * wind.Strength * Rand(0.5f, 1.5f),
                    Spin = Rand(-6f, 6f), Angle = Rand(0, 6.28f), Life = Rand(5f, 8f), Phase = Rand(0, 6.28f),
                    Color = src.colors[_rnd.Next(src.colors.Length)]
                });
            }
        }
        for (int i = _leaves.Count - 1; i >= 0; i--)
        {
            var l = _leaves[i];
            l.Age += dt;
            if (l.Pos.Y > 0.02f)
            {
                // Falling: slow terminal velocity, side-to-side flutter, wind drift with gusts.
                float gust = wind.Gust(l.Age * 3f + l.Phase, l.Pos);
                var drift = wind.Direction * wind.Strength * (0.9f + 1.2f * gust);
                var flutter = new Vector3(MathF.Sin(l.Age * 4.5f + l.Phase), 0, MathF.Cos(l.Age * 3.7f + l.Phase)) * 0.5f;
                var targetVel = drift + flutter + new Vector3(0, -0.9f - 0.4f * MathF.Sin(l.Age * 5f + l.Phase), 0);
                l.Vel = Vector3.Lerp(l.Vel, targetVel, 1 - MathF.Exp(-dt * 3f));
                l.Pos += l.Vel * dt;
                l.Angle += l.Spin * dt;
                if (l.Pos.Y <= 0.02f) { l.Pos.Y = 0.02f; l.Vel = Vector3.Zero; l.Spin = 0; }
            }
            else
            {
                // On the ground: skitter along in strong wind, then fade away.
                if (wind.Strength > 0.9f) l.Pos += wind.Direction * wind.Strength * 0.25f * dt * (0.5f + 0.5f * MathF.Sin(l.Age * 7f + l.Phase));
            }
            if (l.Age > l.Life) _leaves.RemoveAt(i); else _leaves[i] = l;
        }
    }

    public void Draw(GraphicsDevice gd, Matrix view, Matrix proj, Vector3 camPos, bool depthAvailable, Vector3 fogColorDisplay)
    {
        _vertCount = 0;
        var camRight = new Vector3(view.M11, view.M21, view.M31);
        var camUp = new Vector3(view.M12, view.M22, view.M32);

        // Rain: thin streaks stretched along velocity, faded with distance into the fog colour.
        var rainCol = new Color(170, 190, 215);
        foreach (var d in _drops)
        {
            float dist = Vector3.Distance(camPos, d.Pos);
            if (dist > 30f) continue;
            float a = 0.42f * (1f - MathHelper.Clamp((dist - 6f) / 24f, 0, 1)) * MathHelper.Clamp(dist / 1.5f, 0, 1);
            var col = Color.Lerp(new Color(fogColorDisplay), rainCol, 0.7f) * a;
            var tail = d.Pos - d.Vel * 0.045f;
            var axis = Vector3.Normalize(d.Pos - tail);
            var side = Vector3.Cross(axis, Vector3.Normalize(camPos - d.Pos));
            if (side.LengthSquared() < 1e-6f) side = camRight;
            side = Vector3.Normalize(side) * 0.008f;
            Quad(d.Pos + side, d.Pos - side, tail - side, tail + side, col, col * 0.2f);
        }
        // Splashes: a small flat ring that expands and fades.
        foreach (var s in _splashes)
        {
            float t = s.Age / 0.28f;
            float r = 0.03f + 0.14f * t;
            var col = new Color(190, 205, 225) * (0.5f * (1 - t));
            var c = s.Pos;
            Quad(c + new Vector3(-r, 0, -r), c + new Vector3(r, 0, -r), c + new Vector3(r, 0, r), c + new Vector3(-r, 0, r), col * 0.0f, col, ring: true);
        }
        // Leaves: small tumbling quads; fade in the last second.
        foreach (var l in _leaves)
        {
            float fade = MathHelper.Clamp((l.Life - l.Age) / 1.2f, 0, 1);
            var col = l.Color * fade;
            var dark = new Color((int)(l.Color.R * 0.7f), (int)(l.Color.G * 0.7f), (int)(l.Color.B * 0.7f)) * fade;
            float sz = 0.075f;
            var rot = Matrix.CreateFromYawPitchRoll(l.Angle, l.Angle * 0.7f, l.Angle * 1.3f);
            if (l.Pos.Y <= 0.03f) rot = Matrix.CreateRotationY(l.Phase);
            var u = Vector3.TransformNormal(Vector3.Right, rot) * sz;
            var v = Vector3.TransformNormal(Vector3.Backward, rot) * sz * 0.65f;
            var c = l.Pos;
            Quad(c - u - v, c + u - v, c + u + v, c - u + v, col, dark, doubleSided: true);
        }
        if (_vertCount == 0) return;

        gd.BlendState = BlendState.NonPremultiplied;
        gd.DepthStencilState = depthAvailable ? DepthStencilState.DepthRead : DepthStencilState.None;
        gd.RasterizerState = RasterizerState.CullNone;
        _fx.View = view; _fx.Projection = proj; _fx.World = Matrix.Identity;
        foreach (var pass in _fx.CurrentTechnique.Passes)
        {
            pass.Apply();
            gd.DrawUserPrimitives(PrimitiveType.TriangleList, _verts, 0, _vertCount / 3);
        }
        gd.BlendState = BlendState.Opaque;
        gd.DepthStencilState = DepthStencilState.Default;
        gd.RasterizerState = RasterizerState.CullCounterClockwise;
    }

    public int Count => _drops.Count + _leaves.Count;

    private void Quad(Vector3 a, Vector3 b, Vector3 c, Vector3 d, Color headCol, Color tailCol, bool ring = false, bool doubleSided = false)
    {
        if (_vertCount + 12 > _verts.Length) Array.Resize(ref _verts, _verts.Length * 2);
        if (ring)
        {
            // Ring = 8 thin segments around the centre.
            var centre = (a + c) * 0.5f; float r = (b - a).Length() * 0.5f;
            for (int i = 0; i < 8; i++)
            {
                float a0 = i / 8f * MathHelper.TwoPi, a1 = (i + 1) / 8f * MathHelper.TwoPi;
                var p0 = centre + new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0)) * r;
                var p1 = centre + new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1)) * r;
                var p0i = centre + new Vector3(MathF.Cos(a0), 0, MathF.Sin(a0)) * r * 0.7f;
                var p1i = centre + new Vector3(MathF.Cos(a1), 0, MathF.Sin(a1)) * r * 0.7f;
                if (_vertCount + 6 > _verts.Length) Array.Resize(ref _verts, _verts.Length * 2);
                Tri(p0, p1, p1i, tailCol); Tri(p0, p1i, p0i, tailCol);
            }
            return;
        }
        Tri(a, b, c, headCol, headCol, tailCol); Tri(a, c, d, headCol, tailCol, tailCol);
        if (doubleSided) { Tri(a, c, b, headCol, tailCol, headCol); Tri(a, d, c, headCol, tailCol, tailCol); }
    }

    private void Tri(Vector3 a, Vector3 b, Vector3 c, Color col) => Tri(a, b, c, col, col, col);
    private void Tri(Vector3 a, Vector3 b, Vector3 c, Color ca, Color cb, Color cc)
    {
        _verts[_vertCount++] = new VertexPositionColor(a, ca);
        _verts[_vertCount++] = new VertexPositionColor(b, cb);
        _verts[_vertCount++] = new VertexPositionColor(c, cc);
    }
}
