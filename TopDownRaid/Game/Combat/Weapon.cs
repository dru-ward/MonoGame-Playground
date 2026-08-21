using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Core;
using Game.Graphics;
using Game.Items;

namespace Game.Combat;

/// <summary>
/// Static weapon tuning. Ammo is handled as MAGAZINES: <see cref="Mag"/> is the inventory item one reload consumes
/// (a full magazine is loaded, whatever was left in the old one is lost — keeps the loot loop simple and tense).
/// </summary>
public sealed record WeaponDef(
    string Name, HeldWeapon Held, ItemType? Mag, ItemType? GunItem,
    float FireInterval, float BulletSpeed, float Damage, float Spread, float SprintSpread,
    int MagSize, float ReloadTime, int MaxRicochets, Vector3 Tracer, float MuzzleFlash, bool Automatic,
    int Pellets = 1, float PelletSpread = 0f, float Range = 500f, string Description = "", bool IsMelee = false)
{
    /// <summary>Fits in narrow UI slots.</summary>
    public string ShortName => Held switch { HeldWeapon.Rifle => "RIFLE", HeldWeapon.Pistol => "PISTOL", HeldWeapon.Smg => "SMG", HeldWeapon.Shotgun => "SHOTGUN", HeldWeapon.Bat => "BAT", _ => Name.ToUpperInvariant() };

    // ---------------------------------------------------------------- player-grade weapons
    public static readonly WeaponDef Rifle = new("Assault Rifle", HeldWeapon.Rifle, ItemType.RifleMag, ItemType.GunRifle,
        FireInterval: 0.11f, BulletSpeed: 1600f, Damage: 18f, Spread: 0.03f, SprintSpread: 0.09f,
        MagSize: 30, ReloadTime: 1.6f, MaxRicochets: 2, Tracer: new(1.6f, 1.3f, 0.7f), MuzzleFlash: 2.5f, Automatic: true,
        Range: 620f, Description: "5.56 auto. 30 rnd mags");

    public static readonly WeaponDef Pistol = new("Pistol", HeldWeapon.Pistol, ItemType.PistolMag, ItemType.GunPistol,
        FireInterval: 0.18f, BulletSpeed: 1300f, Damage: 12f, Spread: 0.02f, SprintSpread: 0.06f,
        MagSize: 12, ReloadTime: 1.1f, MaxRicochets: 3, Tracer: new(1.3f, 1.4f, 1.6f), MuzzleFlash: 1.6f, Automatic: false,
        Range: 470f, Description: "9mm semi. 12 rnd mags");

    public static readonly WeaponDef Smg = new("SMG", HeldWeapon.Smg, ItemType.SmgMag, ItemType.GunSmg,
        FireInterval: 0.07f, BulletSpeed: 1250f, Damage: 9f, Spread: 0.06f, SprintSpread: 0.14f,
        MagSize: 25, ReloadTime: 1.3f, MaxRicochets: 3, Tracer: new(1.5f, 1.5f, 1.0f), MuzzleFlash: 1.8f, Automatic: true,
        Range: 420f, Description: "9mm auto. 25 rnd mags, sprays");

    public static readonly WeaponDef Shotgun = new("Shotgun", HeldWeapon.Shotgun, ItemType.Shells, ItemType.GunShotgun,
        FireInterval: 0.75f, BulletSpeed: 1100f, Damage: 7f, Spread: 0.02f, SprintSpread: 0.05f,
        MagSize: 6, ReloadTime: 2.2f, MaxRicochets: 1, Tracer: new(1.6f, 1.1f, 0.6f), MuzzleFlash: 3.2f, Automatic: false,
        Pellets: 8, PelletSpread: 0.16f, Range: 260f, Description: "12ga pump. 8 pellets, 6 shells");

    /// <summary>Melee: "firing" swings; damage is applied in an arc by the owner (see Player.SwingMelee). No ammo.</summary>
    public static readonly WeaponDef Bat = new("Nail Bat", HeldWeapon.Bat, null, ItemType.MeleeBat,
        FireInterval: 0.55f, BulletSpeed: 0f, Damage: 34f, Spread: 0f, SprintSpread: 0f,
        MagSize: 0, ReloadTime: 0f, MaxRicochets: 0, Tracer: Vector3.Zero, MuzzleFlash: 0f, Automatic: false,
        Range: 62f, Description: "Silent. 34 dmg in a 100 deg arc, knocks back", IsMelee: true);

    public static readonly IReadOnlyList<WeaponDef> All = new[] { Rifle, Pistol, Smg, Shotgun, Bat };
    public static WeaponDef ForGunItem(ItemType t) => t switch
    {
        ItemType.GunRifle => Rifle, ItemType.GunPistol => Pistol, ItemType.GunSmg => Smg, ItemType.GunShotgun => Shotgun, ItemType.MeleeBat => Bat,
        _ => throw new ArgumentException($"{t} is not a gun item"),
    };

    // ---------------------------------------------------------------- enemy-grade (weaker, sloppier)
    // NOTE: Automatic = true for every AI weapon — the AI holds the trigger permanently and a semi-auto def
    //       (edge-triggered) would fire exactly once.
    public static readonly WeaponDef EnemyPistol  = Pistol  with { FireInterval = 0.6f,  Damage = 7f,  Spread = 0.10f, BulletSpeed = 1000f, MaxRicochets = 2, Tracer = new(1.6f, 0.9f, 0.6f), Automatic = true };
    public static readonly WeaponDef EnemyRifle   = Rifle   with { FireInterval = 0.18f, Damage = 7f,  Spread = 0.13f, BulletSpeed = 1300f, MaxRicochets = 2, Tracer = new(1.6f, 0.9f, 0.6f), Automatic = true, MagSize = 20 };
    public static readonly WeaponDef EnemySmg     = Smg     with { FireInterval = 0.10f, Damage = 4f,  Spread = 0.17f, BulletSpeed = 1100f, MaxRicochets = 3, Tracer = new(1.6f, 0.9f, 0.6f), Automatic = true };
    public static readonly WeaponDef EnemyShotgun = Shotgun with { FireInterval = 1.2f,  Damage = 4f,  Spread = 0.06f, BulletSpeed = 950f,  MaxRicochets = 1, Tracer = new(1.6f, 0.9f, 0.6f), Automatic = true, PelletSpread = 0.2f };
    public static readonly IReadOnlyList<WeaponDef> EnemyPool = new[] { EnemyPistol, EnemyPistol, EnemyRifle, EnemySmg, EnemyShotgun };

    /// <summary>The player-grade version of an enemy weapon (what ends up in the corpse loot).</summary>
    public WeaponDef PlayerGrade => Held switch { HeldWeapon.Rifle => Rifle, HeldWeapon.Pistol => Pistol, HeldWeapon.Smg => Smg, HeldWeapon.Shotgun => Shotgun, HeldWeapon.Bat => Bat, _ => this };
}

/// <summary>Per-instance weapon state (loaded rounds, timers, recoil, flash).</summary>
public sealed class Weapon
{
    public WeaponDef Def { get; }
    public int AmmoInMag;
    public float Cooldown, ReloadTimer, Recoil, Flash;
    public bool IsReloading => ReloadTimer > 0f;
    public bool TriggerWasDown;             // for semi-auto edge detection
    private bool _pendingReload;

    /// <summary>Fitted attachments by slot (see <see cref="AttachmentDef"/>).</summary>
    public Dictionary<AttachSlot, ItemType> Attachments { get; } = new();

    public Weapon(WeaponDef def, bool fullMag = true) { Def = def; AmmoInMag = fullMag ? def.MagSize : 0; }

    // ---- attachment effects -------------------------------------------------------------------------------
    public float SpreadMul { get { float m = 1f; foreach (var a in Attachments.Values) m *= AttachmentDef.For(a)?.SpreadMul ?? 1f; return m; } }
    public float RecoilMul { get { float m = 1f; foreach (var a in Attachments.Values) m *= AttachmentDef.For(a)?.RecoilMul ?? 1f; return m; } }
    public float FlashMul  { get { float m = 1f; foreach (var a in Attachments.Values) m *= AttachmentDef.For(a)?.FlashMul ?? 1f; return m; } }
    public float NoiseMul  { get { float m = 1f; foreach (var a in Attachments.Values) m *= AttachmentDef.For(a)?.NoiseMul ?? 1f; return m; } }
    public float RangeAdd  { get { float m = 0f; foreach (var a in Attachments.Values) m += AttachmentDef.For(a)?.RangeAdd ?? 0f; return m; } }
    public bool HasTorch   { get { foreach (var a in Attachments.Values) if (AttachmentDef.For(a)?.Torch == true) return true; return false; } }
    public bool HasLaser   { get { foreach (var a in Attachments.Values) if (AttachmentDef.For(a)?.Laser == true) return true; return false; } }
    public float EffectiveRange => Def.Range + RangeAdd;

    /// <summary>Fits an attachment item; returns the item it replaced (to go back to the bag) or null. False if the slot is not on this weapon.</summary>
    public bool TryAttach(ItemType item, out ItemType? replaced)
    {
        replaced = null;
        var def = AttachmentDef.For(item); if (def == null || !AttachPoints.Allows(Def.Held, def.Slot)) return false;
        if (Attachments.TryGetValue(def.Slot, out var old)) replaced = old;
        Attachments[def.Slot] = item; return true;
    }
    public ItemType? Detach(AttachSlot slot) { if (Attachments.TryGetValue(slot, out var t)) { Attachments.Remove(slot); return t; } return null; }

    public void Update(float dt)
    {
        Cooldown = MathF.Max(0f, Cooldown - dt);
        Recoil   = MathF.Max(0f, Recoil - dt * 9f);
        Flash    = MathF.Max(0f, Flash - dt * 14f);
        if (ReloadTimer > 0f) ReloadTimer = MathF.Max(0f, ReloadTimer - dt);
    }

    /// <summary>Starts a reload if a spare magazine exists and the weapon is not already full.</summary>
    public bool BeginReload(int spareMags)
    {
        if (IsReloading || AmmoInMag >= Def.MagSize || spareMags <= 0) return false;
        ReloadTimer = Def.ReloadTime; _pendingReload = true; return true;
    }

    /// <summary>Call every frame; when the reload timer elapses it loads a full magazine and returns 1 (mags consumed).</summary>
    public int FinishReloadIfDue(int spareMags)
    {
        if (!_pendingReload || ReloadTimer > 0f) return 0;
        _pendingReload = false;
        if (spareMags <= 0) return 0;
        AmmoInMag = Def.MagSize; return 1;
    }

    /// <summary>
    /// Attempts to fire. Returns true and the centre bullet angle if a round left the barrel; the caller spawns
    /// <see cref="WeaponDef.Pellets"/> projectiles around it (shotguns).
    /// </summary>
    public bool TryFire(bool triggerDown, float facing, bool sprinting, bool infiniteAmmo, out float bulletAngle)
    {
        bulletAngle = facing;
        bool edge = triggerDown && !TriggerWasDown; TriggerWasDown = triggerDown;
        if (!triggerDown || (!Def.Automatic && !edge)) return false;
        if (Cooldown > 0f || IsReloading) return false;
        if (!infiniteAmmo && AmmoInMag <= 0) return false;
        if (!infiniteAmmo) AmmoInMag--;
        Cooldown = Def.FireInterval; Recoil = 1f; Flash = 1f;
        bulletAngle = facing + Rng.Signed((sprinting ? Def.SprintSpread : Def.Spread) * SpreadMul);
        return true;
    }

    /// <summary>Angle for pellet i of a shot centred on 'angle' (evenly fanned with a little jitter).</summary>
    public float PelletAngle(float angle, int i)
    {
        if (Def.Pellets <= 1) return angle;
        float t = (i + 0.5f) / Def.Pellets - 0.5f;
        return angle + t * Def.PelletSpread * 2f + Rng.Signed(Def.PelletSpread * 0.25f);
    }
}
