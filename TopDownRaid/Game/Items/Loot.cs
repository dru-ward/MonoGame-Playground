using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Core;
using Game.Graphics;

namespace Game.Items;

/// <summary>Weighted loot rolls: each roll picks one entry by weight and yields a random count in [Min, Max].</summary>
public sealed class LootTable
{
    public readonly record struct Entry(ItemType Type, float Weight, int Min, int Max);
    private readonly List<Entry> _entries = new();
    public int MinRolls = 1, MaxRolls = 2;
    public float NothingWeight = 0f;                 // extra weight for "no drop" per roll

    public LootTable Add(ItemType type, float weight, int min, int max) { _entries.Add(new Entry(type, weight, min, max)); return this; }

    public List<ItemStack> Roll()
    {
        var result = new List<ItemStack>();
        int rolls = Rng.Int(MinRolls, MaxRolls + 1);
        float total = NothingWeight; foreach (var e in _entries) total += e.Weight;
        for (int r = 0; r < rolls; r++)
        {
            float pick = Rng.Float() * total, acc = NothingWeight;
            if (pick < acc) continue;
            foreach (var e in _entries)
            {
                acc += e.Weight;
                if (pick < acc) { result.Add(new ItemStack(e.Type, Rng.Int(e.Min, e.Max + 1))); break; }
            }
        }
        return result;
    }

    // ---- shared tables ------------------------------------------------------------------------------------
    public static readonly LootTable Crate = new LootTable { MinRolls = 2, MaxRolls = 4 }
        .Add(ItemType.RifleMag, 4f, 1, 2).Add(ItemType.PistolMag, 4f, 1, 3).Add(ItemType.SmgMag, 2.5f, 1, 2).Add(ItemType.Shells, 2.5f, 1, 2)
        .Add(ItemType.Bandage, 3f, 1, 2).Add(ItemType.Medkit, 1.2f, 1, 1).Add(ItemType.ArmorPlate, 1.0f, 1, 1).Add(ItemType.Coin, 3f, 3, 12)
        .Add(ItemType.GunPistol, 0.5f, 1, 1).Add(ItemType.GunSmg, 0.35f, 1, 1).Add(ItemType.GunShotgun, 0.3f, 1, 1).Add(ItemType.GunRifle, 0.2f, 1, 1)
        .Add(ItemType.Grenade, 1.2f, 1, 2).Add(ItemType.MeleeBat, 0.4f, 1, 1)
        .Add(ItemType.VestLight, 0.6f, 1, 1).Add(ItemType.VestHeavy, 0.25f, 1, 1).Add(ItemType.HelmetSteel, 0.5f, 1, 1).Add(ItemType.HelmetTac, 0.25f, 1, 1)
        .Add(ItemType.Optic, 0.7f, 1, 1).Add(ItemType.Suppressor, 0.5f, 1, 1).Add(ItemType.Compensator, 0.5f, 1, 1).Add(ItemType.Torch, 0.7f, 1, 1).Add(ItemType.Laser, 0.5f, 1, 1).Add(ItemType.Grip, 0.6f, 1, 1);

    /// <summary>Extra rolls for shipping containers ("caches"): more gear and attachments.</summary>
    public static readonly LootTable Cache = new LootTable { MinRolls = 3, MaxRolls = 5 }
        .Add(ItemType.RifleMag, 3f, 1, 2).Add(ItemType.SmgMag, 2f, 1, 2).Add(ItemType.Shells, 2f, 1, 2).Add(ItemType.Medkit, 1.5f, 1, 1)
        .Add(ItemType.VestLight, 1.0f, 1, 1).Add(ItemType.VestHeavy, 0.5f, 1, 1).Add(ItemType.HelmetSteel, 0.8f, 1, 1).Add(ItemType.HelmetTac, 0.5f, 1, 1)
        .Add(ItemType.Optic, 1.2f, 1, 1).Add(ItemType.Suppressor, 0.9f, 1, 1).Add(ItemType.Compensator, 0.9f, 1, 1).Add(ItemType.Torch, 1.0f, 1, 1).Add(ItemType.Laser, 0.8f, 1, 1).Add(ItemType.Grip, 1.0f, 1, 1)
        .Add(ItemType.GunRifle, 0.5f, 1, 1).Add(ItemType.GunSmg, 0.5f, 1, 1).Add(ItemType.Grenade, 1.5f, 1, 3).Add(ItemType.Coin, 2f, 5, 15);

    /// <summary>What a Scav brawler carries besides his bat.</summary>
    public static readonly LootTable BrawlerBody = new LootTable { MinRolls = 1, MaxRolls = 3, NothingWeight = 1.5f }
        .Add(ItemType.Coin, 4f, 1, 6).Add(ItemType.Bandage, 2.5f, 1, 1).Add(ItemType.PistolMag, 1.5f, 1, 1).Add(ItemType.Shells, 0.7f, 1, 1)
        .Add(ItemType.MeleeBat, 0.8f, 1, 1).Add(ItemType.Grenade, 0.5f, 1, 1);

    /// <summary>Extras on a gunner body (his gun + mags for it are added separately).</summary>
    public static readonly LootTable GunnerBody = new LootTable { MinRolls = 0, MaxRolls = 2, NothingWeight = 2f }
        .Add(ItemType.Coin, 3f, 2, 8).Add(ItemType.Bandage, 2f, 1, 1).Add(ItemType.ArmorPlate, 0.6f, 1, 1).Add(ItemType.Medkit, 0.5f, 1, 1)
        .Add(ItemType.Grenade, 0.8f, 1, 1).Add(ItemType.HelmetSteel, 0.5f, 1, 1).Add(ItemType.VestLight, 0.4f, 1, 1)
        .Add(ItemType.Optic, 0.5f, 1, 1).Add(ItemType.Grip, 0.4f, 1, 1).Add(ItemType.Laser, 0.3f, 1, 1).Add(ItemType.Compensator, 0.3f, 1, 1);
}

/// <summary>An item stack lying on the floor. Bobs, gets magnetised toward a nearby player and is auto-collected.</summary>
public sealed class Pickup
{
    public ItemStack Stack;
    public Vector2 Position;
    public Vector2 Velocity;          // burst outward when spawned, then slides to a stop
    public float Age;
    public float SparkleTimer;
    public bool Collected;
    public ItemDef Def => Stack.Def;
}

/// <summary>Owns all pickups: spawning bursts from crates/enemies, magnet + collect, and drawing.</summary>
public sealed class PickupManager
{
    public const float MagnetRange = 90f;
    public const float CollectRange = 26f;
    private readonly List<Pickup> _pickups = new();
    private readonly Dictionary<ItemType, SpritePair> _icons;
    private readonly ParticleSystem _particles;
    public IReadOnlyList<Pickup> All => _pickups;

    public PickupManager(Dictionary<ItemType, SpritePair> icons, ParticleSystem particles) { _icons = icons; _particles = particles; }

    /// <summary>Scatters a list of stacks around a point (each pops out with a random velocity).</summary>
    public void SpawnBurst(Vector2 origin, IEnumerable<ItemStack> stacks, float speed = 160f)
    {
        foreach (var s in stacks)
        {
            if (s.IsEmpty) continue;
            var dir = Rng.UnitVector();
            _pickups.Add(new Pickup { Stack = s, Position = origin + dir * 6f, Velocity = dir * speed * (0.5f + Rng.Float()), SparkleTimer = Rng.Float() });
        }
    }

    public void Spawn(Vector2 pos, ItemStack stack) => _pickups.Add(new Pickup { Stack = stack, Position = pos });

    /// <summary>Moves pickups, magnetises them toward the player and returns those collected this frame.</summary>
    public void Update(float dt, Vector2 playerPos, bool playerAlive, Func<ItemStack, int> tryCollect, World.GameWorld world)
    {
        for (int i = _pickups.Count - 1; i >= 0; i--)
        {
            var p = _pickups[i];
            p.Age += dt;
            // slide to a stop after the spawn burst; never end up inside a crate
            if (p.Velocity.LengthSquared() > 1f)
            {
                p.Velocity *= MathF.Exp(-4f * dt);
                p.Position += p.Velocity * dt;
                var v = p.Velocity; world.ResolveCircle(ref p.Position, ref v, 8f); p.Velocity = v;
            }
            if (playerAlive)
            {
                var toPlayer = playerPos - p.Position; float d = toPlayer.Length();
                if (d < CollectRange)
                {
                    int leftover = tryCollect(p.Stack);
                    if (leftover < p.Stack.Count)
                    {
                        _particles.Sparks(p.Position, new Vector2(0, -1), 6, p.Def.Tint * 1.5f, 120f, 3.0f, 0.35f);
                        if (leftover <= 0) { _pickups.RemoveAt(i); continue; }
                        p.Stack.Count = leftover;
                    }
                }
                else if (d < MagnetRange && p.Age > 0.4f)
                {
                    p.Position += toPlayer / d * (260f * (1f - d / MagnetRange) + 60f) * dt;
                }
            }
            // occasional sparkle so pickups are visible in dark corners
            p.SparkleTimer -= dt;
            if (p.SparkleTimer <= 0f)
            {
                p.SparkleTimer = 0.5f + Rng.Float() * 0.8f;
                _particles.Emit(new Particle { Position = p.Position + Rng.InCircle(8f), Velocity = new Vector2(0, -25f), Lifetime = 0.6f, Size = 5f, Aspect = 1f, Color = p.Def.Tint * 1.6f, Drag = 1f, Emissive = true });
            }
        }
    }

    public void Draw(SceneBatch batch)
    {
        foreach (var p in _pickups)
        {
            float bob = MathF.Sin(p.Age * 3f) * 1.5f;
            batch.Draw(_icons[p.Stack.Type], p.Position + new Vector2(0, bob), 1f);
        }
    }
}
