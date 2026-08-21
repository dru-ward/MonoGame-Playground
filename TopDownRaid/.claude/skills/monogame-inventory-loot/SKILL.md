---
name: monogame-inventory-loot
description: TopDownRaid-specific item list, loot tables, balance numbers and visual tints for the shared inventory/loot technique — ItemType values, the magazine/gun item model, heal/armor values, crate lootable ratio and tints, enemy-class tables, HUD prompt wording and the bot-mode env var. Use when changing TopDownRaid items or loot balance; the generic systems live in the shared skill.
---
> Generic technique: see the shared skill `monogame-inventory-loot` in C:\temp\game1\.claude\skills.

# TopDownRaid inventory & loot specifics (`Items/`)

> Update: ammo is now **magazines** (`RifleMag/PistolMag/SmgMag/Shells`, one reload each), guns are items
> (`GunRifle/GunPistol/GunSmg/GunShotgun`, equip from the bag, max 3 carried) and **bodies are containers** searched
> through the mouse-driven screen — see monogame-inventory-screen. Enemy kills no longer burst loot on the floor.

## Items
Original `ItemType { RifleAmmo, PistolAmmo, Medkit, Bandage, ArmorPlate, Coin }`. Medkit: MaxStack 3, "Heals 60";
Bandage heals 20; ArmorPlate `Armor = min(Max, Armor + 50)`; Coins go to `Gold`. Armor absorbs 60 % of incoming damage.
Inventory: 15 slots, hotbar 5 (keys 1..5, inventory panel on Tab). Toast format "+15 5.56 Ammo".

## Loot tables
- `LootTable.Crate = { MinRolls = 2, MaxRolls = 4 }.Add(RifleAmmo, 5, 15, 30).Add(Medkit, 1.2f, 1, 1)...`
- `LootTable.Brawler` (NothingWeight = 2), `LootTable.Gunner`.

## Crates
~45 % of small crates are lootable; interaction range 70 px; `ProjectileSystem.HitCrate`; burst speed 200.
Tints: unopened light `(255,236,170)`, opened dark `(120,105,90)`, white flash on hit. HUD prompts "[E] OPEN CRATE" and
"[E] TAKE BANDAGE x2".

## Icons
`ItemArt.Create(gd, type)`: 28×28 `ShapeSprite`s — ammo box + brass rounds, medkit cross, rolled bandage, armor plate
chevron, coin.

## Bot mode
`GAME1_BOT=1` hoovers `NearbyPickup` automatically so headless raids keep collecting.
