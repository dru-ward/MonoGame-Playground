using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game.Graphics;

/// <summary>
/// Procedural environment textures (floor, crates, particle glow, 1x1 pixel) plus the shared helpers used by
/// every generator: seamless value noise, Sobel height→normal conversion and mip-chain creation.
/// Normal-map convention: +X right, +Y down, +Z toward the viewer, encoded rgb = n*0.5+0.5.
/// </summary>
public static class TextureFactory
{
    // ------------------------------------------------------------------------------------------------ floor
    private static float FloorHeight(int x, int y, int size)
    {
        const int tiles = 4;
        float cell = size / (float)tiles;
        float fx = (x % cell) / cell, fy = (y % cell) / cell;
        float edge = MathF.Min(MathF.Min(fx, 1f - fx), MathF.Min(fy, 1f - fy));   // distance to nearest tile edge
        float bevel = MathHelper.Clamp(edge / 0.10f, 0f, 1f);
        bevel = bevel * bevel * (3f - 2f * bevel);                                 // smoothstep
        float bump = Noise(x * 28f / size, y * 28f / size, 28) * 0.18f + Noise(x * 96f / size, y * 96f / size, 96) * 0.07f;
        return bevel * (0.75f + bump);
    }

    public static Texture2D CreateFloorAlbedo(GraphicsDevice gd, int size)
    {
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float h = FloorHeight(x, y, size);
            float grain = Noise(x * 224f / size, y * 224f / size, 224) * 0.12f;
            int tx = x * 4 / size, ty = y * 4 / size;
            float tint = 0.85f + 0.15f * Hash(tx * 7 + ty * 13);
            float v = MathHelper.Lerp(0.16f, 0.62f, h) * tint + grain;
            data[y * size + x] = new Color(v * 0.86f, v * 0.80f, v * 0.74f, 1f);
        }
        return CreateWithMips(gd, size, data);
    }

    public static Texture2D CreateFloorNormal(GraphicsDevice gd, int size)
    {
        var height = new float[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            height[y * size + x] = FloorHeight(x, y, size);
        return CreateWithMips(gd, size, HeightToNormal(height, size, size, 3.5f, wrap: true));
    }

    // ------------------------------------------------------------------------------------------------ asphalt
    /// <summary>
    /// Cracked, patched urban asphalt tile (seamless). Height field = fine aggregate + ridged-noise cracks + expansion
    /// joints + a few potholes; albedo = dark grey with grime patches, oil stains, tar-filled cracks, gravel specks and
    /// a worn painted lane line. size is the tile size (512 recommended, so 6x6 tiles cover the 3072 world).
    /// </summary>
    private static float AsphaltHeight(int x, int y, int size, out float crack, out float pothole)
    {
        float u = x / (float)size, v = y / (float)size;
        // fine aggregate
        float grain = Noise(u * 128f, v * 128f, 128) * 0.06f + Noise(u * 256f, v * 256f, 256) * 0.03f;
        // cracks: thin ridged-noise lines, only where a low-frequency mask allows (so most of the tile is intact)
        float n1 = Noise(u * 4f + 3.1f, v * 4f + 1.7f, 4);
        float r1 = 1f - MathF.Abs(2f * n1 - 1f);
        float c1 = MathHelper.Clamp((r1 - 0.986f) / 0.012f, 0f, 1f);
        float mask = MathHelper.Clamp((Noise(u * 3f + 0.5f, v * 3f + 8.5f, 3) - 0.56f) / 0.12f, 0f, 1f);
        crack = c1 * mask;
        // expansion joints every tile edge (wraps because it's at the border)
        float jx = MathF.Min(x, size - x), jy = MathF.Min(y, size - y);
        float joint = 1f - MathHelper.Clamp(MathF.Min(jx, jy) / 5f, 0f, 1f);
        // potholes: 3 hashed positions per tile
        pothole = 0f;
        for (int i = 0; i < 3; i++)
        {
            float px = Hash(i * 91 + 7) * size, py = Hash(i * 37 + 3) * size, pr = 22f + Hash(i * 13 + 5) * 26f;
            // wrapped distance
            float dx = MathF.Abs(x - px); dx = MathF.Min(dx, size - dx);
            float dy = MathF.Abs(y - py); dy = MathF.Min(dy, size - dy);
            float d = MathF.Sqrt(dx * dx + dy * dy) / pr;
            float edgeNoise = 0.8f + 0.4f * Noise(x * 0.15f, y * 0.15f, 0);
            pothole = MathF.Max(pothole, MathHelper.Clamp(1f - d * edgeNoise, 0f, 1f));
        }
        float ph = pothole * pothole * (3f - 2f * pothole);
        return 0.6f + grain - crack * 0.25f - joint * 0.3f - ph * 0.5f;
    }

    public static Texture2D CreateAsphaltAlbedo(GraphicsDevice gd, int size)
    {
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (float)size, v = y / (float)size;
            float h = AsphaltHeight(x, y, size, out float crack, out float pothole);
            float baseV = 0.30f + (Noise(u * 128f, v * 128f, 128) - 0.5f) * 0.06f;
            float grime = Noise(u * 4f + 0.7f, v * 4f + 2.3f, 4);                     // large dark/light patches
            baseV *= 0.78f + 0.45f * grime;
            float dust = Noise(u * 20f + 5f, v * 20f + 1f, 20);                       // lighter dusty areas
            baseV += MathHelper.Clamp(dust - 0.6f, 0f, 0.4f) * 0.25f;
            // oil stains: two dark ellipses per tile
            for (int i = 0; i < 2; i++)
            {
                float ox = Hash(i * 53 + 11) * size, oy = Hash(i * 29 + 17) * size;
                float dx = MathF.Abs(x - ox); dx = MathF.Min(dx, size - dx); float dy = MathF.Abs(y - oy); dy = MathF.Min(dy, size - dy);
                float d = MathF.Sqrt(dx * dx * 0.6f + dy * dy) / (60f + Hash(i + 99) * 50f);
                float stain = MathHelper.Clamp(1f - d, 0f, 1f) * (0.6f + 0.4f * Noise(x * 0.08f, y * 0.08f, 0));
                baseV *= 1f - stain * 0.75f;
            }
            // cracks are tar-dark, potholes show lighter gravel
            baseV = MathHelper.Lerp(baseV, 0.06f, crack * 0.8f);
            baseV = MathHelper.Lerp(baseV, 0.30f, pothole * pothole * 0.6f);
            // gravel specks
            float speck = Hash(x * 1973 + y * 7919);
            if (speck > 0.985f) baseV += 0.12f;
            Vector3 col = new(baseV * 1.02f, baseV * 1.0f, baseV * 0.98f);
            // worn painted lane line through the middle of the tile (dashed), also a faint yellow curb line at the top
            float lineD = MathF.Abs(y - size * 0.5f);
            bool dash = ((x + 40) % 200) < 120;
            if (lineD < 5f && dash)
            {
                float wear = Noise(x * 0.05f, y * 0.4f, 0);
                float paint = MathHelper.Clamp((5f - lineD), 0f, 1f) * MathHelper.Clamp((wear - 0.35f) * 2.5f, 0f, 1f) * (1f - crack) * (1f - pothole);
                col = Vector3.Lerp(col, new Vector3(0.62f, 0.60f, 0.55f), paint * 0.8f);
            }
            data[y * size + x] = new Color(col.X, col.Y, col.Z, 1f);
        }
        return CreateWithMips(gd, size, data);
    }

    public static Texture2D CreateAsphaltNormal(GraphicsDevice gd, int size)
    {
        var height = new float[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            height[y * size + x] = AsphaltHeight(x, y, size, out _, out _);
        return CreateWithMips(gd, size, HeightToNormal(height, size, size, 1.5f, wrap: true));
    }

    // ------------------------------------------------------------------------------------------------ grass
    /// <summary>
    /// Overgrown grass tile (seamless): soft turf lumps + fine blade speckle, worn dirt patches, clover flecks,
    /// a few daisies and half-buried stones. Same 512 px tiling scheme as the asphalt.
    /// </summary>
    private static float GrassHeight(int x, int y, int size, out float dirt)
    {
        float u = x / (float)size, v = y / (float)size;
        float lumps = Noise(u * 24f, v * 24f, 24) * 0.10f + Noise(u * 64f + 2.7f, v * 64f + 5.1f, 64) * 0.06f;
        float blades = Noise(u * 192f, v * 192f, 192) * 0.06f;
        dirt = MathHelper.Clamp((Noise(u * 3f + 4.2f, v * 3f + 7.7f, 3) - 0.60f) / 0.14f, 0f, 1f);
        return 0.5f + lumps + blades - dirt * 0.14f;
    }

    public static Texture2D CreateGrassAlbedo(GraphicsDevice gd, int size)
    {
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float u = x / (float)size, v = y / (float)size;
            GrassHeight(x, y, size, out float dirt);
            float patch = Noise(u * 6f + 1.3f, v * 6f + 9.1f, 6);                      // lush ↔ dry sward patches
            var col = Vector3.Lerp(new Vector3(0.18f, 0.30f, 0.11f), new Vector3(0.34f, 0.37f, 0.14f), patch);
            float blades = Noise(u * 192f, v * 192f, 192);                             // fine blade shimmer
            col *= 0.80f + 0.45f * blades;
            col *= 0.88f + 0.24f * Noise(u * 24f, v * 24f, 24);                        // turf lump shading
            // worn dirt patches with a noisy edge
            float dirtN = dirt * (0.65f + 0.5f * Noise(u * 40f + 3f, v * 40f + 6f, 40));
            col = Vector3.Lerp(col, new Vector3(0.31f, 0.25f, 0.16f) * (0.8f + 0.4f * blades), MathHelper.Clamp(dirtN, 0f, 1f));
            // clover flecks, the odd daisy, half-buried stones
            float speck = Hash(x * 1973 + y * 7919);
            if (speck > 0.992f) col *= 1.35f;
            else if (speck < 0.0012f) col = new Vector3(0.72f, 0.70f, 0.55f);          // daisy
            else if (speck > 0.9905f && dirt > 0.3f) col = new Vector3(0.42f, 0.41f, 0.38f);   // stone in a bare patch
            data[y * size + x] = new Color(col.X, col.Y, col.Z, 1f);
        }
        return CreateWithMips(gd, size, data);
    }

    public static Texture2D CreateGrassNormal(GraphicsDevice gd, int size)
    {
        var height = new float[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            height[y * size + x] = GrassHeight(x, y, size, out _);
        return CreateWithMips(gd, size, HeightToNormal(height, size, size, 1.2f, wrap: true));
    }

    // ------------------------------------------------------------------------------------------------ crate
    private static float CrateHeight(int x, int y, int size)
    {
        float fx = x / (float)(size - 1), fy = y / (float)(size - 1);
        float edge = MathF.Min(MathF.Min(fx, 1f - fx), MathF.Min(fy, 1f - fy));
        float frame = MathHelper.Clamp(edge / 0.12f, 0f, 1f);
        frame = frame * frame * (3f - 2f * frame);
        float plankPos = (fy * 3f) % 1f;
        float groove = MathHelper.Clamp(MathF.Min(plankPos, 1f - plankPos) / 0.08f, 0f, 1f);
        float wood = Noise(x * 0.25f, y * 2.0f, 0) * 0.08f;
        return 0.55f + frame * 0.35f * groove + wood;
    }

    public static Texture2D CreateCrateAlbedo(GraphicsDevice gd, int size)
    {
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float h = CrateHeight(x, y, size);
            float grain = Noise(x * 0.15f, y * 1.4f, 0) * 0.25f + Noise(x * 0.6f, y * 5f, 0) * 0.08f;
            float v = 0.30f + h * 0.45f + grain;
            data[y * size + x] = new Color(v * 1.05f, v * 0.72f, v * 0.42f, 1f);
        }
        var tex = new Texture2D(gd, size, size); tex.SetData(data); return tex;
    }

    public static Texture2D CreateCrateNormal(GraphicsDevice gd, int size)
    {
        var height = new float[size * size];
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
            height[y * size + x] = CrateHeight(x, y, size);
        var tex = new Texture2D(gd, size, size);
        tex.SetData(HeightToNormal(height, size, size, 4.0f, wrap: false));
        return tex;
    }

    // ------------------------------------------------------------------------------------------------ misc
    /// <summary>Soft radial glow, premultiplied (rgb == a) so it works with additive blending.</summary>
    public static Texture2D CreateParticle(GraphicsDevice gd, int size)
    {
        var data = new Color[size * size];
        float c = (size - 1) * 0.5f;
        for (int y = 0; y < size; y++)
        for (int x = 0; x < size; x++)
        {
            float d = MathF.Sqrt((x - c) * (x - c) + (y - c) * (y - c)) / c;
            float a = MathHelper.Clamp(1f - d, 0f, 1f);
            a = a * a * (3f - 2f * a);
            a *= a;
            data[y * size + x] = new Color(a, a, a, a);
        }
        var tex = new Texture2D(gd, size, size); tex.SetData(data); return tex;
    }

    public static Texture2D CreatePixel(GraphicsDevice gd, Color color)
    {
        var tex = new Texture2D(gd, 1, 1); tex.SetData(new[] { color }); return tex;
    }

    /// <summary>Soft elliptical drop shadow (premultiplied black). Pair it with a transparent normal so it only darkens albedo.</summary>
    public static SpritePair CreateShadow(GraphicsDevice gd, int w, int h, float strength = 0.55f)
    {
        var data = new Color[w * h]; float cx = (w - 1) * 0.5f, cy = (h - 1) * 0.5f;
        for (int y = 0; y < h; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = (x - cx) / cx, dy = (y - cy) / cy; float d = MathF.Sqrt(dx * dx + dy * dy);
            float a = MathHelper.Clamp(1f - d, 0f, 1f); a = a * a * (3f - 2f * a) * strength;
            data[y * w + x] = new Color(0f, 0f, 0f, a);
        }
        var albedo = new Texture2D(gd, w, h); albedo.SetData(data);
        var normal = new Texture2D(gd, w, h); normal.SetData(new Color[w * h]);   // fully transparent
        return new SpritePair(albedo, normal);
    }

    /// <summary>Flat "facing the camera" normal (128,128,255) — for sprites that need no relief.</summary>
    public static Texture2D CreateFlatNormal(GraphicsDevice gd) => CreatePixel(gd, new Color(128, 128, 255, 255));

    // ------------------------------------------------------------------------------------------------ helpers
    /// <summary>Sobel-filtered height field → encoded tangent-space normal map. n = normalize(-dh/dx*s, -dh/dy*s, 1).</summary>
    public static Color[] HeightToNormal(float[] h, int w, int hgt, float strength, bool wrap)
    {
        var data = new Color[w * hgt];
        float H(int x, int y)
        {
            if (wrap) { x = (x + w) % w; y = (y + hgt) % hgt; }
            else      { x = Math.Clamp(x, 0, w - 1); y = Math.Clamp(y, 0, hgt - 1); }
            return h[y * w + x];
        }
        for (int y = 0; y < hgt; y++)
        for (int x = 0; x < w; x++)
        {
            float dx = (H(x + 1, y - 1) + 2f * H(x + 1, y) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + 2f * H(x - 1, y) + H(x - 1, y + 1));
            float dy = (H(x - 1, y + 1) + 2f * H(x, y + 1) + H(x + 1, y + 1)) - (H(x - 1, y - 1) + 2f * H(x, y - 1) + H(x + 1, y - 1));
            var n = Vector3.Normalize(new Vector3(-dx * strength, -dy * strength, 1f));
            data[y * w + x] = new Color(n.X * 0.5f + 0.5f, n.Y * 0.5f + 0.5f, n.Z * 0.5f + 0.5f, 1f);
        }
        return data;
    }

    /// <summary>Texture with a full box-filtered mip chain (needed for anisotropic/trilinear sampling).</summary>
    public static Texture2D CreateWithMips(GraphicsDevice gd, int size, Color[] level0)
    {
        var tex = new Texture2D(gd, size, size, true, SurfaceFormat.Color);
        var src = level0; int s = size, level = 0;
        tex.SetData(0, null, src, 0, src.Length);
        while (s > 1)
        {
            int ns = s / 2;
            var dst = new Color[ns * ns];
            for (int y = 0; y < ns; y++)
            for (int x = 0; x < ns; x++)
            {
                Color a = src[(2 * y) * s + 2 * x], b = src[(2 * y) * s + 2 * x + 1],
                      c = src[(2 * y + 1) * s + 2 * x], d = src[(2 * y + 1) * s + 2 * x + 1];
                dst[y * ns + x] = new Color((a.R + b.R + c.R + d.R) / 4, (a.G + b.G + c.G + d.G) / 4,
                                            (a.B + b.B + c.B + d.B) / 4, (a.A + b.A + c.A + d.A) / 4);
            }
            level++; s = ns; src = dst;
            tex.SetData(level, null, src, 0, src.Length);
        }
        return tex;
    }

    public static float Hash(int n)
    {
        unchecked { n = (n << 13) ^ n; return ((n * (n * n * 15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f; }
    }

    /// <summary>Smooth value noise. x,y in lattice units; period = lattice cells before repeating (0 = no wrap).</summary>
    public static float Noise(float x, float y, int period)
    {
        int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y);
        float fx = x - xi, fy = y - yi;
        fx = fx * fx * (3f - 2f * fx); fy = fy * fy * (3f - 2f * fy);
        int Mod(int a) => period <= 0 ? a : ((a % period) + period) % period;
        int X0 = Mod(xi), X1 = Mod(xi + 1), Y0 = Mod(yi), Y1 = Mod(yi + 1);
        float a = Hash(X0 * 1619 + Y0 * 31337), b = Hash(X1 * 1619 + Y0 * 31337);
        float c = Hash(X0 * 1619 + Y1 * 31337), d = Hash(X1 * 1619 + Y1 * 31337);
        return MathHelper.Lerp(MathHelper.Lerp(a, b, fx), MathHelper.Lerp(c, d, fx), fy);
    }
}
