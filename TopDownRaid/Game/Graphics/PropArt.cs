using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.World;

namespace Game.Graphics;

/// <summary>
/// Procedural urban props (top-down): shipping container, concrete jersey barrier, sandbag wall, rubble pile,
/// burning barrel, lamp post base, plus the wooden crate. Long props are generated in both orientations.
/// All are <see cref="ShapeSprite"/> lists with grime noise so nothing looks clean.
/// </summary>
public static class PropArt
{
    public static Dictionary<(PropKind kind, bool vertical), SpritePair> CreateAll(GraphicsDevice gd)
    {
        var d = new Dictionary<(PropKind, bool), SpritePair>();
        foreach (PropKind k in System.Enum.GetValues<PropKind>())
        {
            d[(k, false)] = Create(gd, k, false);
            d[(k, true)] = PropDefs.IsLong(k) ? Create(gd, k, true) : d[(k, false)];
        }
        return d;
    }

    /// <summary>Painted extraction marker: worn yellow frame with hazard ticks and an X, plus a green smoke-flare disc.</summary>
    public static SpritePair CreateExtractMarker(GraphicsDevice gd, int w, int h)
    {
        var s = new ShapeSprite(w, h) { GrimeAmount = 0.5f, GrimeSeed = 21, GrimeScale = 0.09f };
        Vector3 paint = new(0.85f, 0.72f, 0.20f), dark = new(0.12f, 0.12f, 0.10f);
        float hw = w / 2f - 4, hh = h / 2f - 4;
        s.Box(-hw, -hh, hw, -hh + 6, paint, 0.12f, 0f); s.Box(-hw, hh - 6, hw, hh, paint, 0.12f, 0f);
        s.Box(-hw, -hh, -hw + 6, hh, paint, 0.12f, 0f); s.Box(hw - 6, -hh, hw, hh, paint, 0.12f, 0f);
        for (float x = -hw + 12; x < hw - 12; x += 24f) { s.Box(x, -hh + 8, x + 10, -hh + 14, paint * 0.9f, 0.1f, 0f); s.Box(x, hh - 14, x + 10, hh - 8, paint * 0.9f, 0.1f, 0f); }
        s.Capsule(-hw * 0.55f, -hh * 0.55f, hw * 0.55f, hh * 0.55f, 3f, paint * 0.8f, 0.1f, 0f);
        s.Capsule(-hw * 0.55f, hh * 0.55f, hw * 0.55f, -hh * 0.55f, 3f, paint * 0.8f, 0.1f, 0f);
        s.Circle(0f, 0f, 11f, dark, 0.6f, 0.6f);                       // flare canister
        s.Circle(0f, 0f, 6f, new Vector3(0.25f, 0.6f, 0.3f), 0.7f, 0.9f);
        return s.Build(gd, 3f, 0.3f);
    }

    private static SpritePair Create(GraphicsDevice gd, PropKind kind, bool vertical)
    {
        var size = PropDefs.Size(kind);
        int inflate = PropDefs.DrawInflate(kind);                       // canopy sprites draw past their collision box
        int w = size.X + inflate * 2, h = size.Y + inflate * 2;
        Vector3 concrete = new(0.46f, 0.45f, 0.42f), rust = new(0.36f, 0.20f, 0.12f), steel = new(0.17f, 0.22f, 0.28f);
        Vector3 dark = new(0.08f, 0.08f, 0.08f), sand = new(0.52f, 0.46f, 0.33f), rock = new(0.30f, 0.30f, 0.29f);
        Vector3 leaf = new(0.13f, 0.22f, 0.08f), leafHi = new(0.23f, 0.34f, 0.12f), glass = new(0.05f, 0.07f, 0.08f);
        ShapeSprite s;
        switch (kind)
        {
            case PropKind.WoodCrate:
                // planked crate with metal corner brackets
                s = new ShapeSprite(w, h) { GrimeAmount = 0.35f, GrimeSeed = 3 };
                s.Box(-46f, -46f, 46f, 46f, new Vector3(0.42f, 0.30f, 0.17f), 0.55f, 0.15f);
                for (int i = 0; i < 4; i++) { float y0 = -46f + i * 23f; s.Box(-44f, y0 + 1f, 44f, y0 + 21f, new Vector3(0.48f + 0.05f * (i % 2), 0.34f, 0.19f), 0.62f, 0.25f); }
                s.Box(-46f, -46f, -34f, 46f, new Vector3(0.36f, 0.25f, 0.14f), 0.68f, 0.2f);
                s.Box(34f, -46f, 46f, 46f, new Vector3(0.36f, 0.25f, 0.14f), 0.68f, 0.2f);
                foreach (var (cx, cy) in new[] { (-40f, -40f), (40f, -40f), (-40f, 40f), (40f, 40f) }) s.Box(cx - 6f, cy - 6f, cx + 6f, cy + 6f, steel * 0.7f, 0.74f, 0.2f);
                break;

            case PropKind.Container:
                // corrugated shipping container, doors at +X end
                s = new ShapeSprite(w, h, vertical) { GrimeAmount = 0.5f, GrimeSeed = 7, GrimeScale = 0.06f };
                s.Box(-w / 2f + 1, -h / 2f + 1, w / 2f - 1, h / 2f - 1, steel * 0.8f, 0.9f, 0.1f);
                for (float x = -w / 2f + 8f; x < w / 2f - 12f; x += 10f) s.Box(x, -h / 2f + 4f, x + 4f, h / 2f - 4f, steel * 1.15f, 0.98f, 0.5f);   // corrugation ridges
                s.Box(w / 2f - 12f, -h / 2f + 3f, w / 2f - 2f, h / 2f - 3f, steel * 0.9f, 0.96f, 0.2f);                                          // door end
                s.Box(w / 2f - 8f, -3f, w / 2f - 4f, 3f, dark, 1.0f, 0.3f);
                s.Box(-w / 2f + 3f, -h / 2f + 3f, -w / 2f + 12f, h / 2f - 3f, rust * 0.9f, 0.94f, 0.2f);                                          // rusty end
                s.Ellipse(-w * 0.15f, h * 0.2f, 22f, 12f, rust, 0.95f, 0.2f, 0.1f);                                                             // rust bloom
                break;

            case PropKind.Barrier:
                // jersey barrier: wide base, narrower raised top, chipped ends
                s = new ShapeSprite(w, h, vertical) { GrimeAmount = 0.35f, GrimeSeed = 11, GrimeScale = 0.08f };
                s.Box(-w / 2f + 1, -h / 2f + 1, w / 2f - 1, h / 2f - 1, concrete * 0.85f, 0.5f, 0.3f);
                s.Box(-w / 2f + 3, -h / 2f + 8, w / 2f - 3, h / 2f - 8, concrete, 0.8f, 0.5f);
                s.Box(-w / 2f + 4, -6f, w / 2f - 4, 6f, concrete * 1.08f, 1.0f, 0.6f);
                s.Box(-w / 2f + 1, -4f, -w / 2f + 12f, 4f, dark, 0.7f, 0.2f);                                                          // lifting slot
                s.Box(w / 2f - 12f, -4f, w / 2f - 1f, 4f, dark, 0.7f, 0.2f);
                s.Ellipse(-w * 0.3f, -h * 0.3f, 7f, 5f, concrete * 0.6f, 0.75f, 0.9f);                                                 // chips
                s.Ellipse(w * 0.35f, h * 0.25f, 5f, 4f, concrete * 0.55f, 0.75f, 0.9f);
                break;

            case PropKind.Sandbags:
                // two staggered rows of bags
                s = new ShapeSprite(w, h, vertical) { GrimeAmount = 0.3f, GrimeSeed = 5, GrimeScale = 0.15f };
                for (int row = 0; row < 2; row++)
                {
                    float y = -h / 4f + row * h / 2f; float off = row * 12f;
                    for (float x = -w / 2f + 12f + off; x < w / 2f - 6f; x += 24f)
                        s.Capsule(x - 7f, y, x + 7f, y, h / 4.6f, sand * (0.9f + 0.1f * ((int)(x / 24f + row) % 2)), 0.9f, 1.0f, 0.5f);
                }
                break;

            case PropKind.Rubble:
                // rocks, concrete chunks and rebar
                s = new ShapeSprite(w, h) { GrimeAmount = 0.4f, GrimeSeed = 9 };
                s.Ellipse(0f, 0f, 44f, 40f, rock * 0.55f, 0.15f, 0.2f);                                       // dust footprint
                s.Ellipse(-14f, -10f, 22f, 16f, rock, 0.6f, 1.0f, 0.5f);
                s.Ellipse(16f, 8f, 18f, 14f, rock * 1.1f, 0.7f, 1.0f, 0.5f);
                s.Box(-8f, 12f, 22f, 26f, concrete * 0.8f, 0.55f, 0.3f);
                s.Box(-30f, 6f, -12f, 30f, concrete * 0.7f, 0.5f, 0.3f);
                s.Ellipse(4f, -22f, 12f, 9f, rock * 0.9f, 0.5f, 1.0f, 0.5f);
                s.Capsule(-32f, -28f, 30f, -18f, 1.5f, rust * 0.8f, 0.8f, 0.9f);                              // rebar
                s.Capsule(-20f, 34f, 34f, 20f, 1.4f, rust * 0.7f, 0.8f, 0.9f);
                break;

            case PropKind.FireBarrel:
                // rusted oil drum seen from above; the fire itself is particles + a light
                s = new ShapeSprite(w, h) { GrimeAmount = 0.45f, GrimeSeed = 13, GrimeScale = 0.3f };
                s.Circle(0f, 0f, 19f, rust * 0.7f, 0.9f, 0.4f, 0.4f);
                s.Circle(0f, 0f, 16f, rust, 1.0f, 0.2f, 0.2f);
                s.Circle(0f, 0f, 12f, new Vector3(0.05f, 0.04f, 0.03f), 0.6f, 0.9f, 0.1f);                     // dark opening
                break;

            case PropKind.LampBase:
                s = new ShapeSprite(w, h) { GrimeAmount = 0.3f, GrimeSeed = 17 };
                s.Circle(0f, 0f, 14f, concrete * 0.8f, 0.5f, 0.3f);
                s.Circle(0f, 0f, 6f, steel * 0.7f, 1.2f, 0.9f, 0.5f);
                break;

            case PropKind.Tree:
                // top-down canopy: dark skirt, lobed crown, lit clusters toward the upper-left
                s = new ShapeSprite(w, h) { GrimeAmount = 0.25f, GrimeSeed = 23, GrimeScale = 0.05f };
                s.Circle(0f, 0f, w * 0.48f, leaf * 0.5f, 0.15f, 0.35f);
                s.Circle(0f, 0f, w * 0.42f, leaf, 0.7f, 0.65f, 0.5f);
                for (int i = 0; i < 6; i++)                                                             // rim lobes
                {
                    float a = i * MathHelper.TwoPi / 6f + 0.4f, rr = w * (0.30f + 0.04f * (i % 3));
                    s.Ellipse(MathF.Cos(a) * rr, MathF.Sin(a) * rr, w * 0.16f, w * 0.13f, leaf * (0.85f + 0.15f * (i % 2)), 0.8f, 0.9f, 0.5f);
                }
                s.Ellipse(-w * 0.16f, -w * 0.14f, w * 0.18f, w * 0.15f, leafHi, 0.95f, 0.9f, 0.5f);     // sunlit side
                s.Ellipse(w * 0.10f, -w * 0.19f, w * 0.13f, w * 0.11f, leaf * 1.25f, 0.9f, 0.9f, 0.5f);
                s.Ellipse(w * 0.16f, w * 0.12f, w * 0.14f, w * 0.12f, leaf * 0.8f, 0.85f, 0.9f, 0.5f);  // shaded side
                s.Circle(-w * 0.05f, -w * 0.05f, w * 0.09f, leafHi * 1.15f, 1.0f, 0.9f, 0.5f);
                break;

            case PropKind.Bush:
                s = new ShapeSprite(w, h) { GrimeAmount = 0.3f, GrimeSeed = 29, GrimeScale = 0.1f };
                s.Circle(0f, 0f, w * 0.45f, leaf * 0.45f, 0.15f, 0.35f);
                s.Ellipse(-w * 0.15f, -w * 0.08f, w * 0.28f, w * 0.23f, leaf * 0.9f, 0.65f, 0.85f, 0.5f);
                s.Ellipse(w * 0.15f, w * 0.05f, w * 0.25f, w * 0.20f, leaf * 1.1f, 0.7f, 0.85f, 0.5f);
                s.Ellipse(0f, w * 0.13f, w * 0.22f, w * 0.17f, leaf * 0.8f, 0.65f, 0.85f, 0.5f);
                s.Ellipse(-w * 0.05f, -w * 0.15f, w * 0.15f, w * 0.12f, leafHi, 0.8f, 0.9f, 0.5f);
                break;

            case PropKind.CarWreck:
                // abandoned saloon seen from above: faded paint, dark glass, rust blooms, tires poking out
                s = new ShapeSprite(w, h, vertical) { GrimeAmount = 0.5f, GrimeSeed = 31, GrimeScale = 0.07f };
                Vector3 body = new(0.18f, 0.26f, 0.28f);
                foreach (float tx in new[] { -w * 0.32f, w * 0.32f })                                   // tires
                {
                    s.Box(tx - 15f, -h / 2f + 1f, tx + 15f, -h / 2f + 12f, dark, 0.5f, 0.2f);
                    s.Box(tx - 15f, h / 2f - 12f, tx + 15f, h / 2f - 1f, dark, 0.5f, 0.2f);
                }
                s.Box(-w / 2f + 6f, -h / 2f + 10f, w / 2f - 6f, h / 2f - 10f, body * 0.85f, 0.8f, 0.45f);   // shell
                s.Box(-w / 2f + 10f, -h / 2f + 14f, -w * 0.16f, h / 2f - 14f, body, 0.9f, 0.5f);            // bonnet
                s.Box(w * 0.20f, -h / 2f + 14f, w / 2f - 10f, h / 2f - 14f, body * 1.05f, 0.9f, 0.5f);      // boot
                s.Box(-w * 0.13f, -h / 2f + 13f, w * 0.17f, h / 2f - 13f, body * 1.2f, 1.0f, 0.55f);        // roof
                s.Box(-w * 0.20f, -h / 2f + 16f, -w * 0.13f, h / 2f - 16f, glass, 0.95f, 0.2f);             // windscreen
                s.Box(w * 0.17f, -h / 2f + 16f, w * 0.24f, h / 2f - 16f, glass, 0.95f, 0.2f);               // rear glass
                s.Ellipse(-w * 0.35f, -h * 0.2f, 20f, 12f, rust, 0.85f, 0.3f);
                s.Ellipse(w * 0.33f, h * 0.18f, 16f, 10f, rust * 0.85f, 0.85f, 0.3f);
                s.Ellipse(w * 0.05f, -h * 0.28f, 12f, 8f, rust * 0.7f, 0.9f, 0.3f);
                break;

            case PropKind.Grass:
                // walk-through tuft: a fan of blades radiating from the base
                s = new ShapeSprite(w, h);
                for (int i = 0; i < 8; i++)
                {
                    float a = i * MathHelper.TwoPi / 8f + (i * 37 % 5) * 0.11f;
                    float len = w * 0.26f + (i * 53 % 7) * 1.5f;
                    s.Capsule(0f, 0f, MathF.Cos(a) * len, MathF.Sin(a) * len, 1.6f, leaf * (0.85f + 0.12f * (i % 3)), 0.7f, 0.9f);
                }
                s.Circle(0f, 0f, 4f, leafHi * 0.9f, 0.8f, 0.9f);
                break;

            default:
                s = new ShapeSprite(w, h); s.Box(-w / 2f, -h / 2f, w / 2f, h / 2f, concrete, 0.5f); break;
        }
        return new SpritePair(s.CreateAlbedo(gd), s.CreateNormal(gd, reliefPx: 10f, strength: 0.4f));
    }
}
