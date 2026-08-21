using System;
using System.Collections.Generic;

namespace Game.Items;

/// <summary>
/// Fixed-slot inventory. Slots 0..HotbarSize-1 are the hotbar (keys 1..5). Adding merges into existing stacks
/// first, then fills empty slots; the leftover count is returned so callers can leave it on the ground.
/// </summary>
public sealed class Inventory
{
    public const int DefaultSlots = 15;
    public const int HotbarSize = 5;
    private readonly ItemStack[] _slots;
    public int SlotCount { get; }

    public Inventory(int slots = DefaultSlots) { SlotCount = slots; _slots = new ItemStack[slots]; }

    public event Action<ItemStack>? ItemAdded;

    public ItemStack this[int i] => _slots[i];
    public int Count => SlotCount;

    /// <summary>Copies contents from another inventory (used when moving a loadout in/out of a raid).</summary>
    public void CopyFrom(Inventory other)
    {
        Clear();
        for (int i = 0; i < other.Count && i < SlotCount; i++) _slots[i] = other[i];
    }
    public void SetSlot(int i, ItemStack s) => _slots[i] = s;
    public IEnumerable<ItemStack> NonEmpty() { foreach (var s in _slots) if (!s.IsEmpty) yield return s; }
    public bool IsEmpty { get { foreach (var s in _slots) if (!s.IsEmpty) return false; return true; } }

    /// <summary>Adds up to 'count' items; returns how many did NOT fit.</summary>
    public int Add(ItemType type, int count)
    {
        var def = ItemDef.Get(type); int added = 0;
        for (int i = 0; i < SlotCount && count > 0; i++)              // top up existing stacks
        {
            if (_slots[i].IsEmpty || _slots[i].Type != type) continue;
            int room = def.MaxStack - _slots[i].Count; int n = Math.Min(room, count);
            _slots[i].Count += n; count -= n; added += n;
        }
        for (int i = 0; i < SlotCount && count > 0; i++)              // then empty slots
        {
            if (!_slots[i].IsEmpty) continue;
            int n = Math.Min(def.MaxStack, count);
            _slots[i] = new ItemStack(type, n); count -= n; added += n;
        }
        if (added > 0) ItemAdded?.Invoke(new ItemStack(type, added));
        return count;
    }

    /// <summary>Removes up to 'count' items of a type across all stacks; returns how many were removed.</summary>
    public int Remove(ItemType type, int count)
    {
        int removed = 0;
        for (int i = SlotCount - 1; i >= 0 && count > 0; i--)
        {
            if (_slots[i].IsEmpty || _slots[i].Type != type) continue;
            int n = Math.Min(_slots[i].Count, count);
            _slots[i].Count -= n; count -= n; removed += n;
            if (_slots[i].Count <= 0) _slots[i] = default;
        }
        return removed;
    }

    public int CountOf(ItemType type)
    {
        int n = 0; foreach (var s in _slots) if (!s.IsEmpty && s.Type == type) n += s.Count; return n;
    }

    public bool Has(ItemType type, int count = 1) => CountOf(type) >= count;

    /// <summary>Consumes one item from a specific slot (hotbar use). Returns the type consumed, or null.</summary>
    public ItemType? ConsumeFromSlot(int slot)
    {
        if (slot < 0 || slot >= SlotCount || _slots[slot].IsEmpty) return null;
        var t = _slots[slot].Type;
        if (--_slots[slot].Count <= 0) _slots[slot] = default;
        return t;
    }

    public void Clear() => Array.Clear(_slots);
}
