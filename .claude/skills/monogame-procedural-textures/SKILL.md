---
name: monogame-procedural-textures
description: Generate textures at runtime in MonoGame with Texture2D.SetData — seamless tiling value noise, height-field to tangent-space normal map via Sobel, box-filtered mip chains, premultiplied-alpha sprites, radial particle glows. Use when a MonoGame project needs placeholder or fully procedural art (albedo + normal maps) without external PNGs.
---

# Procedural textures for MonoGame

All generators return `Texture2D`; build a `Color[]` and `SetData`. Convention for normal maps: +X right, +Y **down**,
+Z toward viewer, encoded `rgb = n*0.5+0.5`.

## Height field → normal map (Sobel)
```csharp
static Color[] HeightToNormal(float[] h, int w, int hgt, float strength, bool wrap)
{
    var data = new Color[w * hgt];
    float H(int x, int y)
    {
        if (wrap) { x = (x + w) % w; y = (y + hgt) % hgt; } else { x = Math.Clamp(x, 0, w-1); y = Math.Clamp(y, 0, hgt-1); }
        return h[y * w + x];
    }
    for (int y = 0; y < hgt; y++) for (int x = 0; x < w; x++)
    {
        float dx = (H(x+1,y-1) + 2*H(x+1,y) + H(x+1,y+1)) - (H(x-1,y-1) + 2*H(x-1,y) + H(x-1,y+1));
        float dy = (H(x-1,y+1) + 2*H(x,y+1) + H(x+1,y+1)) - (H(x-1,y-1) + 2*H(x,y-1) + H(x+1,y-1));
        var n = Vector3.Normalize(new Vector3(-dx * strength, -dy * strength, 1f));   // surface z = h(x,y)
        data[y*w + x] = new Color(n.X*0.5f+0.5f, n.Y*0.5f+0.5f, n.Z*0.5f+0.5f, 1f);
    }
    return data;
}
```
`strength` 3–4 for stone/wood; 6+ looks like hammered metal. Use `wrap:true` for tiling textures.

## Seamless value noise (integer lattice count = period)
```csharp
static float Hash(int n) { unchecked { n = (n << 13) ^ n; return ((n*(n*n*15731 + 789221) + 1376312589) & 0x7fffffff) / 2147483647f; } }
static float Noise(float x, float y, int period)   // x,y in lattice units; period = lattice cells across the texture
{
    int xi = (int)MathF.Floor(x), yi = (int)MathF.Floor(y); float fx = x - xi, fy = y - yi;
    fx = fx*fx*(3-2*fx); fy = fy*fy*(3-2*fy);
    int Mod(int a) => ((a % period) + period) % period;
    int X0 = Mod(xi), X1 = Mod(xi+1), Y0 = Mod(yi), Y1 = Mod(yi+1);
    float a = Hash(X0*1619 + Y0*31337), b = Hash(X1*1619 + Y0*31337), c = Hash(X0*1619 + Y1*31337), d = Hash(X1*1619 + Y1*31337);
    return MathHelper.Lerp(MathHelper.Lerp(a, b, fx), MathHelper.Lerp(c, d, fx), fy);
}
// call as Noise(x * cells / size, y * cells / size, cells)  — an INTEGER cell count keeps the tile seamless
```

## Tile / crate height fields (bevel = smoothstep of distance to edge)
```csharp
float edge  = MathF.Min(MathF.Min(fx, 1-fx), MathF.Min(fy, 1-fy));   // fx,fy in 0..1 within the tile
float bevel = MathHelper.Clamp(edge / 0.10f, 0, 1); bevel = bevel*bevel*(3-2*bevel);
float h = bevel * (0.75f + noiseBump);
```

## Mip chain for wrap/anisotropic samplers
```csharp
var tex = new Texture2D(gd, size, size, mipMap: true, SurfaceFormat.Color);
tex.SetData(0, null, level0, 0, level0.Length);
for (int s = size, level = 0; s > 1; ) { int ns = s/2; var dst = new Color[ns*ns];
    /* 2x2 box average of src into dst */ level++; s = ns; src = dst; tex.SetData(level, null, src, 0, src.Length); }
```

## Premultiplied sprites with anti-aliased edges (SpriteBatch AlphaBlend expects premultiplied)
```csharp
float a = MathHelper.Clamp(R + 0.5f - d, 0f, 1f);                     // 1px AA edge, d = distance from centre
data[i] = a <= 0 ? Color.Transparent : new Color(col.X*a, col.Y*a, col.Z*a, a);
// normal-map sprites: encode then premultiply so the edge fades to whatever is beneath: ((n*0.5+0.5)*a, a)
```
Radially symmetric dome normal (`n = normalize(dx/R*0.8, dy/R*0.8, sqrt(1-t²)*0.6+0.4)`) survives sprite rotation.

## Shape-list sprites - the workhorse for top-down characters & item icons
```csharp
var s = new ShapeSprite(96);                       // sprite-local: +X forward, +Y right, origin = centre
s.Ellipse(cx, cy, rx, ry, color, height, dome=1, shade=.35);  s.Circle(...);  s.Capsule(x0,y0,x1,y1, r, color, height, dome);
s.Box(x0,y0,x1,y1, color, height, dome=.3, shade=.2);
var pair = new SpritePair(s.CreateAlbedo(gd), s.CreateNormal(gd, reliefPx: 8f, strength: 0.35f));
```
Painter's order; AA coverage per shape (`Coverage()` returns cov 0..1 and radial t); albedo darkens toward each dome's
rim (`1 - dome*shade*t`); the normal map is a height field (`Height * lerp(1, sqrt(1-t^2), Dome) * reliefPx`) run through
`HeightToNormal`, both premultiplied by coverage so edges blend onto the floor. Character sprites (bodies, boots, held
items, parameterised by a style record) and small item icons (~28 px) are just shape lists built from the same primitives.

## Particle glow
```csharp
float a = Clamp(1 - d/c, 0, 1); a = a*a*(3-2*a); a *= a;  data[i] = new Color(a, a, a, a);
```

## MGCB settings if you swap in real PNGs later
Normal maps: `ColorKeyEnabled=False`, `PremultiplyAlpha=False`, `TextureFormat=Color`; tiled albedo: `GenerateMipmaps=True`.
