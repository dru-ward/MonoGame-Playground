using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Game.Combat;
using Game.Core;
using Game.Entities;
using Game.Graphics;
using Game.Items;

namespace Game.UI;

/// <summary>
/// The inventory / loot screen. Left: weapon slots + backpack grid (hotbar = first row). Right (when searching a
/// body): the container's items. Bottom: details of whatever the mouse is over. Mouse driven:
///   LMB click = use / equip / take / select · LMB drag = move / swap / stash / fit (drag out of the panel = drop)
///   RMB = inspect (guns show attachment slots you can drag onto) · Ctrl+LMB = stash · F = take all
///   Tab / I / Esc = close (Esc closes the inspect popup first).
/// </summary>
public sealed class InventoryScreen
{
    public bool IsOpen { get; private set; }
    public LootSource? Container { get; private set; }

    private readonly PixelFont _font;
    private readonly Texture2D _px;
    private readonly Dictionary<ItemType, SpritePair> _icons;

    // layout (recomputed each frame from the screen size)
    private const int Slot = 58, Gap = 6, Cols = 5;
    private Rectangle _panel, _weaponsArea, _bagArea, _containerArea, _detailsArea;
    private readonly Rectangle[] _bagSlots = new Rectangle[Inventory.DefaultSlots];
    private readonly Rectangle[] _containerSlots = new Rectangle[Inventory.DefaultSlots];
    private readonly Rectangle[] _weaponSlots = new Rectangle[Player.MaxWeapons];
    private Rectangle _takeAllBtn, _closeBtn;

    // hover state
    private enum Region { None, Bag, Container, Weapon, Attach, Helmet, Vest, TakeAll, Close, InspectAttach, InspectClose }
    private Region _hoverRegion; private int _hoverIndex = -1, _hoverSub = -1;
    private const int Mini = 18;
    private readonly Rectangle[,] _attachSlots = new Rectangle[Player.MaxWeapons, 4];
    private Rectangle _helmetSlot, _vestSlot;
    private static readonly AttachSlot[] SlotOrder = { AttachSlot.Optic, AttachSlot.Muzzle, AttachSlot.Tactical, AttachSlot.Grip };
    private string _status = ""; private float _statusTimer;

    // drag & drop: LMB press remembers the source; moving a few px turns it into a drag, release drops/clicks
    private Region _pressRegion = Region.None; private int _pressIndex = -1, _pressSub = -1;
    private Vector2 _pressPos, _mousePos;
    private bool _dragging;

    // inspect popup (RMB an item / weapon)
    private ItemStack _inspectStack;                  // inspected item (empty when inspecting an equipped weapon)
    private int _inspectWeapon = -1;                  // >= 0: inspecting the equipped weapon at this index
    private Rectangle _inspectPanel, _inspectClose;
    private readonly Rectangle[] _inspectAttach = new Rectangle[4];
    private bool InspectOpen => _inspectWeapon >= 0 || !_inspectStack.IsEmpty;

    public InventoryScreen(PixelFont font, Texture2D pixel, Dictionary<ItemType, SpritePair> icons) { _font = font; _px = pixel; _icons = icons; }

    public void Open(Player p) { IsOpen = true; p.InventoryOpen = true; }
    public void OpenWith(Player p, LootSource src) { Container = src; Open(p); }
    public void Close(Player p)
    {
        IsOpen = false; p.InventoryOpen = false; Container = null; p.CloseLoot();
        _pressRegion = Region.None; _dragging = false; CloseInspect();
    }
    public void Toggle(Player p) { if (IsOpen) Close(p); else Open(p); }

    // ================================================================================================= update
    public void Update(InputState input, Player p, GameContext ctx, int screenW, int screenH)
    {
        _statusTimer = MathF.Max(0f, _statusTimer - 1f / 60f);
        if (!IsOpen)
        {
            if (input.Pressed(Keys.Tab) || input.Pressed(Keys.I)) Open(p);
            return;
        }
        if (input.Pressed(Keys.Escape)) { if (InspectOpen) CloseInspect(); else { Close(p); return; } }
        if (input.Pressed(Keys.Tab) || input.Pressed(Keys.I)) { Close(p); return; }
        if (!p.IsAlive) { Close(p); return; }
        if (Container != null && p.OpenLoot == null) Container = null;        // player walked away → container closed itself
        if (Container == null && (_pressRegion == Region.Container || _dragging && _pressRegion == Region.Container)) { _pressRegion = Region.None; _dragging = false; }
        if (_inspectWeapon >= p.Weapons.Count) CloseInspect();                // inspected weapon got unequipped

        Layout(screenW, screenH);
        var m = _mousePos = input.MouseScreen;
        _hoverRegion = Region.None; _hoverIndex = -1; _hoverSub = -1;
        for (int i = 0; i < _weaponSlots.Length; i++) if (_weaponSlots[i].Contains(m)) { _hoverRegion = Region.Weapon; _hoverIndex = i; }
        for (int i = 0; i < Player.MaxWeapons; i++) for (int a = 0; a < 4; a++) if (_attachSlots[i, a].Contains(m)) { _hoverRegion = Region.Attach; _hoverIndex = i; _hoverSub = a; }
        if (_helmetSlot.Contains(m)) _hoverRegion = Region.Helmet; if (_vestSlot.Contains(m)) _hoverRegion = Region.Vest;
        for (int i = 0; i < _bagSlots.Length; i++) if (_bagSlots[i].Contains(m)) { _hoverRegion = Region.Bag; _hoverIndex = i; }
        if (Container != null) for (int i = 0; i < _containerSlots.Length; i++) if (_containerSlots[i].Contains(m)) { _hoverRegion = Region.Container; _hoverIndex = i; }
        if (Container != null && _takeAllBtn.Contains(m)) _hoverRegion = Region.TakeAll;
        if (_closeBtn.Contains(m)) _hoverRegion = Region.Close;
        if (InspectOpen && _inspectPanel.Contains(m))                          // popup sits on top of everything
        {
            _hoverRegion = Region.None; _hoverIndex = -1; _hoverSub = -1;
            if (_inspectWeapon >= 0)
                for (int a = 0; a < 4; a++) if (_inspectAttach[a].Contains(m)) { _hoverRegion = Region.InspectAttach; _hoverSub = a; }
            if (_inspectClose.Contains(m)) _hoverRegion = Region.InspectClose;
        }

        bool ctrl = input.AnyDown(Keys.LeftControl, Keys.RightControl);
        if (input.LeftPressed)
        {
            switch (_hoverRegion)
            {
                case Region.Close: Close(p); return;
                case Region.InspectClose: CloseInspect(); break;
                case Region.TakeAll: TakeAll(p); break;
                default:
                    _pressRegion = _hoverRegion; _pressIndex = _hoverIndex; _pressSub = _hoverSub;
                    _pressPos = m; _dragging = false;
                    break;
            }
        }
        if (_pressRegion != Region.None && input.LeftDown && !_dragging && (m - _pressPos).LengthSquared() > 36f)
            _dragging = DragItemType(p) != null;
        if (input.LeftReleased && _pressRegion != Region.None)
        {
            if (_dragging) DropOnto(p, ctx, m);
            else if (_hoverRegion == _pressRegion && _hoverIndex == _pressIndex && _hoverSub == _pressSub)
                ClickAction(p, ctx, ctrl);
            _pressRegion = Region.None; _dragging = false;
        }
        if (input.RightPressed)
        {
            switch (_hoverRegion)
            {
                case Region.Bag: if (!p.Inventory[_hoverIndex].IsEmpty) Inspect(p.Inventory[_hoverIndex]); break;
                case Region.Container: if (Container != null && !Container.Items[_hoverIndex].IsEmpty) Inspect(Container.Items[_hoverIndex]); break;
                case Region.Weapon: if (_hoverIndex < p.Weapons.Count) InspectWeapon(_hoverIndex); break;
                case Region.Attach: if (_hoverIndex < p.Weapons.Count && p.Weapons[_hoverIndex].Attachments.TryGetValue(SlotOrder[_hoverSub], out var at)) Inspect(new ItemStack(at, 1)); break;
                case Region.Helmet: if (p.Helmet is { } hm) Inspect(new ItemStack(hm, 1)); break;
                case Region.Vest: if (p.Vest is { } vs) Inspect(new ItemStack(vs, 1)); break;
                default: CloseInspect(); break;
            }
        }
        if (Container != null && input.Pressed(Keys.F)) TakeAll(p);
        // number keys still use hotbar slots while the screen is open
        for (int i = 0; i < Inventory.HotbarSize; i++) if (input.Pressed(Keys.D1 + i)) p.UseSlot(i, ctx);
    }

    /// <summary>A released click (no drag): the classic use / equip / take / select actions.</summary>
    private void ClickAction(Player p, GameContext ctx, bool ctrl)
    {
        switch (_pressRegion)
        {
            case Region.Bag:
                if (Container != null && ctrl) Stash(p, _pressIndex);
                else if (_pressIndex < p.Inventory.Count && !p.Inventory[_pressIndex].IsEmpty) p.UseSlot(_pressIndex, ctx);
                break;
            case Region.Container: Take(p, _pressIndex, all: false); break;
            case Region.Weapon: if (_pressIndex < p.Weapons.Count) p.SelectWeapon(_pressIndex); break;
            case Region.Attach: if (_pressIndex < p.Weapons.Count && p.DetachToBag(_pressIndex, SlotOrder[_pressSub])) Status("Attachment removed"); break;
            case Region.InspectAttach: if (_inspectWeapon >= 0 && p.DetachToBag(_inspectWeapon, SlotOrder[_pressSub])) Status("Attachment removed"); break;
            case Region.Helmet: if (p.Unwear(GearSlot.Helmet)) Status("Helmet removed"); break;
            case Region.Vest: if (p.Unwear(GearSlot.Vest)) Status("Vest removed"); break;
        }
    }

    /// <summary>The item type a drag from the pressed slot would carry, or null if there is nothing to drag.</summary>
    private ItemType? DragItemType(Player p)
    {
        switch (_pressRegion)
        {
            case Region.Bag: return p.Inventory[_pressIndex].IsEmpty ? null : p.Inventory[_pressIndex].Type;
            case Region.Container: return Container != null && !Container.Items[_pressIndex].IsEmpty ? Container.Items[_pressIndex].Type : null;
            case Region.Weapon: return _pressIndex < p.Weapons.Count ? p.Weapons[_pressIndex].Def.GunItem : null;
            case Region.Attach:
                if (_pressIndex < p.Weapons.Count && p.Weapons[_pressIndex].Attachments.TryGetValue(SlotOrder[_pressSub], out var at)) return at;
                return null;
            case Region.InspectAttach:
                if (_inspectWeapon >= 0 && p.Weapons[_inspectWeapon].Attachments.TryGetValue(SlotOrder[_pressSub], out var iat)) return iat;
                return null;
            case Region.Helmet: return p.Helmet;
            case Region.Vest: return p.Vest;
            default: return null;
        }
    }

    /// <summary>Drops the dragged payload onto whatever is under the mouse.</summary>
    private void DropOnto(Player p, GameContext ctx, Vector2 m)
    {
        int si = _pressIndex, di = _hoverIndex;
        switch (_pressRegion)
        {
            case Region.Bag:
                var s = p.Inventory[si]; if (s.IsEmpty) break;
                switch (_hoverRegion)
                {
                    case Region.Bag: if (di != si) MoveStack(p.Inventory, si, p.Inventory, di); break;
                    case Region.Container: if (Container != null) { MoveStack(p.Inventory, si, Container.Items, di); Status("Stashed"); } break;
                    case Region.Weapon:
                        if (s.Def.IsWeapon) p.EquipFromSlotAt(si, di);
                        else if (s.Def.IsAttachment && di < p.Weapons.Count) p.AttachFromSlotTo(si, di);
                        break;
                    case Region.Attach: DropAttachment(p, si, di, _hoverSub); break;
                    case Region.InspectAttach: if (_inspectWeapon >= 0) DropAttachment(p, si, _inspectWeapon, _hoverSub); break;
                    case Region.Helmet: if (GearDef.For(s.Type)?.Slot == GearSlot.Helmet) p.WearFromSlot(si); else Status("Not a helmet"); break;
                    case Region.Vest: if (GearDef.For(s.Type)?.Slot == GearSlot.Vest) p.WearFromSlot(si); else Status("Not a vest"); break;
                    case Region.None:
                        if (!_panel.Contains(m) && !(InspectOpen && _inspectPanel.Contains(m))) p.DropSlot(si, ctx);
                        break;
                }
                break;
            case Region.Container:
                if (Container == null) break;
                var cs = Container.Items[si]; if (cs.IsEmpty) break;
                switch (_hoverRegion)
                {
                    case Region.Bag:
                        if (cs.Type == ItemType.Coin) { p.Collect(cs); Container.Items.SetSlot(si, default); }
                        else MoveStack(Container.Items, si, p.Inventory, di);
                        break;
                    case Region.Container: if (di != si) MoveStack(Container.Items, si, Container.Items, di); break;
                }
                break;
            case Region.Weapon:
                if (si >= p.Weapons.Count) break;
                if (_hoverRegion == Region.Weapon) p.ReorderWeapons(si, di);
                else if (_hoverRegion == Region.Bag && p.Unequip(si)) Status("Unequipped");
                break;
            case Region.Attach:
                if (_hoverRegion == Region.Bag && si < p.Weapons.Count && p.DetachToBag(si, SlotOrder[_pressSub])) Status("Attachment removed");
                break;
            case Region.InspectAttach:
                if (_hoverRegion == Region.Bag && _inspectWeapon >= 0 && p.DetachToBag(_inspectWeapon, SlotOrder[_pressSub])) Status("Attachment removed");
                break;
            case Region.Helmet: if (_hoverRegion == Region.Bag && p.Unwear(GearSlot.Helmet)) Status("Helmet removed"); break;
            case Region.Vest: if (_hoverRegion == Region.Bag && p.Unwear(GearSlot.Vest)) Status("Vest removed"); break;
        }
    }

    private void DropAttachment(Player p, int bagSlot, int weaponIndex, int attachSub)
    {
        var s = p.Inventory[bagSlot];
        if (!s.Def.IsAttachment || weaponIndex >= p.Weapons.Count) return;
        if (AttachmentDef.For(s.Type)?.Slot != SlotOrder[attachSub]) { Status("Wrong slot"); return; }
        p.AttachFromSlotTo(bagSlot, weaponIndex);
    }

    /// <summary>Moves a stack between two slots: place into empty, merge same type, otherwise swap.</summary>
    private static void MoveStack(Inventory src, int si, Inventory dst, int di)
    {
        if (si < 0 || di < 0 || si >= src.Count || di >= dst.Count) return;
        var a = src[si]; if (a.IsEmpty) return;
        var b = dst[di];
        if (b.IsEmpty) { dst.SetSlot(di, a); src.SetSlot(si, default); }
        else if (b.Type == a.Type && b.Count < b.Def.MaxStack)
        {
            int n = Math.Min(b.Def.MaxStack - b.Count, a.Count);
            b.Count += n; a.Count -= n;
            dst.SetSlot(di, b); src.SetSlot(si, a.Count <= 0 ? default : a);
        }
        else { dst.SetSlot(di, a); src.SetSlot(si, b); }                       // swap
    }

    // ================================================================================================= inspect
    private void Inspect(ItemStack stack) { _inspectStack = stack; _inspectWeapon = -1; }
    private void InspectWeapon(int weaponIndex) { _inspectStack = default; _inspectWeapon = weaponIndex; }
    private void CloseInspect() { _inspectStack = default; _inspectWeapon = -1; }

    private void Take(Player p, int index, bool all)
    {
        if (Container == null || index < 0 || index >= Container.Items.Count) return;
        var s = Container.Items[index]; if (s.IsEmpty) return;
        int leftover = p.Collect(s);
        int taken = s.Count - leftover;
        if (taken > 0) Container.Items.Remove(s.Type, taken);
        Status(taken > 0 ? $"Took {taken} {s.Def.Name}" : "Bag full");
    }

    private void TakeAll(Player p)
    {
        if (Container == null) return;
        int took = 0;
        for (int i = 0; i < Container.Items.Count; i++)
        {
            var s = Container.Items[i]; if (s.IsEmpty) continue;
            int leftover = p.Collect(s); int taken = s.Count - leftover;
            if (taken > 0) { Container.Items.Remove(s.Type, taken); took += taken; }
        }
        Status(took > 0 ? $"Took everything that fit ({took})" : "Nothing fits");
    }

    private void Stash(Player p, int bagIndex)
    {
        if (Container == null) return;
        var s = p.Inventory[bagIndex]; if (s.IsEmpty) return;
        int leftover = Container.Items.Add(s.Type, s.Count);
        int moved = s.Count - leftover;
        if (moved > 0) { p.Inventory.Remove(s.Type, moved); Status($"Stashed {moved} {s.Def.Name}"); }
    }

    private void Status(string s) { _status = s; _statusTimer = 2f; }

    // ================================================================================================= layout
    private void Layout(int screenW, int screenH)
    {
        bool hasContainer = Container != null;
        int bagW = Cols * Slot + (Cols - 1) * Gap;
        int panelW = 24 + bagW + 24 + (hasContainer ? bagW + 24 : 0);
        int panelH = 56 + 62 + 3 + Mini + 20 + 44 + 26 + 3 * (Slot + Gap) + 12 + 96 + 16;
        _panel = new Rectangle((screenW - panelW) / 2, (screenH - panelH) / 2 - 20, panelW, panelH);
        _closeBtn = new Rectangle(_panel.Right - 30, _panel.Y + 8, 22, 22);

        int x0 = _panel.X + 24, y = _panel.Y + 56;
        _weaponsArea = new Rectangle(x0, y, bagW, 62);
        int wslotW = (bagW - (Player.MaxWeapons - 1) * Gap) / Player.MaxWeapons;
        for (int i = 0; i < Player.MaxWeapons; i++)
        {
            _weaponSlots[i] = new Rectangle(x0 + i * (wslotW + Gap), y, wslotW, 62);
            for (int a = 0; a < 4; a++) _attachSlots[i, a] = new Rectangle(_weaponSlots[i].X + a * (Mini + 3), _weaponSlots[i].Bottom + 3, Mini, Mini);
        }
        y += 62 + 3 + Mini + 20;
        int gw = (bagW - Gap) / 2;
        _helmetSlot = new Rectangle(x0, y, gw, 44); _vestSlot = new Rectangle(x0 + gw + Gap, y, gw, 44);
        y += 44 + 26;
        _bagArea = new Rectangle(x0, y, bagW, 3 * (Slot + Gap) - Gap);
        for (int i = 0; i < Inventory.DefaultSlots; i++)
            _bagSlots[i] = new Rectangle(x0 + (i % Cols) * (Slot + Gap), y + (i / Cols) * (Slot + Gap), Slot, Slot);
        if (hasContainer)
        {
            int cx = x0 + bagW + 24;
            _containerArea = new Rectangle(cx, _panel.Y + 56, bagW, 32 + 3 * (Slot + Gap) + 34);
            for (int i = 0; i < Inventory.DefaultSlots; i++)
                _containerSlots[i] = new Rectangle(cx + (i % Cols) * (Slot + Gap), _containerArea.Y + 32 + (i / Cols) * (Slot + Gap), Slot, Slot);
            _takeAllBtn = new Rectangle(cx + bagW - 130, _containerArea.Bottom - 30, 124, 24);
        }
        _detailsArea = new Rectangle(x0, _bagArea.Bottom + 12, panelW - 48, 96);

        // inspect popup: to the right of the panel if it fits, else overlapping its right edge (bag stays clear)
        if (InspectOpen)
        {
            int iw = 340, ih = 216;
            int px = _panel.Right + 10;
            if (px + iw > screenW - 6) px = Math.Max(6, _panel.Right - iw - 12);
            _inspectPanel = new Rectangle(px, Math.Max(6, _panel.Y + 40), iw, ih);
            _inspectClose = new Rectangle(_inspectPanel.Right - 28, _inspectPanel.Y + 6, 22, 22);
            const int A = 40;
            for (int a = 0; a < 4; a++)
                _inspectAttach[a] = new Rectangle(_inspectPanel.X + 16 + a * (A + 10), _inspectPanel.Bottom - A - 34, A, A);
        }
    }

    // ================================================================================================= draw
    public void Draw(SpriteBatch sb, Player p, int screenW, int screenH)
    {
        if (!IsOpen) return;
        Layout(screenW, screenH);
        UiDraw.Fill(sb, _px, new Rectangle(0, 0, screenW, screenH), new Color(0, 0, 0, 110));   // dim the game
        UiDraw.PanelBox(sb, _px, _panel);
        _font.DrawShadowed(sb, "INVENTORY", new Vector2(_panel.X + 24, _panel.Y + 12), Color.White, 2.5f);
        if (Container != null) _font.DrawShadowed(sb, $"GOLD {p.Gold}   HP {p.Health:0}", new Vector2(_panel.X + 190, _panel.Y + 18), UiDraw.Accent, 1.5f);
        UiDraw.PanelBox(sb, _px, _closeBtn, new Color(60, 20, 20, 220), _hoverRegion == Region.Close ? Color.White : UiDraw.PanelLight);
        UiDraw.TextCentered(sb, _font, "X", _closeBtn, Color.White, 2f);

        // ---- weapons ----------------------------------------------------------------------------------------
        _font.DrawShadowed(sb, "WEAPONS   [Q] CYCLE | LMB SELECT | RMB INSPECT | DRAG TO BAG = UNEQUIP", new Vector2(_weaponsArea.X, _weaponsArea.Y - 14), Color.Gray, 1.5f);
        for (int i = 0; i < Player.MaxWeapons; i++)
        {
            var r = _weaponSlots[i];
            bool has = i < p.Weapons.Count, current = has && i == p.WeaponIndex;
            UiDraw.PanelBox(sb, _px, r, current ? new Color(30, 45, 60, 230) : UiDraw.Panel, current ? new Color(120, 200, 255) : UiDraw.PanelLight);
            if (_hoverRegion == Region.Weapon && _hoverIndex == i) UiDraw.Fill(sb, _px, r, UiDraw.Hover);
            if (!has) { UiDraw.TextCentered(sb, _font, "EMPTY", r, Color.DimGray, 1.5f); continue; }
            var w = p.Weapons[i];
            _font.DrawShadowed(sb, w.Def.ShortName, new Vector2(r.X + 6, r.Y + 5), current ? Color.White : Color.LightGray, 1.5f);
            if (w.Def.GunItem is { } gi) UiDraw.Icon(sb, _icons[gi].Albedo, new Rectangle(r.X + 2, r.Y + 18, 42, 42), 3);
            _font.DrawShadowed(sb, w.IsReloading ? "RLD" : $"{w.AmmoInMag}/{w.Def.MagSize}", new Vector2(r.X + 46, r.Y + 24), UiDraw.Accent, 1.5f);
            if (w.Def.IsMelee) _font.DrawShadowed(sb, "MELEE", new Vector2(r.X + 46, r.Y + 24), UiDraw.Accent, 1.5f);
            else _font.DrawShadowed(sb, $"{p.SpareMags(w)} MAG", new Vector2(r.X + 46, r.Y + 40), Color.LightGray, 1.5f);
            for (int a = 0; a < 4; a++)
            {
                var ar = _attachSlots[i, a]; var slot = SlotOrder[a];
                bool allowed = !w.Def.IsMelee && AttachPoints.Allows(w.Def.Held, slot);
                UiDraw.PanelBox(sb, _px, ar, allowed ? new Color(18, 20, 24, 220) : new Color(10, 10, 12, 160), allowed ? UiDraw.PanelLight : new Color(255, 255, 255, 12));
                if (w.Attachments.TryGetValue(slot, out var at)) UiDraw.Icon(sb, _icons[at].Albedo, ar, 2);
                else if (allowed) UiDraw.TextCentered(sb, _font, slot.ToString().Substring(0, 1), ar, Color.DimGray, 1f);
                if (_hoverRegion == Region.Attach && _hoverIndex == i && _hoverSub == a) UiDraw.Fill(sb, _px, ar, UiDraw.Hover);
            }
        }
        // ---- gear -----------------------------------------------------------------------------------------
        _font.DrawShadowed(sb, "GEAR   LMB REMOVE TO BAG   (WEAR: LMB ON A VEST/HELMET IN THE BAG)", new Vector2(_helmetSlot.X, _helmetSlot.Y - 14), Color.Gray, 1.5f);
        DrawGear(sb, _helmetSlot, "HELMET", p.Helmet, _hoverRegion == Region.Helmet, null);
        DrawGear(sb, _vestSlot, "VEST", p.Vest, _hoverRegion == Region.Vest, p.Vest != null ? $"{p.Armor:0}/{p.MaxArmor:0}" : null);

        // ---- bag ------------------------------------------------------------------------------------------
        _font.DrawShadowed(sb, "BACKPACK   (1-5 = HOTBAR)   DRAG TO MOVE / SWAP", new Vector2(_bagArea.X, _bagArea.Y - 14), Color.Gray, 1.5f);
        for (int i = 0; i < Inventory.DefaultSlots; i++)
            DrawSlot(sb, _bagSlots[i], p.Inventory[i], i < Inventory.HotbarSize ? (i + 1).ToString() : null, _hoverRegion == Region.Bag && _hoverIndex == i);

        // ---- container ------------------------------------------------------------------------------------
        if (Container != null)
        {
            UiDraw.PanelBox(sb, _px, _containerArea, new Color(20, 14, 10, 200), new Color(200, 160, 100, 120));
            _font.DrawShadowed(sb, Container.Title, new Vector2(_containerArea.X + 8, _containerArea.Y + 9), UiDraw.Accent, 1.5f);
            _font.DrawShadowed(sb, "LMB TAKE / DRAG", new Vector2(_containerArea.X + 8, _containerArea.Bottom - 22), Color.Gray, 1.5f);
            UiDraw.PanelBox(sb, _px, _takeAllBtn, new Color(40, 60, 30, 230), _hoverRegion == Region.TakeAll ? Color.White : UiDraw.PanelLight);
            UiDraw.TextCentered(sb, _font, "[F] TAKE ALL", _takeAllBtn, Color.White, 1.5f);
            for (int i = 0; i < Inventory.DefaultSlots; i++)
                DrawSlot(sb, _containerSlots[i], Container.Items[i], null, _hoverRegion == Region.Container && _hoverIndex == i);
            if (Container.IsEmpty) UiDraw.TextCentered(sb, _font, "EMPTY", new Rectangle(_containerArea.X, _containerArea.Y + 32, _containerArea.Width, _containerArea.Height - 66), Color.DimGray, 2f);
        }

        // ---- details --------------------------------------------------------------------------------------
        UiDraw.PanelBox(sb, _px, _detailsArea, new Color(14, 16, 20, 220));
        DrawDetails(sb, p);
        if (_statusTimer > 0f) _font.DrawShadowed(sb, _status, new Vector2(_detailsArea.Right - _font.Measure(_status, 1.5f).X - 8, _detailsArea.Bottom - 16), UiDraw.Accent, 1.5f);
        _font.DrawShadowed(sb, "[TAB] CLOSE", new Vector2(_panel.Right - 96, _panel.Bottom - 14), Color.Gray, 1.5f);

        DrawInspect(sb, p);
        if (_dragging && DragItemType(p) is { } dragType)                      // ghost of the dragged item
            UiDraw.Icon(sb, _icons[dragType].Albedo, new Rectangle((int)_mousePos.X - 24, (int)_mousePos.Y - 24, 48, 48), 4);
    }

    /// <summary>The RMB inspect popup: big icon, stats; guns get the four attachment slots (drag in / out).</summary>
    private void DrawInspect(SpriteBatch sb, Player p)
    {
        if (!InspectOpen) return;
        bool eq = _inspectWeapon >= 0;
        if (eq && _inspectWeapon >= p.Weapons.Count) { CloseInspect(); return; }
        var w = eq ? p.Weapons[_inspectWeapon] : null;
        WeaponDef? wdef = eq ? w!.Def : _inspectStack.Def.IsWeapon ? WeaponDef.ForGunItem(_inspectStack.Type) : null;
        ItemType? icon = eq ? w!.Def.GunItem : _inspectStack.Type;
        string name = eq ? w!.Def.Name : _inspectStack.Def.Name;

        UiDraw.PanelBox(sb, _px, _inspectPanel, new Color(16, 18, 24, 245), UiDraw.Accent);
        UiDraw.PanelBox(sb, _px, _inspectClose, new Color(60, 20, 20, 220), _hoverRegion == Region.InspectClose ? Color.White : UiDraw.PanelLight);
        UiDraw.TextCentered(sb, _font, "X", _inspectClose, Color.White, 2f);
        int x = _inspectPanel.X + 16, y = _inspectPanel.Y + 10;
        _font.DrawShadowed(sb, "INSPECT", new Vector2(x, y), Color.Gray, 1.5f);
        _font.DrawShadowed(sb, name.ToUpperInvariant(), new Vector2(x, y + 16), Color.White, 2f);
        if (icon is { } ic) UiDraw.Icon(sb, _icons[ic].Albedo, new Rectangle(_inspectPanel.Right - 92, _inspectPanel.Y + 36, 72, 72), 5);
        y += 44;
        _font.DrawShadowed(sb, wdef?.Description ?? _inspectStack.Def.Description, new Vector2(x, y), Color.LightGray, 1.5f); y += 20;
        if (wdef != null)
        {
            int rpm = (int)MathF.Round(60f / wdef.FireInterval);
            _font.DrawShadowed(sb, wdef.IsMelee ? $"DMG {wdef.Damage:0}   SWINGS {rpm}/MIN   REACH {wdef.Range:0}"
                : $"DMG {wdef.Damage:0}{(wdef.Pellets > 1 ? $"x{wdef.Pellets}" : "")}   RPM {rpm}   MAG {wdef.MagSize}   {(wdef.Automatic ? "AUTO" : "SEMI")}",
                new Vector2(x, y), UiDraw.Accent, 1.5f); y += 18;
            if (eq && !wdef.IsMelee) { _font.DrawShadowed(sb, $"LOADED {w!.AmmoInMag}/{wdef.MagSize}   {p.SpareMags(w)} SPARE MAG", new Vector2(x, y), Color.LightGray, 1.5f); y += 18; }
        }
        else if (_inspectStack.Def.IsAttachment && AttachmentDef.For(_inspectStack.Type) is { } ad)
            _font.DrawShadowed(sb, $"SLOT {ad.Slot.ToString().ToUpperInvariant()}   SPREAD x{ad.SpreadMul:0.00}  RECOIL x{ad.RecoilMul:0.00}  FLASH x{ad.FlashMul:0.00}", new Vector2(x, y), UiDraw.Accent, 1.5f);
        else if (_inspectStack.Def.IsGear && GearDef.For(_inspectStack.Type) is { } gd)
            _font.DrawShadowed(sb, gd.Slot == GearSlot.Vest ? $"ARMOR {gd.MaxArmor:0}   ABSORBS {gd.Absorb * 100:0}%   SPEED x{gd.SpeedMul:0.00}" : $"DAMAGE TAKEN -{gd.DamageReduction * 100:0}%", new Vector2(x, y), UiDraw.Accent, 1.5f);

        if (wdef != null && !wdef.IsMelee)
        {
            _font.DrawShadowed(sb, eq ? "ATTACHMENTS   DRAG IN FROM BAG · LMB / DRAG OUT REMOVES" : "ATTACHMENTS   (EQUIP THE GUN TO FIT THEM)",
                new Vector2(x, _inspectAttach[0].Y - 16), Color.Gray, 1.5f);
            for (int a = 0; a < 4; a++)
            {
                var ar = _inspectAttach[a]; var slot = SlotOrder[a];
                bool allowed = AttachPoints.Allows(wdef.Held, slot);
                UiDraw.PanelBox(sb, _px, ar, allowed && eq ? new Color(18, 20, 24, 230) : new Color(10, 10, 12, 160), allowed && eq ? UiDraw.PanelLight : new Color(255, 255, 255, 12));
                if (eq && w!.Attachments.TryGetValue(slot, out var at)) UiDraw.Icon(sb, _icons[at].Albedo, ar, 4);
                else if (allowed) UiDraw.TextCentered(sb, _font, slot.ToString().Substring(0, 3).ToUpperInvariant(), ar, Color.DimGray, 1f);
                if (_hoverRegion == Region.InspectAttach && _hoverSub == a) UiDraw.Fill(sb, _px, ar, UiDraw.Hover);
            }
        }
    }

    private void DrawGear(SpriteBatch sb, Rectangle r, string label, ItemType? item, bool hover, string? extra)
    {
        UiDraw.PanelBox(sb, _px, r, new Color(18, 20, 24, 220), UiDraw.PanelLight);
        if (hover) UiDraw.Fill(sb, _px, r, UiDraw.Hover);
        _font.DrawShadowed(sb, label, new Vector2(r.X + 6, r.Y + 4), Color.Gray, 1.5f);
        if (item is { } it)
        {
            UiDraw.Icon(sb, _icons[it].Albedo, new Rectangle(r.Right - 42, r.Y + 2, 40, 40), 3);
            _font.DrawShadowed(sb, ItemDef.Get(it).Name.ToUpperInvariant() + (extra != null ? "  " + extra : ""), new Vector2(r.X + 6, r.Y + 22), Color.White, 1.5f);
        }
        else _font.DrawShadowed(sb, "NONE", new Vector2(r.X + 6, r.Y + 22), Color.DimGray, 1.5f);
    }

    private void DrawDetails(SpriteBatch sb, Player p)
    {
        int x = _detailsArea.X + 10, y = _detailsArea.Y + 8;
        ItemStack stack = default; string title = "", sub = "";
        WeaponDef? wdef = null;
        switch (_hoverRegion)
        {
            case Region.Bag: stack = p.Inventory[_hoverIndex]; break;
            case Region.Container: if (Container != null) stack = Container.Items[_hoverIndex]; break;
            case Region.Weapon: if (_hoverIndex < p.Weapons.Count) { wdef = p.Weapons[_hoverIndex].Def; title = wdef.Name; sub = wdef.Description; } break;
            case Region.Attach: if (_hoverIndex < p.Weapons.Count && p.Weapons[_hoverIndex].Attachments.TryGetValue(SlotOrder[_hoverSub], out var at)) stack = new ItemStack(at, 1); break;
            case Region.InspectAttach: if (_inspectWeapon >= 0 && _inspectWeapon < p.Weapons.Count && p.Weapons[_inspectWeapon].Attachments.TryGetValue(SlotOrder[_hoverSub], out var iat)) stack = new ItemStack(iat, 1); break;
            case Region.Helmet: if (p.Helmet is { } hm) stack = new ItemStack(hm, 1); break;
            case Region.Vest: if (p.Vest is { } vs) stack = new ItemStack(vs, 1); break;
        }
        if (!stack.IsEmpty)
        {
            title = $"{stack.Def.Name}  x{stack.Count}"; sub = stack.Def.Description;
            if (stack.Def.IsWeapon) wdef = WeaponDef.ForGunItem(stack.Type);
        }
        if (title.Length == 0)
        {
            _font.DrawShadowed(sb, "HOVER AN ITEM FOR DETAILS.   RMB = INSPECT", new Vector2(x, y), Color.Gray, 1.5f);
            _font.DrawShadowed(sb, "BAG: LMB USE / EQUIP   DRAG = MOVE / SWAP   DRAG OUT = DROP" + (Container != null ? "   CTRL+LMB STASH" : ""), new Vector2(x, y + 22), Color.DimGray, 1.5f);
            _font.DrawShadowed(sb, "WEAPONS: LMB SELECT   DRAG = REORDER / GUN ONTO SLOT = REPLACE   (MAX 3)", new Vector2(x, y + 40), Color.DimGray, 1.5f);
            if (Container != null) _font.DrawShadowed(sb, "CONTAINER: LMB TAKE   [F] TAKE ALL   DRAG EITHER WAY", new Vector2(x, y + 58), Color.DimGray, 1.5f);
            return;
        }
        _font.DrawShadowed(sb, title.ToUpperInvariant(), new Vector2(x, y), Color.White, 2f);
        _font.DrawShadowed(sb, sub, new Vector2(x, y + 20), Color.LightGray, 1.5f);
        if (wdef != null)
        {
            int rpm = (int)MathF.Round(60f / wdef.FireInterval);
            string stats = wdef.IsMelee ? $"DMG {wdef.Damage:0}   SWINGS {rpm}/MIN   REACH {wdef.Range:0}   SILENT"
                : $"DMG {wdef.Damage:0}{(wdef.Pellets > 1 ? $"x{wdef.Pellets}" : "")}   RPM {rpm}   MAG {wdef.MagSize}   RANGE {wdef.Range:0}   {(wdef.Automatic ? "AUTO" : "SEMI")}   RICOCHET {wdef.MaxRicochets}";
            _font.DrawShadowed(sb, stats, new Vector2(x, y + 40), UiDraw.Accent, 1.5f);
            if (wdef.Mag is { } mag) _font.DrawShadowed(sb, $"USES {ItemDef.Get(mag).Name.ToUpperInvariant()}   ({p.Inventory.CountOf(mag)} in bag)", new Vector2(x, y + 58), Color.LightGray, 1.5f);
        }
        else if (!stack.IsEmpty && stack.Def.IsAttachment && AttachmentDef.For(stack.Type) is { } ad)
        {
            _font.DrawShadowed(sb, $"SLOT {ad.Slot.ToString().ToUpperInvariant()}   SPREAD x{ad.SpreadMul:0.00}  RECOIL x{ad.RecoilMul:0.00}  FLASH x{ad.FlashMul:0.00}  RANGE +{ad.RangeAdd:0}", new Vector2(x, y + 40), UiDraw.Accent, 1.5f);
            _font.DrawShadowed(sb, _hoverRegion == Region.Bag ? $"LMB FIT TO {p.CurrentWeapon.Def.ShortName}   DRAG ONTO A WEAPON" : _hoverRegion is Region.Attach or Region.InspectAttach ? "LMB REMOVE TO BAG" : "LMB TAKE", new Vector2(x, y + 58), Color.Gray, 1.5f);
        }
        else if (!stack.IsEmpty && stack.Def.IsGear && GearDef.For(stack.Type) is { } gd)
        {
            _font.DrawShadowed(sb, gd.Slot == GearSlot.Vest ? $"ARMOR {gd.MaxArmor:0}   ABSORBS {gd.Absorb * 100:0}%   SPEED x{gd.SpeedMul:0.00}" : $"DAMAGE TAKEN -{gd.DamageReduction * 100:0}%", new Vector2(x, y + 40), UiDraw.Accent, 1.5f);
            _font.DrawShadowed(sb, _hoverRegion == Region.Bag ? "LMB WEAR   RMB DROP" : _hoverRegion == Region.Container ? "LMB TAKE" : "LMB REMOVE TO BAG", new Vector2(x, y + 58), Color.Gray, 1.5f);
        }
        else if (!stack.IsEmpty)
        {
            string hint = _hoverRegion == Region.Container ? "LMB TAKE" : stack.Type == ItemType.Grenade ? "LMB THROW [G]   DRAG OUT = DROP" : stack.Def.Usable ? "LMB USE   DRAG OUT = DROP" : "DRAG OUT = DROP";
            _font.DrawShadowed(sb, hint, new Vector2(x, y + 40), Color.Gray, 1.5f);
        }
    }

    private void DrawSlot(SpriteBatch sb, Rectangle r, ItemStack stack, string? key, bool hover)
    {
        UiDraw.PanelBox(sb, _px, r, new Color(18, 20, 24, 220), UiDraw.PanelLight);
        if (hover) UiDraw.Fill(sb, _px, r, UiDraw.Hover);
        if (!stack.IsEmpty)
        {
            UiDraw.Icon(sb, _icons[stack.Type].Albedo, r, 7);
            string count = stack.Count.ToString();
            if (stack.Count > 1 || stack.Def.MaxStack > 1)
                _font.DrawShadowed(sb, count, new Vector2(r.Right - _font.Measure(count, 1.5f).X - 4, r.Bottom - 13), Color.White, 1.5f);
            if (stack.Def.IsWeapon) UiDraw.Fill(sb, _px, new Rectangle(r.X + 2, r.Bottom - 4, r.Width - 4, 2), new Color(120, 200, 255));   // weapon marker
            else if (stack.Def.IsAttachment) UiDraw.Fill(sb, _px, new Rectangle(r.X + 2, r.Bottom - 4, r.Width - 4, 2), new Color(255, 200, 110));
            else if (stack.Def.IsGear) UiDraw.Fill(sb, _px, new Rectangle(r.X + 2, r.Bottom - 4, r.Width - 4, 2), new Color(140, 220, 140));
        }
        if (key != null) _font.DrawShadowed(sb, key, new Vector2(r.X + 4, r.Y + 3), UiDraw.Accent, 1.5f);
    }
}
