using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;
using Game.Combat;
using Game.Items;

namespace Game.Meta;

/// <summary>A gun plus whatever is bolted onto it.</summary>
public sealed class WeaponLoadout
{
    public ItemType Gun;
    public Dictionary<AttachSlot, ItemType> Attachments { get; } = new();
    public WeaponLoadout(ItemType gun) { Gun = gun; }
    public WeaponDef Def => WeaponDef.ForGunItem(Gun);
}

/// <summary>What the player takes into a raid: up to 3 guns (with attachments), helmet, vest and a 15-slot bag.</summary>
public sealed class Loadout
{
    public List<WeaponLoadout> Weapons { get; } = new();     // index 0 is drawn first
    public Inventory Bag { get; } = new(Inventory.DefaultSlots);
    public ItemType? Helmet, Vest;
    public bool HasWeapon => Weapons.Count > 0;

    public bool AddWeapon(ItemType gun) { if (Weapons.Count >= 3 || !ItemDef.Get(gun).IsWeapon) return false; Weapons.Add(new WeaponLoadout(gun)); return true; }
    public void Clear() { Weapons.Clear(); Bag.Clear(); Helmet = null; Vest = null; }

    /// <summary>Fits an attachment from the bag onto weapon[index]; a displaced attachment goes back to the bag.</summary>
    public bool AttachFromBag(int bagSlot, int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= Weapons.Count) return false;
        var s = Bag[bagSlot]; var def = s.IsEmpty ? null : AttachmentDef.For(s.Type);
        if (def == null) return false;
        var w = Weapons[weaponIndex];
        if (!AttachPoints.Allows(w.Def.Held, def.Slot)) return false;
        if (w.Attachments.TryGetValue(def.Slot, out var old)) Bag.Add(old, 1);
        w.Attachments[def.Slot] = s.Type; Bag.Remove(s.Type, 1); return true;
    }
    public bool DetachToBag(int weaponIndex, AttachSlot slot)
    {
        if (weaponIndex < 0 || weaponIndex >= Weapons.Count) return false;
        var w = Weapons[weaponIndex];
        if (!w.Attachments.TryGetValue(slot, out var t)) return false;
        if (Bag.Add(t, 1) > 0) return false;
        w.Attachments.Remove(slot); return true;
    }
    /// <summary>Wears gear from the bag (swapping with what is worn).</summary>
    public bool WearFromBag(int bagSlot)
    {
        var s = Bag[bagSlot]; var def = s.IsEmpty ? null : GearDef.For(s.Type);
        if (def == null) return false;
        ItemType? old = def.Slot == GearSlot.Helmet ? Helmet : Vest;
        if (def.Slot == GearSlot.Helmet) Helmet = s.Type; else Vest = s.Type;
        Bag.Remove(s.Type, 1);
        if (old is { } o) Bag.Add(o, 1);
        return true;
    }
    public bool UnwearToBag(GearSlot gs)
    {
        ItemType? t = gs == GearSlot.Helmet ? Helmet : Vest;
        if (t is not { } it) return false;
        if (Bag.Add(it, 1) > 0) return false;
        if (gs == GearSlot.Helmet) Helmet = null; else Vest = null;
        return true;
    }
}

/// <summary>Lifetime statistics shown in the stash and on the summary screen.</summary>
public sealed class Stats
{
    public int Raids, Extracts, Deaths, Kills, GoldEarned;
    public float SurvivalRate => Raids == 0 ? 0f : Extracts / (float)Raids;
}

/// <summary>
/// The persistent meta-game state: the stash (big inventory), gold, the current loadout, the selected map and stats.
/// Saved as JSON in the user's app-data folder after every raid and on quit; a fresh profile gets a starter kit.
/// </summary>
public sealed class Profile
{
    public const int StashSlots = 48;                       // 8 x 6
    public Inventory Stash { get; } = new(StashSlots);
    public Loadout Loadout { get; } = new();
    public Stats Stats { get; } = new();
    public int Gold;
    public string SelectedMapId = "scrapyard";

    /// <summary>Tests point this at a temp file; null = the real app-data location.</summary>
    public static string? SavePathOverride;
    public static string SavePath => SavePathOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "TopDownRaid", "profile.json");

    /// <summary>Starter kit: enough to survive a first raid.</summary>
    public void GiveStarterKit()
    {
        Stash.Add(ItemType.GunRifle, 1); Stash.Add(ItemType.GunPistol, 1);
        Stash.Add(ItemType.RifleMag, 4); Stash.Add(ItemType.PistolMag, 4);
        Stash.Add(ItemType.Bandage, 4); Stash.Add(ItemType.Medkit, 1); Stash.Add(ItemType.ArmorPlate, 1);
        Stash.Add(ItemType.VestLight, 1); Stash.Add(ItemType.Torch, 1); Stash.Add(ItemType.Grenade, 2);
        Gold = 0;
    }

    /// <summary>If the loadout has no gun at deploy time, hand out a free scav pistol + a mag so a raid is always possible.</summary>
    public void EnsureMinimumLoadout()
    {
        if (!Loadout.HasWeapon) { Loadout.AddWeapon(ItemType.GunPistol); Loadout.Bag.Add(ItemType.PistolMag, 1); }
    }

    // ------------------------------------------------------------------------------------------------ stash <-> loadout
    /// <summary>Moves one stash slot into the loadout (guns into a weapon slot when there is room, else the bag). Returns false if nothing moved.</summary>
    public bool MoveToLoadout(int stashSlot)
    {
        var s = Stash[stashSlot]; if (s.IsEmpty) return false;
        if (s.Def.IsWeapon && Loadout.AddWeapon(s.Type)) { Stash.Remove(s.Type, 1); return true; }
        int leftover = Loadout.Bag.Add(s.Type, s.Count); int moved = s.Count - leftover;
        if (moved > 0) Stash.Remove(s.Type, moved);
        return moved > 0;
    }

    public bool MoveBagToStash(int bagSlot)
    {
        var s = Loadout.Bag[bagSlot]; if (s.IsEmpty) return false;
        int leftover = Stash.Add(s.Type, s.Count); int moved = s.Count - leftover;
        if (moved > 0) Loadout.Bag.Remove(s.Type, moved);
        return moved > 0;
    }

    /// <summary>Weapon (with its attachments, which are stripped into the stash too) back to the stash.</summary>
    public bool MoveWeaponToStash(int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= Loadout.Weapons.Count) return false;
        var w = Loadout.Weapons[weaponIndex];
        if (Stash.Add(w.Gun, 1) > 0) return false;
        foreach (var a in w.Attachments.Values) if (Stash.Add(a, 1) > 0) Loadout.Bag.Add(a, 1);
        Loadout.Weapons.RemoveAt(weaponIndex); return true;
    }
    public bool MoveGearToStash(GearSlot gs)
    {
        ItemType? t = gs == GearSlot.Helmet ? Loadout.Helmet : Loadout.Vest;
        if (t is not { } it) return false;
        if (Stash.Add(it, 1) > 0) return false;
        if (gs == GearSlot.Helmet) Loadout.Helmet = null; else Loadout.Vest = null;
        return true;
    }

    /// <summary>Successful extraction: whatever the player carried goes back into the loadout (and can be stashed).</summary>
    public void ReturnFromRaid(IEnumerable<WeaponLoadout> weapons, ItemType? helmet, ItemType? vest, Inventory bag, int goldFound, int kills)
    {
        Loadout.Clear();
        foreach (var w in weapons) Loadout.Weapons.Add(w);
        Loadout.Helmet = helmet; Loadout.Vest = vest;
        Loadout.Bag.CopyFrom(bag);
        Gold += goldFound; Stats.GoldEarned += goldFound; Stats.Kills += kills; Stats.Raids++; Stats.Extracts++;
    }

    /// <summary>Death / MIA: everything carried is lost.</summary>
    public void LoseRaid(int kills)
    {
        Loadout.Clear();
        Stats.Kills += kills; Stats.Raids++; Stats.Deaths++;
    }

    /// <summary>Moves everything from the loadout into the stash (what does not fit stays in the loadout).</summary>
    public void StashAll()
    {
        for (int i = Loadout.Weapons.Count - 1; i >= 0; i--) MoveWeaponToStash(i);
        MoveGearToStash(GearSlot.Helmet); MoveGearToStash(GearSlot.Vest);
        for (int i = 0; i < Loadout.Bag.Count; i++) MoveBagToStash(i);
    }

    // ------------------------------------------------------------------------------------------------ persistence
    private sealed class StackDto { public string Type { get; set; } = ""; public int Count { get; set; } }
    private sealed class WeaponDto { public string Gun { get; set; } = ""; public Dictionary<string, string> Attachments { get; set; } = new(); }
    private sealed class Dto
    {
        public int Gold { get; set; }
        public string SelectedMapId { get; set; } = "scrapyard";
        public List<StackDto> Stash { get; set; } = new();
        public List<WeaponDto> LoadoutWeapons { get; set; } = new();
        public string? Helmet { get; set; } public string? Vest { get; set; }
        public List<StackDto> LoadoutBag { get; set; } = new();
        public int Raids { get; set; } public int Extracts { get; set; } public int Deaths { get; set; } public int Kills { get; set; } public int GoldEarned { get; set; }
    }

    public void Save()
    {
        try
        {
            var dto = new Dto { Gold = Gold, SelectedMapId = SelectedMapId, Raids = Stats.Raids, Extracts = Stats.Extracts, Deaths = Stats.Deaths, Kills = Stats.Kills, GoldEarned = Stats.GoldEarned };
            for (int i = 0; i < Stash.Count; i++) dto.Stash.Add(new StackDto { Type = Stash[i].IsEmpty ? "" : Stash[i].Type.ToString(), Count = Stash[i].Count });
            foreach (var w in Loadout.Weapons)
            {
                var wd = new WeaponDto { Gun = w.Gun.ToString() };
                foreach (var kv in w.Attachments) wd.Attachments[kv.Key.ToString()] = kv.Value.ToString();
                dto.LoadoutWeapons.Add(wd);
            }
            dto.Helmet = Loadout.Helmet?.ToString(); dto.Vest = Loadout.Vest?.ToString();
            for (int i = 0; i < Loadout.Bag.Count; i++) dto.LoadoutBag.Add(new StackDto { Type = Loadout.Bag[i].IsEmpty ? "" : Loadout.Bag[i].Type.ToString(), Count = Loadout.Bag[i].Count });
            Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
            File.WriteAllText(SavePath, JsonSerializer.Serialize(dto, new JsonSerializerOptions { WriteIndented = true }));
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Profile] save failed: {ex.Message}"); }
    }

    public static Profile Load()
    {
        var p = new Profile();
        try
        {
            if (File.Exists(SavePath))
            {
                var dto = JsonSerializer.Deserialize<Dto>(File.ReadAllText(SavePath));
                if (dto != null)
                {
                    p.Gold = dto.Gold; p.SelectedMapId = dto.SelectedMapId;
                    p.Stats.Raids = dto.Raids; p.Stats.Extracts = dto.Extracts; p.Stats.Deaths = dto.Deaths; p.Stats.Kills = dto.Kills; p.Stats.GoldEarned = dto.GoldEarned;
                    for (int i = 0; i < dto.Stash.Count && i < p.Stash.Count; i++)
                        if (Enum.TryParse<ItemType>(dto.Stash[i].Type, out var t) && dto.Stash[i].Count > 0) p.Stash.SetSlot(i, new ItemStack(t, dto.Stash[i].Count));
                    foreach (var w in dto.LoadoutWeapons)
                    {
                        if (!Enum.TryParse<ItemType>(w.Gun, out var t) || !p.Loadout.AddWeapon(t)) continue;
                        var wl = p.Loadout.Weapons[p.Loadout.Weapons.Count - 1];
                        foreach (var kv in w.Attachments) if (Enum.TryParse<AttachSlot>(kv.Key, out var sl) && Enum.TryParse<ItemType>(kv.Value, out var it)) wl.Attachments[sl] = it;
                    }
                    if (Enum.TryParse<ItemType>(dto.Helmet ?? "", out var hm)) p.Loadout.Helmet = hm;
                    if (Enum.TryParse<ItemType>(dto.Vest ?? "", out var vs)) p.Loadout.Vest = vs;
                    for (int i = 0; i < dto.LoadoutBag.Count && i < p.Loadout.Bag.Count; i++)
                        if (Enum.TryParse<ItemType>(dto.LoadoutBag[i].Type, out var t) && dto.LoadoutBag[i].Count > 0) p.Loadout.Bag.SetSlot(i, new ItemStack(t, dto.LoadoutBag[i].Count));
                    return p;
                }
            }
        }
        catch (Exception ex) { Console.Error.WriteLine($"[Profile] load failed: {ex.Message}"); }
        p.GiveStarterKit();
        return p;
    }
}
