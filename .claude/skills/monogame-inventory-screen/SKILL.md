---
name: monogame-inventory-screen
description: A mouse-driven inventory / container-transfer screen for a MonoGame game — equipment slots, a backpack grid whose first row doubles as the hotbar, a hover-detail panel, a single click-on-release vs drag-and-drop state machine (move / merge / swap / equip / drop-outside), a right-click inspect popup with its own draggable sub-slots that is hit-tested on top of everything else, a modifier-click shortcut for fast transfer, a second column for the container being searched with take / take-all, a trigger lockout so the click that closes the screen never fires the weapon, a layout recomputed every frame for resizable windows, and headless verification through an env knob. Use when a MonoGame game needs an inventory UI, a loot or container transfer screen, drag-and-drop slots, or reliable mouse hit-testing over a HUD.
---

# Inventory & container screen

## Structure
```csharp
public sealed class InventoryScreen {
  bool IsOpen; LootSource? Container;                       // Container = the external inventory being searched (null = bag only)
  void Open(p) / OpenWith(p, LootSource) / Close(p) / Toggle(p)   // Close also notifies the container (OnClosed) so the world can mark it searched
  void Update(InputState, Player, GameContext, screenW, screenH) // hit-test + clicks; ALSO owns the open/close key handling
  void Draw(SpriteBatch, Player, screenW, screenH)              // inside the HUD's SpriteBatch Begin/End (NonPremultiplied, PointClamp)
}
public sealed class LootSource { string Title; Inventory Items; Vector2 Position; Action? OnClosed; bool IsEmpty; }
```
The player raises an event (e.g. `LootRequested(LootSource)`) when the interact key is pressed near a container; the host
calls `screen.OpenWith(player, src)`. Mirror `IsOpen` onto the player (`Player.InventoryOpen`) so gameplay code can block
firing/movement and the host can show the OS cursor (`IsMouseVisible = inventoryOpen || dead || paused || !IsActive`).
Auto-close the container column when the player walks beyond interaction reach plus a small margin (~60 px is a
starting value) so a container can't be searched from across the map.

## Layout (recomputed every frame — the window is resizable)
```
panel (centred)  = pad + bagW + pad [+ bagW + pad if container]  wide;  header + equipment row + grid rows + details tall
equipment row    = N slots across bagW (short name, icon, ammo/charge text, spare count), current = highlighted frame
backpack grid    = C cols × R rows of (cell + gap) px; slots 0..C-1 carry hotbar key labels
container column = title + same C×R grid + footer ("LMB TAKE", [key] TAKE ALL button)
details panel    = full width strip: title, description, stat line, ammo/consumable info, or usage hints when idle
```
Starting values that read well at 1080p: 58 px cells, 6 px gap, 5×3 grid, 96 px details strip, 24 px panel padding.
Store every rectangle (`_bagSlots[]`, `_containerSlots[]`, `_equipSlots[]`, buttons) in one `Layout()` call and hit-test
exactly those rectangles in `Update()` — never compute positions in two places.

## Input model: click on release + drag & drop (one state machine)
```csharp
Region _pressRegion; int _pressIndex, _pressSub; Vector2 _pressPos; bool _dragging;
LeftPressed  → buttons (Close/TakeAll) act immediately; slots only RECORD the press (region + index + mouse pos)
LeftDown     → if moved > 6 px since press and the source has a payload → _dragging = true (ghost icon at cursor)
LeftReleased → dragging ? DropOnto(hover target) : hover == press ? ClickAction() : nothing
```
`InputState` needs a `LeftReleased` edge (down→up) — deliberately NOT gated on "mouse inside window" so a drag always
ends even if the cursor left the window. Click actions fire on *release over the same slot* (standard button feel);
that is what lets click and drag coexist without a timer.

| Region | LMB click | drag from it | drag onto it |
|---|---|---|---|
| Bag slot | use / equip / wear / fit (Ctrl+LMB = stash into container) | move the stack | place / merge / swap (`MoveStack`) |
| Container slot | take (leftover stays if the bag is full) | move to bag | stash into that slot |
| Equipment slot | select | to bag = unequip | compatible item = equip/replace at that index; same-kind = reorder |
| Sub-slot (attachment / gear) | detach to bag | detach to bag | item matching that sub-slot type |
| outside the panel | – | – | bag item = drop into the world |

`MoveStack(srcInv, si, dstInv, di)`: empty target → place; same type → merge up to `MaxStack` (leftover stays); else
swap the two stacks. It works within one inventory and across bag ⇄ container because both are the same `Inventory`
type. Give the player index-targeted variants for drops (`EquipFromSlotAt(slot, equipIndex)`, `ReorderEquipment(a, b)`
that also fixes the current-selection index so the selection follows the item). Cancel the drag if the container
closes mid-drag (player walked away). Every action sets a short status line (~2 s: "Stashed", "Wrong slot", "Bag full").

## RMB = inspect popup
RMB on any item / equipped item opens a popup panel placed to the right of the main panel (or overlapping its right
edge when there's no room, so the bag stays clear): big icon, name, description, stat line. Items with sub-slots render
them LARGE in the popup (`Region.InspectSub`) and accept drags in/out exactly like the small in-grid sub-slots; an item
that has no per-instance sub-slot state while in the bag shows them greyed with a hint. Esc closes the popup first, the
screen second. **Hit-test the popup rect on top:** if the mouse is inside it, clear the underlying hover before any
slot logic — otherwise drops land on slots under it. The "drop outside the panel = drop item" check must also exclude
the popup rect.

## Trigger lockout (the close-click fires bug)
Closing the screen with a mouse click clears `InventoryOpen` while LMB is still physically held, and the next
`Player.Update` sees `LeftDown && !InventoryOpen` → the weapon fires at the cursor. Fix in the player, not the UI:
```csharp
if (InventoryOpen) _triggerLocked = true; else if (!input.LeftDown) _triggerLocked = false;
bool trigger = input.LeftDown && !_triggerLocked && !InventoryOpen;   // start true: also swallows menu clicks at spawn
```

## Equipment as items, ammo as consumable stacks
- Map each equipment definition to an item type both ways (`Def.Item` ↔ `Def.ForItem(type)`); `EquipFromSlot` fills a
  free equipment slot or replaces the current one (the old item goes back to the bag). Equipment is `MaxStack 1` and
  `Usable` so the hotbar keys equip it.
- Treat ammo as a magazine/charge item: `Spare = Inventory.CountOf(magItem)`; a reload consumes ONE and fills the
  weapon — simple to display (`26/30 | 1 MAG`) and simple to balance.

## Containers in the world
Anything with an `Inventory` can be a `LootSource` (crates, bodies, vehicles). On close, mark it searched via
`OnClosed` so it can fade or be skipped; a `FindLootableNear(pos, reach)` query should skip empty containers and the HUD
shows a context prompt ("[E] SEARCH ...").

## Font/UI gotchas
- Tiny bitmap fonts often lack glyphs like `·`; use `|` or `-` as separators and short display names in narrow slots.
  Measure with `font.Measure(text, scale)` before placing right-aligned text.
- Draw the screen after the HUD in the same overlay callback; dim the game with a full-screen `Color(0,0,0,110)` fill.
- Verify headlessly with env knobs that open the screen with a sample container (e.g. `UI=loot`, `UI=inv` for bag only)
  plus the screenshot knob from monogame-headless-screenshots — this is how overlapping captions and missing glyphs get
  caught without a human at the screen.
