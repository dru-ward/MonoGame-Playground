using System.IO;
using Game.Combat;
using Game.Items;
using Game.Meta;
using Xunit;

namespace Game.Tests;

public class ProfileTests
{
    private static string TempSave() => Path.Combine(Path.GetTempPath(), "topdownraid_test_" + System.Guid.NewGuid().ToString("N") + ".json");

    [Fact]
    public void SaveLoad_RoundTrips_Stash_Loadout_Attachments_Gear_Stats()
    {
        Profile.SavePathOverride = TempSave();
        try
        {
            var p = new Profile(); p.GiveStarterKit();
            p.Gold = 123; p.SelectedMapId = "docks"; p.Stats.Raids = 4; p.Stats.Extracts = 3; p.Stats.Kills = 17;
            // build a loadout: rifle with optic + torch, heavy vest, tac helmet, a bag
            Assert.True(p.MoveToLoadout(0));                                         // rifle (first stash slot)
            p.Loadout.Bag.Add(ItemType.Optic, 1); p.Loadout.Bag.Add(ItemType.Torch, 1); p.Loadout.Bag.Add(ItemType.VestHeavy, 1); p.Loadout.Bag.Add(ItemType.HelmetTac, 1);
            for (int i = 0; i < p.Loadout.Bag.Count; i++) if (!p.Loadout.Bag[i].IsEmpty && p.Loadout.Bag[i].Def.IsAttachment) p.Loadout.AttachFromBag(i, 0);
            for (int i = 0; i < p.Loadout.Bag.Count; i++) if (!p.Loadout.Bag[i].IsEmpty && p.Loadout.Bag[i].Def.IsGear) p.Loadout.WearFromBag(i);
            Assert.Equal(2, p.Loadout.Weapons[0].Attachments.Count);
            Assert.Equal(ItemType.VestHeavy, p.Loadout.Vest); Assert.Equal(ItemType.HelmetTac, p.Loadout.Helmet);
            p.Loadout.Bag.Add(ItemType.RifleMag, 3);
            p.Save();

            var q = Profile.Load();
            Assert.Equal(123, q.Gold); Assert.Equal("docks", q.SelectedMapId);
            Assert.Equal(4, q.Stats.Raids); Assert.Equal(3, q.Stats.Extracts); Assert.Equal(17, q.Stats.Kills);
            Assert.Single(q.Loadout.Weapons);
            Assert.Equal(ItemType.GunRifle, q.Loadout.Weapons[0].Gun);
            Assert.Equal(ItemType.Optic, q.Loadout.Weapons[0].Attachments[AttachSlot.Optic]);
            Assert.Equal(ItemType.Torch, q.Loadout.Weapons[0].Attachments[AttachSlot.Tactical]);
            Assert.Equal(ItemType.VestHeavy, q.Loadout.Vest); Assert.Equal(ItemType.HelmetTac, q.Loadout.Helmet);
            Assert.Equal(3, q.Loadout.Bag.CountOf(ItemType.RifleMag));
            for (int i = 0; i < p.Stash.Count; i++) { Assert.Equal(p.Stash[i].Type, q.Stash[i].Type); Assert.Equal(p.Stash[i].Count, q.Stash[i].Count); }
        }
        finally { File.Delete(Profile.SavePathOverride!); Profile.SavePathOverride = null; }
    }

    [Fact]
    public void Load_WithoutFile_GivesStarterKit()
    {
        Profile.SavePathOverride = TempSave();
        try
        {
            var p = Profile.Load();
            Assert.True(p.Stash.CountOf(ItemType.GunRifle) >= 1);
            Assert.True(p.Stash.CountOf(ItemType.RifleMag) >= 1);
            Assert.Equal(0, p.Stats.Raids);
        }
        finally { Profile.SavePathOverride = null; }
    }

    [Fact]
    public void Load_WithCorruptFile_FallsBackToStarterKit()
    {
        Profile.SavePathOverride = TempSave();
        try
        {
            File.WriteAllText(Profile.SavePathOverride!, "{ this is not json");
            var p = Profile.Load();
            Assert.True(p.Stash.CountOf(ItemType.GunPistol) >= 1);
        }
        finally { File.Delete(Profile.SavePathOverride!); Profile.SavePathOverride = null; }
    }

    [Fact]
    public void StashAndLoadout_Moves_AreSymmetric()
    {
        var p = new Profile(); p.GiveStarterKit();
        int stashBefore = Count(p.Stash);
        p.MoveToLoadout(0); p.MoveToLoadout(0); p.MoveToLoadout(0);          // gun, gun, mags (slot 0 refills as stacks shift? no: slots keep positions)
        Assert.True(p.Loadout.HasWeapon);
        p.StashAll();
        Assert.False(p.Loadout.HasWeapon); Assert.True(p.Loadout.Bag.IsEmpty);
        Assert.Null(p.Loadout.Helmet); Assert.Null(p.Loadout.Vest);
        Assert.Equal(stashBefore, Count(p.Stash));
    }

    [Fact]
    public void MoveWeaponToStash_StripsAttachmentsIntoStash()
    {
        var p = new Profile();
        p.Stash.Add(ItemType.GunSmg, 1);
        p.MoveToLoadout(0);
        p.Loadout.Bag.Add(ItemType.Suppressor, 1);
        Assert.True(p.Loadout.AttachFromBag(0, 0));
        Assert.True(p.MoveWeaponToStash(0));
        Assert.Equal(1, p.Stash.CountOf(ItemType.GunSmg));
        Assert.Equal(1, p.Stash.CountOf(ItemType.Suppressor));
        Assert.Empty(p.Loadout.Weapons);
    }

    [Fact]
    public void EnsureMinimumLoadout_IssuesAPistol()
    {
        var p = new Profile();
        p.EnsureMinimumLoadout();
        Assert.True(p.Loadout.HasWeapon);
        Assert.Equal(ItemType.GunPistol, p.Loadout.Weapons[0].Gun);
        Assert.True(p.Loadout.Bag.CountOf(ItemType.PistolMag) >= 1);
    }

    [Fact]
    public void RaidOutcomes_UpdateStatsAndLoadout()
    {
        var p = new Profile();
        var bag = new Inventory(); bag.Add(ItemType.Coin, 0); bag.Add(ItemType.Medkit, 2);
        p.ReturnFromRaid(new[] { new WeaponLoadout(ItemType.GunShotgun) }, ItemType.HelmetSteel, null, bag, goldFound: 40, kills: 3);
        Assert.Equal(40, p.Gold); Assert.Equal(1, p.Stats.Extracts); Assert.Equal(1, p.Stats.Raids);
        Assert.Equal(ItemType.GunShotgun, p.Loadout.Weapons[0].Gun); Assert.Equal(ItemType.HelmetSteel, p.Loadout.Helmet);
        Assert.Equal(2, p.Loadout.Bag.CountOf(ItemType.Medkit));
        p.LoseRaid(kills: 1);
        Assert.Equal(2, p.Stats.Raids); Assert.Equal(1, p.Stats.Deaths); Assert.Equal(4, p.Stats.Kills);
        Assert.False(p.Loadout.HasWeapon); Assert.Null(p.Loadout.Helmet);
        Assert.Equal(40, p.Gold);                                                 // gold is never lost
    }

    private static int Count(Inventory inv) { int n = 0; for (int i = 0; i < inv.Count; i++) n += inv[i].Count; return n; }
}
