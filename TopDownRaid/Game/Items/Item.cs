using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game.Items;

public enum ItemType
{
    RifleMag, PistolMag, SmgMag, Shells,          // magazines (one reload each)
    Medkit, Bandage, ArmorPlate, Coin,            // consumables / valuables
    GunRifle, GunPistol, GunSmg, GunShotgun,      // weapons (equip from the inventory)
    VestLight, VestHeavy, HelmetSteel, HelmetTac, // wearable gear
    Optic, Suppressor, Compensator, Torch, Laser, Grip,   // weapon attachments
    Grenade,                                      // throwable
    MeleeBat,                                     // melee weapon (equips like a gun)
}

public enum ItemCategory { Magazine, Consumable, Valuable, Weapon, Gear, Attachment, Throwable }

/// <summary>Static definition of an item type (looked up through <see cref="ItemDef.Get"/>).</summary>
public sealed record ItemDef(ItemType Type, string Name, ItemCategory Category, int MaxStack, bool Usable, Vector3 Tint, string Description)
{
    private static readonly Dictionary<ItemType, ItemDef> Defs = new()
    {
        [ItemType.RifleMag]   = new(ItemType.RifleMag,   "5.56 Mag",     ItemCategory.Magazine,   6, false, new(0.95f, 0.78f, 0.30f), "30 rnd rifle magazine"),
        [ItemType.PistolMag]  = new(ItemType.PistolMag,  "9mm Mag",      ItemCategory.Magazine,   8, false, new(0.80f, 0.80f, 0.85f), "12 rnd pistol magazine"),
        [ItemType.SmgMag]     = new(ItemType.SmgMag,     "SMG Mag",      ItemCategory.Magazine,   6, false, new(0.85f, 0.85f, 0.70f), "25 rnd SMG magazine"),
        [ItemType.Shells]     = new(ItemType.Shells,     "12ga Shells",  ItemCategory.Magazine,   8, false, new(0.85f, 0.35f, 0.25f), "6 shells (one reload)"),
        [ItemType.Medkit]     = new(ItemType.Medkit,     "Medkit",       ItemCategory.Consumable, 3, true,  new(0.95f, 0.95f, 0.95f), "Heals 60"),
        [ItemType.Bandage]    = new(ItemType.Bandage,    "Bandage",      ItemCategory.Consumable, 8, true,  new(0.90f, 0.82f, 0.70f), "Heals 20"),
        [ItemType.ArmorPlate] = new(ItemType.ArmorPlate, "Armor Plate",  ItemCategory.Consumable, 3, true,  new(0.35f, 0.55f, 0.90f), "+50 armor"),
        [ItemType.Coin]       = new(ItemType.Coin,       "Gold Coin",    ItemCategory.Valuable,  99, false, new(1.00f, 0.85f, 0.25f), "Score"),
        [ItemType.GunRifle]   = new(ItemType.GunRifle,   "Assault Rifle",ItemCategory.Weapon,     1, true,  new(0.55f, 0.55f, 0.50f), "Equip: 5.56 auto rifle"),
        [ItemType.GunPistol]  = new(ItemType.GunPistol,  "Pistol",       ItemCategory.Weapon,     1, true,  new(0.55f, 0.55f, 0.60f), "Equip: 9mm sidearm"),
        [ItemType.GunSmg]     = new(ItemType.GunSmg,     "SMG",          ItemCategory.Weapon,     1, true,  new(0.60f, 0.58f, 0.50f), "Equip: 9mm SMG"),
        [ItemType.GunShotgun] = new(ItemType.GunShotgun, "Shotgun",      ItemCategory.Weapon,     1, true,  new(0.50f, 0.40f, 0.30f), "Equip: 12ga pump"),
        [ItemType.VestLight]  = new(ItemType.VestLight,  "Light Vest",   ItemCategory.Gear,       1, true,  new(0.35f, 0.40f, 0.32f), "Wear: 60 armor, absorbs 55%"),
        [ItemType.VestHeavy]  = new(ItemType.VestHeavy,  "Heavy Vest",   ItemCategory.Gear,       1, true,  new(0.18f, 0.18f, 0.20f), "Wear: 120 armor, absorbs 75%, slower"),
        [ItemType.HelmetSteel]= new(ItemType.HelmetSteel,"Steel Helmet", ItemCategory.Gear,       1, true,  new(0.40f, 0.42f, 0.38f), "Wear: -15% damage taken"),
        [ItemType.HelmetTac]  = new(ItemType.HelmetTac,  "Tac Helmet",   ItemCategory.Gear,       1, true,  new(0.28f, 0.30f, 0.24f), "Wear: -25% damage taken"),
        [ItemType.Optic]      = new(ItemType.Optic,      "Red Dot",      ItemCategory.Attachment, 1, true,  new(0.30f, 0.30f, 0.34f), "Optic: -30% spread, +80 range"),
        [ItemType.Suppressor] = new(ItemType.Suppressor, "Suppressor",   ItemCategory.Attachment, 1, true,  new(0.20f, 0.20f, 0.22f), "Muzzle: tiny flash, enemies hear less"),
        [ItemType.Compensator]= new(ItemType.Compensator,"Compensator",  ItemCategory.Attachment, 1, true,  new(0.35f, 0.35f, 0.38f), "Muzzle: -35% recoil, -15% spread"),
        [ItemType.Torch]      = new(ItemType.Torch,      "Torch",        ItemCategory.Attachment, 1, true,  new(0.85f, 0.85f, 0.70f), "Tactical: weapon light (cone)"),
        [ItemType.Laser]      = new(ItemType.Laser,      "Laser",        ItemCategory.Attachment, 1, true,  new(0.90f, 0.25f, 0.25f), "Tactical: aiming line, -25% spread"),
        [ItemType.Grip]       = new(ItemType.Grip,       "Fore Grip",    ItemCategory.Attachment, 1, true,  new(0.22f, 0.20f, 0.18f), "Grip: -30% recoil"),
        [ItemType.MeleeBat]   = new(ItemType.MeleeBat,   "Nail Bat",     ItemCategory.Weapon,     1, true,  new(0.55f, 0.38f, 0.20f), "Equip: melee swing, silent"),
        [ItemType.Grenade]    = new(ItemType.Grenade,    "Frag Grenade", ItemCategory.Throwable,  4, true,  new(0.35f, 0.45f, 0.30f), "Throw [G] or hotbar: 2.5 s fuse, 150 px blast"),
    };
    public static ItemDef Get(ItemType t) => Defs[t];
    public static IEnumerable<ItemDef> All => Defs.Values;
    public bool IsWeapon => Category == ItemCategory.Weapon;
    public bool IsGear => Category == ItemCategory.Gear;
    public bool IsAttachment => Category == ItemCategory.Attachment;
}

/// <summary>A quantity of one item type. Empty slots are represented by Count == 0.</summary>
public struct ItemStack
{
    public ItemType Type;
    public int Count;
    public bool IsEmpty => Count <= 0;
    public ItemDef Def => ItemDef.Get(Type);
    public ItemStack(ItemType type, int count) { Type = type; Count = count; }
    public override string ToString() => IsEmpty ? "-" : $"{Def.Name} x{Count}";
}
