using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Core;
using Game.World;
using Xunit;

namespace Game.Tests;

public class CollisionTests
{
    [Fact]
    public void SegmentVsRect_HitsLeftFace_WithOutwardNormal()
    {
        var r = new Rectangle(100, 100, 50, 50);
        Assert.True(Collision.SegmentVsRect(new Vector2(0, 125), new Vector2(200, 125), r, out float t, out var n));
        Assert.Equal(0.5f, t, 3);
        Assert.Equal(new Vector2(-1, 0), n);
    }

    [Fact]
    public void SegmentVsRect_MissesWhenBeside()
    {
        var r = new Rectangle(100, 100, 50, 50);
        Assert.False(Collision.SegmentVsRect(new Vector2(0, 50), new Vector2(200, 50), r, out _, out _));
        Assert.False(Collision.SegmentVsRect(new Vector2(0, 125), new Vector2(90, 125), r, out _, out _));   // stops short
    }

    [Fact]
    public void SegmentVsRect_StartingInside_ReportsZero()
    {
        var r = new Rectangle(100, 100, 50, 50);
        Assert.True(Collision.SegmentVsRect(new Vector2(125, 125), new Vector2(300, 125), r, out float t, out var n));
        Assert.Equal(0f, t, 4);
        Assert.True(n.Length() > 0.99f);
    }

    [Fact]
    public void SegmentVsCircle_FirstIntersection()
    {
        Assert.True(Collision.SegmentVsCircle(new Vector2(0, 0), new Vector2(100, 0), new Vector2(50, 0), 10f, out float t));
        Assert.Equal(0.4f, t, 3);
        Assert.False(Collision.SegmentVsCircle(new Vector2(0, 20), new Vector2(100, 20), new Vector2(50, 0), 10f, out _));
    }

    [Fact]
    public void ResolveCircleRect_PushesOut_AndKillsVelocityIntoWall()
    {
        var r = new Rectangle(100, 100, 50, 50);
        var pos = new Vector2(95, 125); var vel = new Vector2(50, 10);      // overlapping the left face by 5 px (radius 10)
        Assert.True(Collision.ResolveCircleRect(ref pos, ref vel, 10f, r));
        Assert.Equal(90f, pos.X, 3);
        Assert.Equal(0f, vel.X, 3);
        Assert.Equal(10f, vel.Y, 3);
        var far = new Vector2(0, 0); var v2 = new Vector2(1, 1);
        Assert.False(Collision.ResolveCircleRect(ref far, ref v2, 10f, r));
    }

    [Fact]
    public void SeparateCircles_SplitsOverlapEvenly()
    {
        var a = new Vector2(0, 0); var b = new Vector2(10, 0);
        Collision.SeparateCircles(ref a, 10f, ref b, 10f);                // overlap 10 → each moves 5
        Assert.Equal(-5f, a.X, 3); Assert.Equal(15f, b.X, 3);
    }
}

public class CameraTests
{
    [Fact]
    public void Follow_ClampsInsideWorld()
    {
        var cam = new Camera2D();
        cam.SnapTo(new Vector2(10, 10), new Vector2(1280, 720));
        for (int i = 0; i < 100; i++) cam.Follow(new Vector2(-500, -500), 1f / 60f, new Vector2(1280, 720), 3072);
        Assert.Equal(640f, cam.Position.X, 1);                            // half view width at zoom 1
        Assert.Equal(360f, cam.Position.Y, 1);
    }

    [Fact]
    public void Follow_LargeViewport_RaisesMinimumZoom_SoViewNeverExceedsWorld()
    {
        var cam = new Camera2D { TargetZoom = 0.5f, Zoom = 0.5f };
        cam.SnapTo(new Vector2(1536, 1536), new Vector2(2560, 1440));
        for (int i = 0; i < 200; i++) cam.Follow(new Vector2(1536, 1536), 1f / 60f, new Vector2(2560, 1440), 3072);
        Assert.True(cam.Zoom >= 2560f / 3072f - 1e-4f, $"zoom {cam.Zoom}");
        var half = new Vector2(2560, 1440) / (2f * cam.Zoom);
        Assert.True(half.X * 2 <= 3072 + 0.01f && half.Y * 2 <= 3072 + 0.01f);
    }

    [Fact]
    public void Follow_PerAxisCentering_WhenOnlyOneAxisOverflows()
    {
        // a very wide, short viewport: X axis wider than the world, Y axis not
        var cam = new Camera2D { MinZoom = 0.1f, TargetZoom = 0.5f, Zoom = 0.5f };
        cam.SnapTo(new Vector2(100, 100), new Vector2(8000, 400));
        // min zoom for world = 8000/3072 = 2.6 → X view = 3076 >= world → centred; Y view = 154 → follows
        for (int i = 0; i < 300; i++) cam.Follow(new Vector2(100, 100), 1f / 60f, new Vector2(8000, 400), 3072);
        Assert.Equal(1536f, cam.Position.X, 0);
        Assert.True(cam.Position.Y < 200f, $"y {cam.Position.Y}");
    }

    [Fact]
    public void ScreenWorld_RoundTrip()
    {
        var cam = new Camera2D { Zoom = 1.5f, TargetZoom = 1.5f };
        cam.SnapTo(new Vector2(1000, 800), new Vector2(1280, 720));
        var w = new Vector2(1234, 567);
        var s = cam.WorldToScreen(w);
        var back = cam.ScreenToWorld(s);
        Assert.Equal(w.X, back.X, 2); Assert.Equal(w.Y, back.Y, 2);
        Assert.Equal(640f, cam.WorldToScreen(new Vector2(1000, 800)).X, 2);   // camera position maps to screen centre
    }

    [Fact]
    public void ApplyScroll_ClampsZoom()
    {
        var cam = new Camera2D();
        for (int i = 0; i < 50; i++) cam.ApplyScroll(120);
        Assert.Equal(cam.MaxZoom, cam.TargetZoom, 3);
        for (int i = 0; i < 50; i++) cam.ApplyScroll(-120);
        Assert.Equal(cam.MinZoom, cam.TargetZoom, 3);
    }
}

public class GameWorldTests
{
    public static IEnumerable<object[]> Maps() { foreach (var m in MapDef.All) yield return new object[] { m.Id }; }

    [Theory]
    [MemberData(nameof(Maps))]
    public void Generate_ProducesValidLayout(string mapId)
    {
        var map = MapDef.ById(mapId);
        var world = new GameWorld();
        var spawn = new Vector2(map.Size * 0.5f);
        world.Generate(map, spawn);

        Assert.Equal(map.Size, world.Size);
        Assert.True(world.Crates.Count >= map.PropCount * 0.6f, $"only {world.Crates.Count} props");
        Assert.Equal(map.Extracts.Count, world.Extracts.Count);
        Assert.Equal(map.LampGrid * map.LampGrid, world.Lamps.Count);
        Assert.False(world.CircleOverlapsAny(spawn, 60f), "spawn blocked");
        foreach (var c in world.Crates)
        {
            Assert.True(world.Bounds.Contains(c.Bounds), $"prop outside world: {c.Bounds}");
            foreach (var ez in world.Extracts) Assert.False(ez.Area.Intersects(c.Bounds), "prop inside an extract");
        }
        foreach (var ez in world.Extracts) Assert.True(world.Bounds.Contains(ez.Area));
        // deterministic for the same seed
        var again = new GameWorld(); again.Generate(map, spawn);
        Assert.Equal(world.Crates.Count, again.Crates.Count);
        for (int i = 0; i < world.Crates.Count; i++) Assert.Equal(world.Crates[i].Bounds, again.Crates[i].Bounds);
    }

    [Fact]
    public void CastSegment_FindsNearestCrate_AndWorldEdge()
    {
        var world = new GameWorld();
        world.Generate(MapDef.Scrapyard, new Vector2(1536, 1536));
        var c = world.Crates[0];
        var from = c.Center - new Vector2(c.Bounds.Width, 0); // left of the crate, aimed through it
        Assert.True(world.CastSegment(from, c.Center, out float t, out var n, out var hit));
        Assert.Same(c, hit); Assert.True(t > 0f && t < 1f); Assert.Equal(new Vector2(-1, 0), n);
        // props keep an 80 px margin from the floor edge, so a segment in the edge strip hits the world edge itself
        Assert.True(world.CastSegment(new Vector2(world.Size - 10, world.Size * 0.37f), new Vector2(world.Size + 100, world.Size * 0.37f), out float te, out var ne, out var he));
        Assert.Null(he);
        Assert.Equal(10f / 110f, te, 3);
        Assert.Equal(new Vector2(-1, 0), ne);
    }

    [Fact]
    public void ExtractQueries_Work()
    {
        var world = new GameWorld();
        world.Generate(MapDef.Docks, new Vector2(1792, 1792));
        var ez = world.Extracts[0];
        Assert.Same(ez, world.ExtractAt(ez.Center));
        Assert.Null(world.ExtractAt(new Vector2(1792, 1792)));
        Assert.NotNull(world.NearestExtract(new Vector2(1792, 1792)));
    }

    [Fact]
    public void LootableCrate_ContentsRollOnce()
    {
        Rng.Seed(9);
        var crate = new Crate { Lootable = true, Loot = Game.Items.LootTable.Crate };
        var a = crate.EnsureContents(); var b = crate.EnsureContents();
        Assert.Same(a, b);
        Assert.False(a.IsEmpty);
    }
}
