---
name: monogame-inventory-screen
description: TopDownRaid-specific styling and game rules for the inventory / loot screen — weapon slot count, magazine and attachment rules, shotgun pellets, enemy-body loot tables and corpse timers, HUD copy and PixelFont quirks, and the GAME1_UI headless knobs. The generic screen technique lives in the shared skill.
---
> Generic technique: see the shared skill `monogame-inventory-screen` in C:\temp\game1\.claude\skills.

# TopDownRaid inventory specifics (`UI/InventoryScreen.cs`, `UI/UiDraw.cs`)

- Toggle keys: Tab / I / Esc, handled inside `InventoryScreen.Update`. `Close(p)` also calls `p.CloseLoot()` (marks the
  body searched). Container auto-closes when the player is > reach + 60 px away.
- Layout: panel = 24 + bagW + 24 [+ bagW + 24]; height 56 + 70 + 12 + 3 rows + 12 + 96 + 16. Weapons row =
  `MaxWeapons` (3) slots ("30/30", "2 MAG"), current weapon = blue frame. Bag grid 5×3 of 58 px + 6 gap; slots 0..4 are
  hotbar 1–5. Container footer: "LMB TAKE", `[F] TAKE ALL`. Details line: "USES <mag> (n in bag)".
- Regions: bag, container, weapon, attach mini-slot (4 `AttachSlot`s per gun), helmet/vest gear slots. Player API:
  `EquipFromSlotAt(slot, weaponIndex)`, `AttachFromSlotTo(slot, weaponIndex)`, `ReorderWeapons(a, b)` (fixes
  `WeaponIndex`), `Player.Collect`, `Player.DropSlot`. Coins dragged from a container convert straight to gold.
- Inspect popup: guns render the 4 attachment slots large (`Region.InspectAttach`); a bag gun has no per-item attachment
  state and shows "EQUIP THE GUN TO FIT THEM".
- Weapons: `WeaponDef.GunItem` ↔ `WeaponDef.ForGunItem(type)`; `WeaponDef.Mag` is the magazine item;
  `SpareMags(w) = Inventory.CountOf(mag)`; `Weapon.FinishReloadIfDue(spareMags)` returns 1 mag consumed. HUD shows
  `26/30 | 1 MAG`. Shotguns: `Pellets` + `PelletSpread`; caller loops `w.PelletAngle(ang, i)` and spawns short-lived pellets.
- Bodies as containers (`Entities/Enemy.cs`): on death `FillLoot()` = carried gun's player-grade item (90 %), 1–3 mags
  for it, plus a per-kind table (coins, bandage, plate…). `Looted`/`MarkLooted()` on close → fade after 2.5 s;
  unsearched bodies persist 90 s (`CorpseAlpha` overrides the base fade). `EnemyManager.FindLootableBodyNear(pos, reach)`
  skips empty bodies; HUD prompt "[E] SEARCH BODY" (bodies take priority over crates).
- The 5×7 PixelFont has no `·`; use `|` (added) or `-`; use `WeaponDef.ShortName` in slots.
- Headless: `GAME1_UI=loot GAME1_SHOT_DELAY=2 GAME1_SCREENSHOT=out.png` opens the screen with a sample container;
  `GAME1_UI=inv` for the bag only.
