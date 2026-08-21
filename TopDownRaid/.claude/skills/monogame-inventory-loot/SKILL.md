---
name: monogame-inventory-loot
description: Inventory and looting for a MonoGame game — ItemDef/ItemType registry with stack limits, a fixed-slot Inventory with hotbar, stack merging and leftovers, weighted LootTable rolls with a "nothing" weight, lootable/breakable crates (E to open or shoot open), enemy drops, floor Pickups that burst out, slide, sparkle, magnetise and auto-collect, ammo-as-inventory reserve, consumables (heal/armor) and procedural item icons. Use when adding items, inventories, loot drops or pickups to a MonoGame game.
---

# Inventory & loot (`Items/`)

> Update: ammo is now **magazines** (`RifleMag/PistolMag/SmgMag/Shells`, one reload each), guns are items
> (`GunRifle/GunPistol/GunSmg/GunShotgun`, equip from the bag, max 3 carried) and **bodies are containers** searched
> through the mouse-driven screen — see monogame-inventory-screen. Enemy kills no longer burst loot on the floor.

## Item registry
```csharp
public enum ItemType { RifleAmmo, PistolAmmo, Medkit, Bandage, ArmorPlate, Coin }
public sealed record ItemDef(ItemType Type, string Name, int MaxStack, bool Usable, Vector3 Tint, string Description) {
    static readonly Dictionary<ItemType, ItemDef> Defs = new() { [ItemType.Medkit] = new(ItemType.Medkit, "Medkit", 3, true, white, "Heals 60"), ... };
    public static ItemDef Get(ItemType t) => Defs[t]; public static IEnumerable<ItemDef> All => Defs.Values; }
public struct ItemStack { public ItemType Type; public int Count; public bool IsEmpty => Count <= 0; public ItemDef Def => ItemDef.Get(Type); }
```
Ammo lives in the inventory: `ReserveAmmo(weapon) = Inventory.CountOf(weapon.Def.Ammo)`; reloading removes rounds.

## Inventory (fixed slots, hotbar = first N)
```csharp
public sealed class Inventory {
  public const int SlotCount = 15, HotbarSize = 5; ItemStack[] _slots;
  public event Action<ItemStack>? ItemAdded;                       // HUD toast "+15 5.56 Ammo"
  public int Add(ItemType t, int count)   // 1) top up existing stacks of t  2) fill empty slots  → returns LEFTOVER
  public int Remove(ItemType t, int count)// from the last stack backwards; returns removed
  public int CountOf(ItemType t); public bool Has(ItemType t, int n = 1);
  public ItemType? ConsumeFromSlot(int slot); // hotbar use
}
```
Returning the leftover lets pickups stay on the floor with the remaining count when the inventory is full.

## Using items (player)
```csharp
if (input.Pressed(Keys.D1 + i)) UseSlot(i, ctx);   // 1..5
switch (stack.Type) { case Medkit: heal 60; case Bandage: heal 20; case ArmorPlate: Armor = min(Max, Armor + 50); }
// only consume when it had an effect; otherwise toast "not needed". Coins never enter slots: Collect() adds Gold.
```
Armor absorbs 60 % of incoming damage until depleted (`absorbed = min(Armor, amount*0.6)`).

## Loot tables
```csharp
public sealed class LootTable { record struct Entry(ItemType Type, float Weight, int Min, int Max); int MinRolls, MaxRolls; float NothingWeight;
  public LootTable Add(ItemType t, float w, int min, int max);            // fluent
  public List<ItemStack> Roll()   // rolls in [MinRolls, MaxRolls]; each picks by weight (NothingWeight = empty roll)
  public static readonly LootTable Crate = new LootTable { MinRolls = 2, MaxRolls = 4 }.Add(RifleAmmo, 5, 15, 30).Add(Medkit, 1.2f, 1, 1)...;
  public static readonly LootTable Brawler = ... NothingWeight = 2 ..., Gunner = ...; }
```

## Lootable crates
`Crate { Rectangle Bounds; bool Lootable, Opened; float Health = 3; LootTable? Loot; float HitFlash; }` — ~45 % of the
small crates. Two ways to open: `world.FindLootableNear(player.Position, 70)` + `E` (HUD shows "[E] OPEN CRATE" over
it), or bullets: `ProjectileSystem.HitCrate` decrements Health and raises `CrateBroken` at 0. Both call
`pickups.SpawnBurst(crate.Center, crate.Loot.Roll(), 200)` + dust/spark burst. Tint: unopened light `(255,236,170)`,
opened dark `(120,105,90)`, white flash on hit (tint applies to the albedo pass only, lighting still works).

## Pickups (`PickupManager`)
```csharp
SpawnBurst(origin, stacks, speed) → each pickup gets Velocity = randomDir * speed*(0.5..1.5)
Update: velocity *= exp(-4dt), world.ResolveCircle (never rest inside a crate);
        d < 26 px → tryCollect(stack) (returns leftover; remove if 0, else keep the rest)
        d < 90 px && Age > 0.4 → magnet: pos += toPlayer/d * (260*(1-d/90) + 60) * dt
        sparkle: every 0.5–1.3 s emit one small emissive particle in the item's tint (visible in dark corners)
Draw: batch.Draw(icons[type], pos + bob(sin(age*3)*1.5), 1f)   // in both G-buffer passes → lit like the floor
```
Icons come from `ItemArt.Create(gd, type)`: 28×28 `ShapeSprite`s (ammo box + brass rounds, medkit cross, rolled bandage,
armor plate chevron, coin) — the same `SpritePair` is used on the floor and in the HUD/inventory grid.

## HUD hooks
Hotbar slots show icon + count + key; the inventory panel (Tab) is a 5-column grid of the same slot renderer plus a
text legend of totals; toasts come from `Inventory.ItemAdded` and player actions.
