---
name: monogame-inventory-loot
description: Inventory and loot systems for a MonoGame game — an ItemDef/ItemType registry record with stack limits and usability flags, a fixed-slot Inventory with a hotbar, stack merging that returns leftovers, weighted LootTable rolls with a "nothing" weight and a roll-count range, lootable/breakable containers opened by an interaction key or by damage, floor Pickups that burst out, decelerate, sparkle and are taken deliberately (nearest-interactable priority, no auto-collect), ammo stored as inventory items, consumables that only consume when they have an effect, and procedural item icons shared by world and HUD. Use when adding items, inventories, loot drops or pickups to a MonoGame game.
---

# Inventory & loot

## Item registry
```csharp
public enum ItemType { AmmoA, AmmoB, HealLarge, HealSmall, ArmorPlate, Currency }
public sealed record ItemDef(ItemType Type, string Name, int MaxStack, bool Usable, Vector3 Tint, string Description) {
    static readonly Dictionary<ItemType, ItemDef> Defs = new() { [ItemType.HealLarge] = new(ItemType.HealLarge, "Medkit", 3, true, white, "Heals N"), ... };
    public static ItemDef Get(ItemType t) => Defs[t]; public static IEnumerable<ItemDef> All => Defs.Values; }
public struct ItemStack { public ItemType Type; public int Count; public bool IsEmpty => Count <= 0; public ItemDef Def => ItemDef.Get(Type); }
```
Ammo can live in the inventory: `ReserveAmmo(weapon) = Inventory.CountOf(weapon.Def.Ammo)`; reloading removes rounds
(or one magazine item per reload if ammo is modelled as magazines).

## Inventory (fixed slots, hotbar = first N)
```csharp
public sealed class Inventory {
  public const int SlotCount = 15, HotbarSize = 5; ItemStack[] _slots;      // starting sizes
  public event Action<ItemStack>? ItemAdded;                       // HUD toast "+N <name>"
  public int Add(ItemType t, int count)   // 1) top up existing stacks of t  2) fill empty slots  → returns LEFTOVER
  public int Remove(ItemType t, int count)// from the last stack backwards; returns removed
  public int CountOf(ItemType t); public bool Has(ItemType t, int n = 1);
  public ItemType? ConsumeFromSlot(int slot); // hotbar use
}
```
Returning the leftover lets pickups stay on the floor with the remaining count when the inventory is full.

## Using items (player)
```csharp
if (input.Pressed(Keys.D1 + i)) UseSlot(i, ctx);   // 1..HotbarSize
switch (stack.Type) { case HealLarge: heal; case HealSmall: heal less; case ArmorPlate: Armor = min(Max, Armor + plateValue); }
// only consume when it had an effect; otherwise toast "not needed". Currency never enters slots: Collect() adds to a counter.
```
Armor as a damage absorber: `absorbed = min(Armor, amount * absorbFraction)` until depleted (0.6 is a starting value).

## Loot tables
```csharp
public sealed class LootTable { record struct Entry(ItemType Type, float Weight, int Min, int Max); int MinRolls, MaxRolls; float NothingWeight;
  public LootTable Add(ItemType t, float w, int min, int max);            // fluent
  public List<ItemStack> Roll()   // rolls in [MinRolls, MaxRolls]; each picks by weight (NothingWeight = empty roll)
  // example: new LootTable { MinRolls = 2, MaxRolls = 4 }.Add(AmmoA, 5, 15, 30).Add(HealLarge, 1.2f, 1, 1) ...
  // per-enemy-class tables with NothingWeight > 0 give "sometimes drops nothing"
}
```

## Lootable containers
`Crate { Rectangle Bounds; bool Lootable, Opened; float Health = 3; LootTable? Loot; float HitFlash; }` — make only a
fraction of containers lootable. Two ways to open: `world.FindLootableNear(player.Position, range)` + interaction key
(HUD prompt over it), or damage: the projectile system decrements `Health` and raises a `CrateBroken` event at 0. Both
call `pickups.SpawnBurst(crate.Center, crate.Loot.Roll(), speed)` plus a dust/spark burst. Tint unopened vs opened
differently and flash white on hit; apply the tint in the albedo pass only so lighting still works.

## Pickups (`PickupManager`) — deliberate take, no auto-collect
```csharp
SpawnBurst(origin, stacks, speed) → each pickup gets Velocity = randomDir * speed*(0.5..1.5)     // speed ~200 px/s
Update(dt, world): velocity *= exp(-4dt), world.ResolveCircle (never rest inside a solid);
        sparkle: every 0.5–1.3 s emit one small emissive particle in the item's tint (visible in dark corners)
FindNearest(pos, range) → Pickup?          // for the "[E] TAKE <name> xN" prompt over the item
TryCollect(pickup, tryCollect)             // leftover >= count → "nothing fit"; 0 → remove; else shrink the stack
Draw: batch.Draw(icons[type], pos + bob(sin(age*3)*1.5), 1f)   // in every G-buffer pass → lit like the floor
```
Nothing magnetises or auto-collects: the player presses the interaction key. Interaction priority each frame: a
searchable body/container under the cursor first, then the NEARER of floor pickup vs lootable crate (distance-squared
compare — otherwise an opened crate blocks items dropped beside it). A bot/headless mode can still auto-collect the
nearby pickup so automated runs keep gathering.
Icons come from a procedural `ItemArt.Create(gd, type)`: ~28×28 shape sprites (box + rounds, cross, roll, chevron,
disc); the same albedo/normal pair is used on the floor and in the HUD/inventory grid.

## HUD hooks
Hotbar slots show icon + count + key; an inventory panel is a grid of the same slot renderer plus a text legend of
totals; toasts come from `Inventory.ItemAdded` and player actions (see monogame-hud-pixel-font).
