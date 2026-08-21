---
name: monogame-inventory-screen
description: A mouse-driven inventory / loot screen for a MonoGame game — weapon slots with ammo and spare magazines, a backpack grid whose first row is the hotbar, hover-detail panel with weapon stats, LMB use/equip, RMB drop, Ctrl+LMB stash, a side column for the container being searched (bodies) with take / take-all, layout recomputed per frame, cursor shown only while open, and headless verification via an env knob. Use when a MonoGame game needs an inventory UI, a loot/container transfer screen, or mouse hit-testing over a HUD.
---

# Inventory & loot screen (`UI/InventoryScreen.cs`, `UI/UiDraw.cs`)

## Structure
```csharp
public sealed class InventoryScreen {
  bool IsOpen; LootSource? Container;                       // Container = the body/crate being searched
  void Open(p) / OpenWith(p, LootSource) / Close(p) / Toggle(p)   // Close also calls p.CloseLoot() (marks the body searched)
  void Update(InputState, Player, GameContext, screenW, screenH) // hit-test + clicks; ALSO owns the Tab/I/Esc toggling
  void Draw(SpriteBatch, Player, screenW, screenH)              // inside the HUD's SpriteBatch Begin/End (NonPremultiplied, PointClamp)
}
public sealed class LootSource { string Title; Inventory Items; Vector2 Position; Action? OnClosed; bool IsEmpty; }
```
The player raises `LootRequested(LootSource)` when E is pressed on a body; the host does `_inventoryScreen.OpenWith(player, src)`.
`Player.InventoryOpen` mirrors `IsOpen` so gameplay code can block firing/movement and the host can show the OS cursor
(`IsMouseVisible = inventoryOpen || dead || paused || !IsActive`). Walking > reach+60 px away closes the container.

## Layout (recomputed every frame — window is resizable)
```
panel (centred)  = 24 + bagW + 24 [+ bagW + 24 if container]  wide;  56 + 70 + 12 + 3 rows + 12 + 96 + 16 tall
weapons row      = MaxWeapons slots across bagW (short name, icon, "30/30", "2 MAG"), current = blue frame
backpack grid    = 5 cols × 3 rows of 58 px + 6 gap; slots 0..4 carry hotbar key labels
container column = title + same 5×3 grid + footer ("LMB TAKE", [F] TAKE ALL button)
details panel    = full width, 96 px: title, description, weapon stats line, "USES <mag> (n in bag)", or hints when idle
```
Store every rectangle (`_bagSlots[]`, `_containerSlots[]`, `_weaponSlots[]`, buttons) in `Layout()` and hit-test them in
`Update()` — never compute positions twice in different places.

## Input mapping (single place, edge-triggered via InputState)
| Region | LMB | RMB | keys |
|---|---|---|---|
| Bag slot | use / equip (`Player.UseSlot`) — Ctrl+LMB stashes into the open container | drop on the floor (`Player.DropSlot` → pickup burst) | 1–5 use hotbar |
| Container slot | take (`Player.Collect` returns leftover; remove taken from container) | – | F take all |
| Weapon slot | select (`Player.SelectWeapon`) | unequip into the bag (`Player.Unequip`, keeps ≥1 weapon) | Q cycles |
| Take-all / X | button | – | Tab/I/Esc close |
Every action sets a 2 s status line in the details panel ("Took 3 9mm Mag", "Bag full").

## Weapons as items, ammo as magazines
- `WeaponDef.GunItem` (ItemType) ↔ `WeaponDef.ForGunItem(type)`; `Player.EquipFromSlot` fills a free weapon slot (max 3)
  or replaces the current one (old gun goes back to the bag). Guns are `MaxStack 1`, `Usable` (hotbar 1–5 equips).
- `WeaponDef.Mag` is the magazine item; `SpareMags(w) = Inventory.CountOf(mag)`; a reload consumes ONE mag and loads a
  full magazine (`Weapon.FinishReloadIfDue(spareMags)` returns 1) — simple and Tarkov-ish; HUD shows `26/30 | 1 MAG`.
- Shotguns: `Pellets` + `PelletSpread`; the caller loops `w.PelletAngle(ang, i)` and spawns each pellet with a short life.

## Bodies as containers (`Entities/Enemy.cs`)
On death `FillLoot()`: the carried gun's player-grade item (90 %), 1–3 mags for it, plus a per-kind table (coins,
bandage, plate…). `Looted`/`MarkLooted()` on close → fade after 2.5 s; unsearched bodies persist 90 s (`CorpseAlpha`
overrides the base fade). `EnemyManager.FindLootableBodyNear(pos, reach)` skips empty bodies; the HUD shows
"[E] SEARCH BODY" (bodies take priority over crates).

## Font/UI gotchas
- The 5×7 PixelFont has no `·`; use `|` (added) or `-` for separators, and short labels (`WeaponDef.ShortName`) in
  narrow slots. Measure with `font.Measure(text, scale)` before placing right-aligned text.
- Draw the screen after the HUD in the same overlay callback; dim the game with a full-screen `Color(0,0,0,110)` fill.
- Verify headlessly: `GAME1_UI=loot GAME1_SHOT_DELAY=2 GAME1_SCREENSHOT=out.png` opens the screen with a sample
  container (`GAME1_UI=inv` for the bag only) — the way overlapping captions and missing glyphs were caught.
