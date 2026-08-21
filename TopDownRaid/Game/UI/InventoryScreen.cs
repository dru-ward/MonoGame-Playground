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
///   LMB bag item = use / equip · RMB bag item = drop · Ctrl+LMB = stash into the open container
///   LMB container item = take · F = take all · LMB weapon slot = select · RMB weapon slot = unequip into bag
///   Tab / I / Esc = close.
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
    private enum Region { None, Bag, Container, Weapon, Attach, Helmet, Vest, TakeAll, Close }
    private Region _hoverRegion; private int _hoverIndex = -1, _hoverSub = -1;
    private const int Mini = 18;
    private readonly Rectangle[,] _attachSlots = new Rectangle[Player.MaxWeapons, 4];
    private Rectangle _helmetSlot, _vestSlot;
    private static readonly AttachSlot[] SlotOrder = { AttachSlot.Optic, AttachSlot.Muzzle, AttachSlot.Tactical, AttachSlot.Grip };
    private string _status = ""; private float _statusTimer;

    public InventoryScreen(PixelFont font, Texture2D pixel, Dictionary<ItemType, SpritePair> icons) { _font = font; _px = pixel; _icons = icons; }

    public void Open(Player p) { IsOpen = true; p.InventoryOpen = true; }
    public void OpenWith(Player p, LootSource src) { Container = src; Open(p); }
    public void Close(Player p) { IsOpen = false; p.InventoryOpen = false; Container = null; p.CloseLoot(); }
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
        if (input.Pressed(Keys.Tab) || input.Pressed(Keys.I) || input.Pressed(Keys.Escape)) { Close(p); return; }
        if (!p.IsAlive) { Close(p); return; }
        if (Container != null && p.OpenLoot == null) Container = null;        // player walked away → container closed itself

        Layout(screenW, screenH);
        var m = input.MouseScreen;
        _hoverRegion = Region.None; _hoverIndex = -1; _hoverSub = -1;
        for (int i = 0; i < _weaponSlots.Length; i++) if (_weaponSlots[i].Contains(m)) { _hoverRegion = Region.Weapon; _hoverIndex = i; }
        for (int i = 0; i < Player.MaxWeapons; i++) for (int a = 0; a < 4; a++) if (_attachSlots[i, a].Contains(m)) { _hoverRegion = Region.Attach; _hoverIndex = i; _hoverSub = a; }
        if (_helmetSlot.Contains(m)) _hoverRegion = Region.Helmet; if (_vestSlot.Contains(m)) _hoverRegion = Region.Vest;
        for (int i = 0; i < _bagSlots.Length; i++) if (_bagSlots[i].Contains(m)) { _hoverRegion = Region.Bag; _hoverIndex = i; }
        if (Container != null) for (int i = 0; i < _containerSlots.Length; i++) if (_containerSlots[i].Contains(m)) { _hoverRegion = Region.Container; _hoverIndex = i; }
        if (Container != null && _takeAllBtn.Contains(m)) _hoverRegion = Region.TakeAll;
        if (_closeBtn.Contains(m)) _hoverRegion = Region.Close;

        bool ctrl = input.AnyDown(Keys.LeftControl, Keys.RightControl);
        if (input.LeftPressed)
        {
            switch (_hoverRegion)
            {
                case Region.Bag:
                    if (Container != null && ctrl) Stash(p, _hoverIndex);
                    else if (_hoverIndex < p.Inventory.Count && !p.Inventory[_hoverIndex].IsEmpty) p.UseSlot(_hoverIndex, ctx);
                    break;
                case Region.Container: Take(p, _hoverIndex, all: false); break;
                case Region.Weapon: if (_hoverIndex < p.Weapons.Count) p.SelectWeapon(_hoverIndex); break;
                case Region.Attach: if (_hoverIndex < p.Weapons.Count && p.DetachToBag(_hoverIndex, SlotOrder[_hoverSub])) Status("Attachment removed"); break;
                case Region.Helmet: if (p.Unwear(GearSlot.Helmet)) Status("Helmet removed"); break;
                case Region.Vest: if (p.Unwear(GearSlot.Vest)) Status("Vest removed"); break;
                case Region.TakeAll: TakeAll(p); break;
                case Region.Close: Close(p); return;
            }
        }
        if (input.RightPressed)
        {
            switch (_hoverRegion)
            {
                case Region.Bag: p.DropSlot(_hoverIndex, ctx); break;
                case Region.Weapon: if (_hoverIndex < p.Weapons.Count && p.Unequip(_hoverIndex)) Status("Unequipped"); break;
            }
        }
        if (Container != null && input.Pressed(Keys.F)) TakeAll(p);
        // number keys still use hotbar slots while the screen is open
        for (int i = 0; i < Inventory.HotbarSize; i++) if (input.Pressed(Keys.D1 + i)) p.UseSlot(i, ctx);
    }

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
        _font.DrawShadowed(sb, "WEAPONS   [Q] CYCLE | LMB SELECT | RMB UNEQUIP", new Vector2(_weaponsArea.X, _weaponsArea.Y - 14), Color.Gray, 1.5f);
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
        _font.DrawShadowed(sb, "BACKPACK   (1-5 = HOTBAR)", new Vector2(_bagArea.X, _bagArea.Y - 14), Color.Gray, 1.5f);
        for (int i = 0; i < Inventory.DefaultSlots; i++)
            DrawSlot(sb, _bagSlots[i], p.Inventory[i], i < Inventory.HotbarSize ? (i + 1).ToString() : null, _hoverRegion == Region.Bag && _hoverIndex == i);

        // ---- container ------------------------------------------------------------------------------------
        if (Container != null)
        {
            UiDraw.PanelBox(sb, _px, _containerArea, new Color(20, 14, 10, 200), new Color(200, 160, 100, 120));
            _font.DrawShadowed(sb, Container.Title, new Vector2(_containerArea.X + 8, _containerArea.Y + 9), UiDraw.Accent, 1.5f);
            _font.DrawShadowed(sb, "LMB TAKE", new Vector2(_containerArea.X + 8, _containerArea.Bottom - 22), Color.Gray, 1.5f);
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
            _font.DrawShadowed(sb, "HOVER AN ITEM FOR DETAILS.", new Vector2(x, y), Color.Gray, 1.5f);
            _font.DrawShadowed(sb, "BAG: LMB USE / EQUIP   RMB DROP" + (Container != null ? "   CTRL+LMB STASH IN CONTAINER" : ""), new Vector2(x, y + 22), Color.DimGray, 1.5f);
            _font.DrawShadowed(sb, "WEAPONS: LMB SELECT   RMB UNEQUIP TO BAG   (MAX 3 CARRIED)", new Vector2(x, y + 40), Color.DimGray, 1.5f);
            if (Container != null) _font.DrawShadowed(sb, "CONTAINER: LMB TAKE   [F] TAKE ALL", new Vector2(x, y + 58), Color.DimGray, 1.5f);
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
            _font.DrawShadowed(sb, _hoverRegion == Region.Bag ? $"LMB FIT TO {p.CurrentWeapon.Def.ShortName}   RMB DROP" : _hoverRegion == Region.Attach ? "LMB REMOVE TO BAG" : "LMB TAKE", new Vector2(x, y + 58), Color.Gray, 1.5f);
        }
        else if (!stack.IsEmpty && stack.Def.IsGear && GearDef.For(stack.Type) is { } gd)
        {
            _font.DrawShadowed(sb, gd.Slot == GearSlot.Vest ? $"ARMOR {gd.MaxArmor:0}   ABSORBS {gd.Absorb * 100:0}%   SPEED x{gd.SpeedMul:0.00}" : $"DAMAGE TAKEN -{gd.DamageReduction * 100:0}%", new Vector2(x, y + 40), UiDraw.Accent, 1.5f);
            _font.DrawShadowed(sb, _hoverRegion == Region.Bag ? "LMB WEAR   RMB DROP" : _hoverRegion == Region.Container ? "LMB TAKE" : "LMB REMOVE TO BAG", new Vector2(x, y + 58), Color.Gray, 1.5f);
        }
        else if (!stack.IsEmpty)
        {
            string hint = _hoverRegion == Region.Container ? "LMB TAKE" : stack.Type == ItemType.Grenade ? "LMB THROW [G]   RMB DROP" : stack.Def.Usable ? "LMB USE   RMB DROP" : "RMB DROP";
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
