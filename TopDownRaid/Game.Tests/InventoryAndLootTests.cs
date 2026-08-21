using Game.Core;
using Game.Items;
using Xunit;

namespace Game.Tests;

public class InventoryTests
{
    [Fact]
    public void Add_MergesIntoExistingStacks_ThenFillsEmptySlots()
    {
        var inv = new Inventory(3);
        Assert.Equal(0, inv.Add(ItemType.RifleMag, 4));          // RifleMag stacks to 6
        Assert.Equal(0, inv.Add(ItemType.RifleMag, 4));          // 6 in slot 0, 2 in slot 1
        Assert.Equal(6, inv[0].Count);
        Assert.Equal(2, inv[1].Count);
        Assert.Equal(8, inv.CountOf(ItemType.RifleMag));
    }

    [Fact]
    public void Add_ReturnsLeftover_WhenFull()
    {
        var inv = new Inventory(1);
        Assert.Equal(0, inv.Add(ItemType.Medkit, 3));             // max stack 3
        Assert.Equal(2, inv.Add(ItemType.Medkit, 2));             // nothing fits
        Assert.Equal(1, inv.Add(ItemType.Bandage, 1));            // different type, no slot
    }

    [Fact]
    public void Remove_TakesFromLastStackFirst_AndClearsEmpty()
    {
        var inv = new Inventory(3);
        inv.Add(ItemType.PistolMag, 10);                          // 8 + 2
        Assert.Equal(3, inv.Remove(ItemType.PistolMag, 3));       // takes 2 from slot 1 then 1 from slot 0
        Assert.True(inv[1].IsEmpty);
        Assert.Equal(7, inv[0].Count);
        Assert.Equal(7, inv.Remove(ItemType.PistolMag, 99));
        Assert.True(inv.IsEmpty);
    }

    [Fact]
    public void ConsumeFromSlot_DecrementsAndClears()
    {
        var inv = new Inventory(2);
        inv.Add(ItemType.Bandage, 2);
        Assert.Equal(ItemType.Bandage, inv.ConsumeFromSlot(0));
        Assert.Equal(1, inv[0].Count);
        Assert.Equal(ItemType.Bandage, inv.ConsumeFromSlot(0));
        Assert.True(inv[0].IsEmpty);
        Assert.Null(inv.ConsumeFromSlot(0));
        Assert.Null(inv.ConsumeFromSlot(99));
    }

    [Fact]
    public void CopyFrom_ReplacesContents_AndTruncatesToCapacity()
    {
        var src = new Inventory(4); src.Add(ItemType.Coin, 5); src.Add(ItemType.Medkit, 1); src.Add(ItemType.Torch, 1); src.Add(ItemType.Grenade, 2);
        var dst = new Inventory(2); dst.Add(ItemType.RifleMag, 1);
        dst.CopyFrom(src);
        Assert.Equal(ItemType.Coin, dst[0].Type);
        Assert.Equal(ItemType.Medkit, dst[1].Type);
        Assert.Equal(0, dst.CountOf(ItemType.RifleMag));
    }

    [Fact]
    public void ItemAdded_FiresWithAddedCountOnly()
    {
        var inv = new Inventory(1); int reported = -1;
        inv.ItemAdded += s => reported = s.Count;
        inv.Add(ItemType.Bandage, 20);                            // max 8 fit
        Assert.Equal(8, reported);
    }

    [Fact]
    public void EveryItemType_HasADefinition()
    {
        foreach (ItemType t in System.Enum.GetValues<ItemType>())
        {
            var def = ItemDef.Get(t);
            Assert.Equal(t, def.Type);
            Assert.True(def.MaxStack >= 1);
            Assert.False(string.IsNullOrWhiteSpace(def.Name));
        }
    }
}

public class LootTableTests
{
    [Fact]
    public void Roll_RespectsRollBounds_AndCounts()
    {
        Rng.Seed(7);
        var table = new LootTable { MinRolls = 2, MaxRolls = 3 }.Add(ItemType.RifleMag, 1f, 1, 2).Add(ItemType.Coin, 1f, 3, 5);
        for (int i = 0; i < 200; i++)
        {
            var r = table.Roll();
            Assert.InRange(r.Count, 2, 3);
            foreach (var s in r)
            {
                if (s.Type == ItemType.RifleMag) Assert.InRange(s.Count, 1, 2);
                else if (s.Type == ItemType.Coin) Assert.InRange(s.Count, 3, 5);
                else Assert.Fail($"unexpected {s.Type}");
            }
        }
    }

    [Fact]
    public void NothingWeight_ProducesEmptyRolls()
    {
        Rng.Seed(3);
        var table = new LootTable { MinRolls = 1, MaxRolls = 1, NothingWeight = 100f }.Add(ItemType.Coin, 1f, 1, 1);
        int empty = 0;
        for (int i = 0; i < 200; i++) if (table.Roll().Count == 0) empty++;
        Assert.True(empty > 150, $"expected mostly empty rolls, got {empty}/200");
    }

    [Fact]
    public void SharedTables_OnlyYieldKnownItems()
    {
        Rng.Seed(11);
        foreach (var t in new[] { LootTable.Crate, LootTable.Cache, LootTable.BrawlerBody, LootTable.GunnerBody })
            for (int i = 0; i < 50; i++)
                foreach (var s in t.Roll()) { Assert.True(s.Count > 0); _ = ItemDef.Get(s.Type); }
    }
}
