using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Graphics;
using Game.Items;

namespace Game.Combat;

public enum AttachSlot { Optic, Muzzle, Tactical, Grip }
public enum GearSlot { Helmet, Vest }

/// <summary>A weapon attachment: which slot it fills and what it changes.</summary>
public sealed record AttachmentDef(ItemType Item, AttachSlot Slot, float SpreadMul = 1f, float RecoilMul = 1f, float FlashMul = 1f, float RangeAdd = 0f,
                                   bool Torch = false, bool Laser = false, float NoiseMul = 1f)
{
    private static readonly Dictionary<ItemType, AttachmentDef> Defs = new()
    {
        [ItemType.Optic]       = new(ItemType.Optic, AttachSlot.Optic, SpreadMul: 0.70f, RangeAdd: 80f),
        [ItemType.Suppressor]  = new(ItemType.Suppressor, AttachSlot.Muzzle, FlashMul: 0.15f, NoiseMul: 0.4f, SpreadMul: 0.95f),
        [ItemType.Compensator] = new(ItemType.Compensator, AttachSlot.Muzzle, RecoilMul: 0.65f, SpreadMul: 0.85f),
        [ItemType.Torch]       = new(ItemType.Torch, AttachSlot.Tactical, Torch: true),
        [ItemType.Laser]       = new(ItemType.Laser, AttachSlot.Tactical, Laser: true, SpreadMul: 0.75f),
        [ItemType.Grip]        = new(ItemType.Grip, AttachSlot.Grip, RecoilMul: 0.70f),
    };
    public static AttachmentDef? For(ItemType t) => Defs.TryGetValue(t, out var d) ? d : null;
    public static IEnumerable<AttachmentDef> All => Defs.Values;
}

/// <summary>Wearable armor: vests soak a fraction of damage until their durability is gone; helmets reduce all damage.</summary>
public sealed record GearDef(ItemType Item, GearSlot Slot, float MaxArmor = 0f, float Absorb = 0f, float DamageReduction = 0f, float SpeedMul = 1f, HeadGear? Head = null)
{
    private static readonly Dictionary<ItemType, GearDef> Defs = new()
    {
        [ItemType.VestLight]   = new(ItemType.VestLight, GearSlot.Vest, MaxArmor: 60f, Absorb: 0.55f, SpeedMul: 1.0f),
        [ItemType.VestHeavy]   = new(ItemType.VestHeavy, GearSlot.Vest, MaxArmor: 120f, Absorb: 0.75f, SpeedMul: 0.88f),
        [ItemType.HelmetSteel] = new(ItemType.HelmetSteel, GearSlot.Helmet, DamageReduction: 0.15f, Head: HeadGear.Helmet),
        [ItemType.HelmetTac]   = new(ItemType.HelmetTac, GearSlot.Helmet, DamageReduction: 0.25f, Head: HeadGear.Helmet),
    };
    public static GearDef? For(ItemType t) => Defs.TryGetValue(t, out var d) ? d : null;
}

/// <summary>Where each attachment sits on the arm-layer sprite (sprite-local texels, +X forward) per held weapon.</summary>
public static class AttachPoints
{
    public static Vector2? Get(HeldWeapon w, AttachSlot slot) => (w, slot) switch
    {
        (HeldWeapon.Rifle, AttachSlot.Optic)      => new Vector2(17f, -5f),
        (HeldWeapon.Rifle, AttachSlot.Muzzle)     => new Vector2(50f, 0.5f),
        (HeldWeapon.Rifle, AttachSlot.Tactical)   => new Vector2(30f, -4.5f),
        (HeldWeapon.Rifle, AttachSlot.Grip)       => new Vector2(28f, 6f),
        (HeldWeapon.Pistol, AttachSlot.Optic)     => new Vector2(22f, -1f),
        (HeldWeapon.Pistol, AttachSlot.Muzzle)    => new Vector2(34f, 3f),
        (HeldWeapon.Pistol, AttachSlot.Tactical)  => new Vector2(24f, 6.5f),
        (HeldWeapon.Smg, AttachSlot.Optic)        => new Vector2(15f, -5f),
        (HeldWeapon.Smg, AttachSlot.Muzzle)       => new Vector2(35f, 1.5f),
        (HeldWeapon.Smg, AttachSlot.Tactical)     => new Vector2(24f, -4.5f),
        (HeldWeapon.Smg, AttachSlot.Grip)         => new Vector2(22f, 6f),
        (HeldWeapon.Shotgun, AttachSlot.Optic)    => new Vector2(16f, -4.5f),
        (HeldWeapon.Shotgun, AttachSlot.Muzzle)   => new Vector2(46f, -0.5f),
        (HeldWeapon.Shotgun, AttachSlot.Tactical) => new Vector2(30f, -4.5f),
        _ => null,
    };
    public static bool Allows(HeldWeapon w, AttachSlot slot) => Get(w, slot) != null;
}
