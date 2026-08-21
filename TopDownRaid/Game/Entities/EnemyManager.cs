using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Core;
using Game.Graphics;
using Game.Items;
using Game.World;

namespace Game.Entities;

/// <summary>Spawns, updates, separates and draws enemies; keeps a target population that grows with kills.</summary>
public sealed class EnemyManager
{
    private readonly List<Enemy> _enemies = new();
    private readonly List<Enemy> _alive = new();
    private readonly List<Character> _allCharacters = new();
    private readonly Dictionary<EnemyKind, CharacterRig> _rigs = new();
    private readonly LightManager _lights;
    private float _spawnTimer;

    public IReadOnlyList<Enemy> All => _enemies;
    public IReadOnlyList<Enemy> Alive => _alive;
    public int Kills { get; private set; }
    public int MaxAlive = 10;
    public float SpawnMinDistance = 950f;
    public float GunnerChance = 0.45f;
    public event Action<Enemy>? EnemyKilled;

    private readonly Dictionary<ItemType, SpritePair>? _attachArt;
    public EnemyManager(GraphicsDevice gd, LightManager lights, Dictionary<ItemType, SpritePair>? attachArt = null)
    {
        _lights = lights; _attachArt = attachArt;
        _rigs[EnemyKind.Brawler] = CharacterArt.CreateRig(gd, EnemyDef.Brawler.Style);
        _rigs[EnemyKind.Gunner]  = CharacterArt.CreateRig(gd, EnemyDef.Gunner.Style,
            new[] { HeldWeapon.Pistol, HeldWeapon.Rifle, HeldWeapon.Smg, HeldWeapon.Shotgun });   // arms for every gun they can roll
    }

    public Enemy Spawn(EnemyDef def, Vector2 pos, GameContext ctx)
    {
        var e = new Enemy(def, pos, _rigs[def.Kind], _lights, _attachArt);
        e.Died += () =>
        {
            Kills++;
            ctx.Score += def.ScoreValue;
            // loot stays ON the body (search it with E) — see Enemy.FillLoot
            ctx.Particles.Puff(e.Position, new Vector2(0, -1), 8, new Vector3(0.45f, 0.10f, 0.08f), 70f, 14f, 0.8f);
            EnemyKilled?.Invoke(e);
        };
        _enemies.Add(e);
        return e;
    }

    /// <summary>Population target: 4 at the start, +1 per 3 kills, capped at MaxAlive.</summary>
    public int TargetAlive => Math.Min(MaxAlive, 4 + Kills / 3);

    public void Update(float dt, GameContext ctx)
    {
        // spawn up to the target population, away from the player and clear of crates
        _spawnTimer -= dt;
        if (_alive.Count < TargetAlive && _spawnTimer <= 0f)
        {
            _spawnTimer = 1.2f;
            var def = Rng.Chance(GunnerChance) ? EnemyDef.Gunner : EnemyDef.Brawler;
            var pos = ctx.World.RandomClearPoint(ctx.Player.Position, SpawnMinDistance, 26f);
            Spawn(def, pos, ctx);
        }

        foreach (var e in _enemies) e.Update(dt, ctx);

        // pairwise separation so they don't stack (alive only)
        for (int i = 0; i < _enemies.Count; i++)
        {
            if (!_enemies[i].IsAlive) continue;
            for (int j = i + 1; j < _enemies.Count; j++)
            {
                if (!_enemies[j].IsAlive) continue;
                var a = _enemies[i].Position; var b = _enemies[j].Position;
                Collision.SeparateCircles(ref a, _enemies[i].Radius, ref b, _enemies[j].Radius);
                _enemies[i].Position = a; _enemies[j].Position = b;
            }
        }

        _enemies.RemoveAll(e => e.ReadyToRemove);
        _alive.Clear(); foreach (var e in _enemies) if (e.IsAlive) _alive.Add(e);
    }

    /// <summary>Everyone bullets can hit: the player plus every living enemy.</summary>
    public IReadOnlyList<Character> HittableCharacters(Player player)
    {
        _allCharacters.Clear(); _allCharacters.Add(player); _allCharacters.AddRange(_alive);
        return _allCharacters;
    }

    public void ResetAggro() { foreach (var e in _alive) e.CalmDown(); }

    /// <summary>Gunfire / explosions: every living enemy within radius starts hunting the player.</summary>
    public void AlertNear(Vector2 pos, float radius)
    {
        foreach (var e in _alive) if ((e.Position - pos).LengthSquared() < radius * radius) e.Aggro();
    }

    /// <summary>Nearest dead body within reach that still has something in it.</summary>
    public Enemy? FindLootableBodyNear(Vector2 pos, float reach)
    {
        Enemy? best = null; float bestD = reach;
        foreach (var e in _enemies)
        {
            if (e.IsAlive || e.CorpseAlpha <= 0.5f) continue;
            bool hasItems = false; for (int i = 0; i < e.Loot.Count; i++) if (!e.Loot[i].IsEmpty) { hasItems = true; break; }
            if (!hasItems) continue;
            float d = (e.Position - pos).Length();
            if (d < bestD) { bestD = d; best = e; }
        }
        return best;
    }

    public void Draw(SceneBatch batch, RectangleF visible)
    {
        // dead first (they lie under the living), then alive
        foreach (var e in _enemies) if (!e.IsAlive && visible.Inflate(80).Contains(e.Position)) e.Draw(batch);
        foreach (var e in _enemies) if (e.IsAlive && visible.Inflate(80).Contains(e.Position)) e.Draw(batch);
    }
}
