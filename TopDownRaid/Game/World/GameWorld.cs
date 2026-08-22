using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Core;
using Game.Items;

namespace Game.World;

public enum PropKind { WoodCrate, Container, Barrier, Sandbags, Rubble, FireBarrel, LampBase, Tree, Bush, CarWreck, Grass }

/// <summary>Floor material of a map (picks the tiling texture and, for grass, scatters tufts).</summary>
public enum FloorKind { Asphalt, Grass }

/// <summary>Sizes / traits of the props. Size is the collision box; some sprites draw larger (DrawInflate).</summary>
public static class PropDefs
{
    public static Point Size(PropKind k) => k switch
    {
        PropKind.WoodCrate  => new Point(96, 96),
        PropKind.Container  => new Point(224, 112),
        PropKind.Barrier    => new Point(192, 56),
        PropKind.Sandbags   => new Point(128, 56),
        PropKind.Rubble     => new Point(96, 96),
        PropKind.FireBarrel => new Point(40, 40),
        PropKind.LampBase   => new Point(32, 32),
        PropKind.Tree       => new Point(56, 56),      // trunk only — the canopy overhangs (DrawInflate)
        PropKind.Bush       => new Point(64, 64),
        PropKind.CarWreck   => new Point(228, 104),
        PropKind.Grass      => new Point(48, 48),      // decor tuft, never solid
        _ => new Point(96, 96),
    };
    /// <summary>Extra pixels the sprite extends past the collision box on each side (tree canopies).</summary>
    public static int DrawInflate(PropKind k) => k switch { PropKind.Tree => 62, PropKind.Bush => 8, _ => 0 };
    public static bool IsLong(PropKind k) => k is PropKind.Container or PropKind.Barrier or PropKind.Sandbags or PropKind.CarWreck;
    /// <summary>Lootable chance per kind (crates and containers are caches, car boots hold odds and ends).</summary>
    public static float LootChance(PropKind k) => k switch { PropKind.WoodCrate => 0.5f, PropKind.Container => 0.6f, PropKind.CarWreck => 0.4f, _ => 0f };
    public static string Name(PropKind k) => k switch { PropKind.WoodCrate => "CRATE", PropKind.Container => "CONTAINER", PropKind.CarWreck => "CAR BOOT", _ => k.ToString().ToUpperInvariant() };
}

/// <summary>A solid prop on the floor. Some are lootable containers that can be opened (E) or shot open.</summary>
public sealed class Crate
{
    public PropKind Kind = PropKind.WoodCrate;
    public bool Vertical;                 // long props rotated 90°
    public Rectangle Bounds;
    public bool Lootable;
    public bool Opened;
    public float Health = 3f;             // shots needed to break a lootable crate open
    public LootTable? Loot;
    public float HitFlash;                // white flash after being shot
    public Vector2 Center => new(Bounds.Center.X, Bounds.Center.Y);
    /// <summary>Rolled once on first open/break so searching and shooting agree on what was inside.</summary>
    public Inventory? Contents;
    public Inventory EnsureContents()
    {
        if (Contents == null)
        {
            Contents = new Inventory(Inventory.DefaultSlots);
            if (Loot != null) foreach (var st in Loot.Roll()) Contents.Add(st.Type, st.Count);
        }
        return Contents;
    }
}

/// <summary>
/// Static level data: floor extent, crates, and the collision/raycast queries every system uses.
/// </summary>
public sealed class GameWorld
{
    /// <summary>Floor extent in world pixels (set by the MapDef; multiples of the 512 px asphalt tile look best).</summary>
    public int Size { get; private set; } = 3072;
    public Rectangle Bounds => new(0, 0, Size, Size);
    /// <summary>Extraction zones for the current map.</summary>
    public List<ExtractZone> Extracts { get; } = new();
    public List<Crate> Crates { get; } = new();
    /// <summary>Non-blocking decoration (lamp post bases) — position + kind.</summary>
    public List<(Vector2 pos, PropKind kind)> Decor { get; } = new();
    /// <summary>Street-lamp positions (a light + a LampBase decor is placed at each).</summary>
    public List<Vector2> Lamps { get; } = new();

    public MapDef Map { get; private set; } = MapDef.Scrapyard;

    /// <summary>
    /// Deterministic urban layout from a <see cref="MapDef"/>: props by the map's weights, a jittered lamp grid,
    /// extraction zones kept clear of props, nothing on the spawn point.
    /// </summary>
    public void Generate(MapDef map, Vector2 spawn)
    {
        Map = map; Size = map.Size;
        var rng = new Random(map.Seed);
        Crates.Clear(); Decor.Clear(); Lamps.Clear(); Extracts.Clear();

        // extraction zones (relative placement, clamped inside the floor)
        foreach (var e in map.Extracts)
        {
            int x = (int)MathHelper.Clamp(e.RelX * Size - e.Width / 2f, 40, Size - e.Width - 40);
            int y = (int)MathHelper.Clamp(e.RelY * Size - e.Height / 2f, 40, Size - e.Height - 40);
            Extracts.Add(new ExtractZone { Name = e.Name, Area = new Rectangle(x, y, e.Width, e.Height) });
        }

        // lamps: jittered grid
        int g = map.LampGrid;
        for (int gy = 0; gy < g; gy++)
        for (int gx = 0; gx < g; gx++)
        {
            var p = new Vector2((gx + 0.5f) * Size / g + rng.Next(-260, 260), (gy + 0.5f) * Size / g + rng.Next(-260, 260));
            if ((p - spawn).Length() < 140f) p += new Vector2(200f, 0f);
            Lamps.Add(p); Decor.Add((p, PropKind.LampBase));
        }

        var weights = new List<(PropKind kind, float weight)>();
        foreach (var kv in map.PropWeights) if (kv.Value > 0f) weights.Add((kv.Key, kv.Value));
        float total = 0f; foreach (var w in weights) total += w.weight;

        int attempts = 0;
        while (Crates.Count < map.PropCount && attempts++ < map.PropCount * 25)
        {
            float pick = (float)rng.NextDouble() * total, acc = 0f; PropKind kind = PropKind.WoodCrate;
            foreach (var w in weights) { acc += w.weight; if (pick < acc) { kind = w.kind; break; } }
            bool vertical = PropDefs.IsLong(kind) && rng.Next(2) == 0;
            var sz = PropDefs.Size(kind); if (vertical) sz = new Point(sz.Y, sz.X);
            var r = new Rectangle(rng.Next(80, Size - sz.X - 80), rng.Next(80, Size - sz.Y - 80), sz.X, sz.Y);
            var padded = r; padded.Inflate(90, 90);
            if (padded.Contains((int)spawn.X, (int)spawn.Y)) continue;
            bool overlaps = false;
            foreach (var c in Crates) { var p = c.Bounds; p.Inflate(56, 56); if (p.Intersects(r)) { overlaps = true; break; } }
            foreach (var l in Lamps) { if (r.Contains((int)l.X, (int)l.Y) || (new Vector2(r.Center.X, r.Center.Y) - l).Length() < 90f) { overlaps = true; break; } }
            foreach (var ez in Extracts) { var a = ez.Area; a.Inflate(60, 60); if (a.Intersects(r)) { overlaps = true; break; } }
            if (overlaps) continue;
            bool lootable = rng.NextDouble() < PropDefs.LootChance(kind);
            Crates.Add(new Crate { Kind = kind, Vertical = vertical, Bounds = r, Lootable = lootable, Loot = lootable ? (kind == PropKind.Container ? LootTable.Cache : LootTable.Crate) : null,
                                   Health = kind switch { PropKind.Container => 5f, PropKind.CarWreck => 6f, _ => 3f } });
        }
        // grass floors get a scatter of walk-through tufts
        if (map.Floor == FloorKind.Grass)
            for (int i = Size * Size / 26000; i > 0; i--)
                Decor.Add((new Vector2(rng.Next(30, Size - 30), rng.Next(30, Size - 30)), PropKind.Grass));
        // barriers like to come in pairs: extend some barriers with a second segment end-to-end
        int n = Crates.Count;
        for (int i = 0; i < n; i++)
        {
            var c = Crates[i];
            if (c.Kind != PropKind.Barrier || rng.Next(2) == 0) continue;
            var r = c.Bounds; if (c.Vertical) r.Y += r.Height + 6; else r.X += r.Width + 6;
            if (r.Right > Size - 60 || r.Bottom > Size - 60) continue;
            bool overlaps = false; foreach (var o in Crates) { var p = o.Bounds; p.Inflate(20, 20); if (o != c && p.Intersects(r)) { overlaps = true; break; } }
            foreach (var ez in Extracts) { var a = ez.Area; a.Inflate(60, 60); if (a.Intersects(r)) { overlaps = true; break; } }
            if (!overlaps) Crates.Add(new Crate { Kind = PropKind.Barrier, Vertical = c.Vertical, Bounds = r });
        }
    }

    /// <summary>The extract zone the point is in, if any.</summary>
    public ExtractZone? ExtractAt(Vector2 p) { foreach (var e in Extracts) if (e.Contains(p)) return e; return null; }

    /// <summary>Nearest extract centre (for the compass).</summary>
    public ExtractZone? NearestExtract(Vector2 p)
    {
        ExtractZone? best = null; float bd = float.MaxValue;
        foreach (var e in Extracts) { float d = (e.Center - p).LengthSquared(); if (d < bd) { bd = d; best = e; } }
        return best;
    }

    // ------------------------------------------------------------------------------------------------ queries
    /// <summary>Resolves a moving circle against every crate (two iterations) and the world bounds.</summary>
    public void ResolveCircle(ref Vector2 pos, ref Vector2 vel, float radius)
    {
        for (int iter = 0; iter < 2; iter++)
            foreach (var c in Crates) Collision.ResolveCircleRect(ref pos, ref vel, radius, c.Bounds);
        var min = new Vector2(radius); var max = new Vector2(Size - radius);
        if (pos.X < min.X) { pos.X = min.X; if (vel.X < 0) vel.X = 0; }
        if (pos.Y < min.Y) { pos.Y = min.Y; if (vel.Y < 0) vel.Y = 0; }
        if (pos.X > max.X) { pos.X = max.X; if (vel.X > 0) vel.X = 0; }
        if (pos.Y > max.Y) { pos.Y = max.Y; if (vel.Y > 0) vel.Y = 0; }
    }

    public bool CircleOverlapsAny(Vector2 pos, float radius)
    {
        foreach (var c in Crates)
        {
            var closest = new Vector2(MathHelper.Clamp(pos.X, c.Bounds.Left, c.Bounds.Right), MathHelper.Clamp(pos.Y, c.Bounds.Top, c.Bounds.Bottom));
            if ((pos - closest).LengthSquared() < radius * radius) return true;
        }
        return false;
    }

    /// <summary>
    /// Casts a segment against crates and the world edge. Returns the nearest hit with its outward normal (used
    /// for ricochets) and the crate that was hit (null for the world edge).
    /// </summary>
    public bool CastSegment(Vector2 a, Vector2 b, out float t, out Vector2 normal, out Crate? crate)
    {
        t = float.MaxValue; normal = Vector2.Zero; crate = null;
        foreach (var c in Crates)
        {
            if (Collision.SegmentVsRect(a, b, c.Bounds, out float ct, out var cn) && ct < t) { t = ct; normal = cn; crate = c; }
        }
        // world edges as four half-planes
        var d = b - a; float bestT = t; var bestN = normal; Crate? bestC = crate;
        void Edge(float tt, Vector2 n) { if (tt >= 0f && tt <= 1f && tt < bestT) { bestT = tt; bestN = n; bestC = null; } }
        if (d.X < 0) Edge((0 - a.X) / d.X, new Vector2(1, 0));
        if (d.X > 0) Edge((Size - a.X) / d.X, new Vector2(-1, 0));
        if (d.Y < 0) Edge((0 - a.Y) / d.Y, new Vector2(0, 1));
        if (d.Y > 0) Edge((Size - a.Y) / d.Y, new Vector2(0, -1));
        t = bestT; normal = bestN; crate = bestC;
        return t <= 1f;
    }

    /// <summary>True if nothing solid lies between two points (AI line of sight).</summary>
    public bool HasLineOfSight(Vector2 a, Vector2 b)
    {
        foreach (var c in Crates)
            if (Collision.SegmentVsRect(a, b, c.Bounds, out _, out _)) return false;
        return true;
    }

    /// <summary>The nearest lootable, unopened crate whose edge is within reach of a point (for the E prompt).</summary>
    public Crate? FindLootableNear(Vector2 pos, float reach)
    {
        Crate? best = null; float bestD = reach;
        foreach (var c in Crates)
        {
            if (!c.Lootable || c.Opened) continue;
            var closest = new Vector2(MathHelper.Clamp(pos.X, c.Bounds.Left, c.Bounds.Right), MathHelper.Clamp(pos.Y, c.Bounds.Top, c.Bounds.Bottom));
            float d = (pos - closest).Length();
            if (d < bestD) { bestD = d; best = c; }
        }
        return best;
    }

    /// <summary>A random floor position at least minDist from 'from' and clear of crates.</summary>
    public Vector2 RandomClearPoint(Vector2 from, float minDist, float radius)
    {
        for (int i = 0; i < 200; i++)
        {
            var p = new Vector2(Rng.Range(radius + 40, Size - radius - 40), Rng.Range(radius + 40, Size - radius - 40));
            if ((p - from).Length() < minDist) continue;
            if (CircleOverlapsAny(p, radius + 8)) continue;
            return p;
        }
        return new Vector2(Size * 0.5f);
    }
}
