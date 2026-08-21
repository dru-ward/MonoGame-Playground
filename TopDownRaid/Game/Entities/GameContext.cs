using Game.Combat;
using Game.Core;
using Game.Graphics;
using Game.Items;
using Game.World;

namespace Game.Entities;

/// <summary>Bag of shared systems handed to entities each frame (avoids long constructor chains).</summary>
public sealed class GameContext
{
    public required GameWorld World { get; init; }
    public required ParticleSystem Particles { get; init; }
    public required LightManager Lights { get; init; }
    public required ProjectileSystem Projectiles { get; init; }
    public required GrenadeSystem Grenades { get; init; }
    public required PickupManager Pickups { get; init; }
    public required Camera2D Camera { get; init; }
    public required InputState Input { get; init; }
    public Player Player { get; set; } = null!;
    public EnemyManager Enemies { get; set; } = null!;
    public float Time;                     // seconds since start (unpaused)
    public int Score;
    public int Kills;
}
