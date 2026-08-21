using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game.World;

/// <summary>An extraction point: stand inside the zone for <see cref="ExtractZone.HoldSeconds"/> to leave the raid.</summary>
public sealed class ExtractZone
{
    public required string Name;
    public Rectangle Area;
    public float HoldSeconds = 5f;
    public Vector2 Center => new(Area.Center.X, Area.Center.Y);
    public bool Contains(Vector2 p) => Area.Contains((int)p.X, (int)p.Y);
}

/// <summary>Relative extract placement (0..1 of the map size) so one definition works for every map size.</summary>
public sealed record ExtractDef(string Name, float RelX, float RelY, int Width = 160, int Height = 160);

/// <summary>
/// Everything that makes one map different from another: size, seed, prop mix, lighting, enemy pressure, raid
/// timer and extraction points. The world generator and the raid read from this; the map-select screen lists them.
/// </summary>
public sealed record MapDef(
    string Id, string Name, string Difficulty, string Description,
    int Seed, int Size, int PropCount, int LampGrid, float ColdLampChance,
    int StartEnemies, int MaxAlive, float GunnerChance, float SpawnMinDistance,
    Vector3 Ambient, float RaidMinutes, IReadOnlyList<ExtractDef> Extracts, IReadOnlyDictionary<PropKind, float> PropWeights)
{
    public static readonly MapDef Scrapyard = new(
        "scrapyard", "SCRAPYARD", "EASY",
        "An open lot of crates, barrels and junk. Two exits on opposite corners. Good first raid.",
        Seed: 42, Size: 3072, PropCount: 52, LampGrid: 3, ColdLampChance: 0.25f,
        StartEnemies: 4, MaxAlive: 8, GunnerChance: 0.45f, SpawnMinDistance: 950f,
        Ambient: new(0.085f, 0.09f, 0.115f), RaidMinutes: 8f,
        Extracts: new[] { new ExtractDef("RAIL GATE", 0.93f, 0.07f), new ExtractDef("CULVERT", 0.06f, 0.93f) },
        PropWeights: new Dictionary<PropKind, float> { [PropKind.WoodCrate] = 24, [PropKind.Barrier] = 20, [PropKind.Container] = 12, [PropKind.Sandbags] = 16, [PropKind.Rubble] = 16, [PropKind.FireBarrel] = 12 });

    public static readonly MapDef Docks = new(
        "docks", "DOCKS", "MEDIUM",
        "Container terminal at night. Long sightlines between stacks, cold floodlights. More gunners, better loot.",
        Seed: 7, Size: 3584, PropCount: 74, LampGrid: 4, ColdLampChance: 0.5f,
        StartEnemies: 5, MaxAlive: 10, GunnerChance: 0.65f, SpawnMinDistance: 1000f,
        Ambient: new(0.07f, 0.085f, 0.12f), RaidMinutes: 10f,
        Extracts: new[] { new ExtractDef("CRANE", 0.5f, 0.05f, 220, 140), new ExtractDef("PIER 9", 0.95f, 0.55f, 140, 220), new ExtractDef("SERVICE ROAD", 0.05f, 0.5f, 140, 220) },
        PropWeights: new Dictionary<PropKind, float> { [PropKind.Container] = 34, [PropKind.Barrier] = 14, [PropKind.WoodCrate] = 16, [PropKind.Sandbags] = 8, [PropKind.Rubble] = 10, [PropKind.FireBarrel] = 8 });

    public static readonly MapDef Factory = new(
        "factory", "FACTORY", "HARD",
        "Tight, dark yard behind a burnt-out plant. Barriers and sandbag lines everywhere, barrels for light. Scavs swarm.",
        Seed: 1337, Size: 2560, PropCount: 64, LampGrid: 2, ColdLampChance: 0.0f,
        StartEnemies: 6, MaxAlive: 12, GunnerChance: 0.5f, SpawnMinDistance: 800f,
        Ambient: new(0.06f, 0.06f, 0.075f), RaidMinutes: 7f,
        Extracts: new[] { new ExtractDef("LOADING BAY", 0.92f, 0.92f), new ExtractDef("HOLE IN FENCE", 0.08f, 0.08f, 120, 120) },
        PropWeights: new Dictionary<PropKind, float> { [PropKind.Barrier] = 30, [PropKind.Sandbags] = 26, [PropKind.Rubble] = 20, [PropKind.WoodCrate] = 14, [PropKind.FireBarrel] = 18, [PropKind.Container] = 6 });

    public static readonly IReadOnlyList<MapDef> All = new[] { Scrapyard, Docks, Factory };
    public static MapDef ById(string id) { foreach (var m in All) if (m.Id == id) return m; return Scrapyard; }
}
