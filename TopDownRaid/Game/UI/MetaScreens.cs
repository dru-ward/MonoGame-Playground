using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Game.Combat;
using Game.Core;
using Game.Graphics;
using Game.Items;
using Game.Meta;
using Game.World;

namespace Game.UI;

/// <summary>Shared plumbing for the out-of-raid screens: font, pixel, icons, a dim tiled background.</summary>
public abstract class MetaScreen
{
    protected readonly PixelFont Font; protected readonly Texture2D Px; protected readonly Dictionary<ItemType, SpritePair> Icons; protected readonly Texture2D Background;
    protected MetaScreen(PixelFont font, Texture2D px, Dictionary<ItemType, SpritePair> icons, Texture2D background) { Font = font; Px = px; Icons = icons; Background = background; }

    /// <summary>Dark asphalt backdrop + vignette so the screens share the game's mood.</summary>
    protected void DrawBackdrop(SpriteBatch sb, int w, int h, float time)
    {
        // tile the 512 px asphalt by hand (the batch uses PointClamp so the pixel font stays crisp)
        int off = (int)(time * 6f) % Background.Width;
        for (int y = 0; y < h; y += Background.Height)
        for (int x = -off; x < w; x += Background.Width)
            sb.Draw(Background, new Vector2(x, y), new Color(34, 36, 40));
        UiDraw.Fill(sb, Px, new Rectangle(0, 0, w, 80), new Color(0, 0, 0, 140)); UiDraw.Fill(sb, Px, new Rectangle(0, h - 60, w, 60), new Color(0, 0, 0, 140));
    }
    protected static bool Hover(Rectangle r, Vector2 m) => r.Contains(m);

    /// <summary>Word-wrapped text; returns the y below the last line.</summary>
    protected float DrawWrapped(SpriteBatch sb, string text, Vector2 pos, int width, Color c, float scale)
    {
        var words = text.Split(' '); string line = ""; float y = pos.Y; float lineH = 7f * scale + 4f;
        foreach (var wd in words)
        {
            string test = line.Length == 0 ? wd : line + " " + wd;
            if (Font.Measure(test, scale).X > width && line.Length > 0) { Font.DrawShadowed(sb, line, new Vector2(pos.X, y), c, scale); y += lineH; line = wd; }
            else line = test;
        }
        if (line.Length > 0) { Font.DrawShadowed(sb, line, new Vector2(pos.X, y), c, scale); y += lineH; }
        return y;
    }
}

// =================================================================================================== menu
public sealed class MenuScreen : MetaScreen
{
    public enum Action { None, Continue, Quit }
    private Rectangle _play, _quit;
    public MenuScreen(PixelFont f, Texture2D px, Dictionary<ItemType, SpritePair> icons, Texture2D bg) : base(f, px, icons, bg) { }

    public Action Update(InputState input, int w, int h)
    {
        _play = new Rectangle(w / 2 - 140, h / 2 + 20, 280, 44); _quit = new Rectangle(w / 2 - 140, h / 2 + 76, 280, 44);
        if (input.Pressed(Keys.Enter) || input.Pressed(Keys.Space)) return Action.Continue;
        if (input.Pressed(Keys.Escape)) return Action.Quit;
        if (input.LeftPressed) { if (Hover(_play, input.MouseScreen)) return Action.Continue; if (Hover(_quit, input.MouseScreen)) return Action.Quit; }
        return Action.None;
    }

    public void Draw(SpriteBatch sb, Profile profile, Vector2 mouse, int w, int h, float time)
    {
        DrawBackdrop(sb, w, h, time);
        UiDraw.TextCentered(sb, Font, "TOPDOWN RAID", new Rectangle(0, h / 2 - 150, w, 40), Color.White, 6f);
        UiDraw.TextCentered(sb, Font, "GET IN. LOOT. GET OUT.", new Rectangle(0, h / 2 - 100, w, 20), UiDraw.Accent, 2f);
        UiDraw.TextCentered(sb, Font, $"RAIDS {profile.Stats.Raids}   EXTRACTED {profile.Stats.Extracts}   KIA {profile.Stats.Deaths}   KILLS {profile.Stats.Kills}   GOLD {profile.Gold}",
            new Rectangle(0, h / 2 - 60, w, 20), Color.LightGray, 1.5f);
        UiDraw.Button(sb, Px, Font, _play, "ENTER STASH  [ENTER]", Hover(_play, mouse));
        UiDraw.Button(sb, Px, Font, _quit, "QUIT  [ESC]", Hover(_quit, mouse));
        UiDraw.TextCentered(sb, Font, "WASD MOVE   MOUSE AIM   LMB FIRE   R RELOAD   Q SWAP   E SEARCH/OPEN   TAB INVENTORY   FIND AN EXTRACT AND HOLD POSITION", new Rectangle(0, h - 40, w, 20), Color.Gray, 1.5f);
    }
}

// =================================================================================================== stash / loadout
public sealed class StashScreen : MetaScreen
{
    public enum Action { None, Deploy, ChooseMap, Back }
    private const int Slot = 56, Gap = 6, StashCols = 8, BagCols = 5, Mini = 18;
    private readonly Rectangle[] _stashSlots = new Rectangle[Profile.StashSlots];
    private readonly Rectangle[] _bagSlots = new Rectangle[Inventory.DefaultSlots];
    private readonly Rectangle[] _weaponSlots = new Rectangle[3];
    private readonly Rectangle[,] _attachSlots = new Rectangle[3, 4];
    private Rectangle _helmetSlot, _vestSlot, _deploy, _map, _stashAll, _back, _details;
    private enum Region { None, Stash, Bag, Weapon, Attach, Helmet, Vest }
    private Region _hover; private int _hoverIdx = -1, _hoverSub = -1;
    private int _selectedWeapon;
    private string _status = ""; private float _statusTimer;
    private static readonly AttachSlot[] SlotOrder = { AttachSlot.Optic, AttachSlot.Muzzle, AttachSlot.Tactical, AttachSlot.Grip };

    public StashScreen(PixelFont f, Texture2D px, Dictionary<ItemType, SpritePair> icons, Texture2D bg) : base(f, px, icons, bg) { }

    private void Layout(int w, int h)
    {
        int stashW = StashCols * Slot + (StashCols - 1) * Gap, bagW = BagCols * Slot + (BagCols - 1) * Gap;
        int total = stashW + 40 + bagW; int x0 = (w - total) / 2, y0 = 100;
        for (int i = 0; i < Profile.StashSlots; i++) _stashSlots[i] = new Rectangle(x0 + (i % StashCols) * (Slot + Gap), y0 + 24 + (i / StashCols) * (Slot + Gap), Slot, Slot);
        int bx = x0 + stashW + 40;
        int wslotW = (bagW - 2 * Gap) / 3;
        for (int i = 0; i < 3; i++)
        {
            _weaponSlots[i] = new Rectangle(bx + i * (wslotW + Gap), y0 + 24, wslotW, 62);
            for (int a = 0; a < 4; a++) _attachSlots[i, a] = new Rectangle(_weaponSlots[i].X + a * (Mini + 3), _weaponSlots[i].Bottom + 3, Mini, Mini);
        }
        int gy = y0 + 24 + 62 + 3 + Mini + 22;
        int gw = (bagW - Gap) / 2;
        _helmetSlot = new Rectangle(bx, gy, gw, 44); _vestSlot = new Rectangle(bx + gw + Gap, gy, gw, 44);
        int bagY = gy + 44 + 22;
        for (int i = 0; i < Inventory.DefaultSlots; i++) _bagSlots[i] = new Rectangle(bx + (i % BagCols) * (Slot + Gap), bagY + (i / BagCols) * (Slot + Gap), Slot, Slot);
        int by = bagY + 3 * (Slot + Gap) + 4;
        _stashAll = new Rectangle(bx, by, bagW, 28);
        _map = new Rectangle(bx, by + 34, bagW, 32);
        _deploy = new Rectangle(bx, by + 72, bagW, 42);
        _back = new Rectangle(20, h - 50, 140, 34);
        _details = new Rectangle(x0, y0 + 24 + 6 * (Slot + Gap) + 8, stashW, 110);
    }

    public Action Update(InputState input, Profile profile, int w, int h)
    {
        Layout(w, h);
        _statusTimer = MathF.Max(0f, _statusTimer - 1f / 60f);
        _selectedWeapon = Math.Clamp(_selectedWeapon, 0, Math.Max(0, profile.Loadout.Weapons.Count - 1));
        var m = input.MouseScreen; _hover = Region.None; _hoverIdx = -1; _hoverSub = -1;
        for (int i = 0; i < _stashSlots.Length; i++) if (Hover(_stashSlots[i], m)) { _hover = Region.Stash; _hoverIdx = i; }
        for (int i = 0; i < _bagSlots.Length; i++) if (Hover(_bagSlots[i], m)) { _hover = Region.Bag; _hoverIdx = i; }
        for (int i = 0; i < _weaponSlots.Length; i++) if (Hover(_weaponSlots[i], m)) { _hover = Region.Weapon; _hoverIdx = i; }
        for (int i = 0; i < 3; i++) for (int a = 0; a < 4; a++) if (Hover(_attachSlots[i, a], m)) { _hover = Region.Attach; _hoverIdx = i; _hoverSub = a; }
        if (Hover(_helmetSlot, m)) _hover = Region.Helmet; if (Hover(_vestSlot, m)) _hover = Region.Vest;
        var lo = profile.Loadout;

        if (input.LeftPressed)
        {
            switch (_hover)
            {
                case Region.Stash: if (!profile.MoveToLoadout(_hoverIdx)) Status("No room in the loadout"); break;
                case Region.Bag:
                {
                    var s = lo.Bag[_hoverIdx];
                    if (s.IsEmpty) break;
                    if (s.Def.IsAttachment) { if (lo.AttachFromBag(_hoverIdx, _selectedWeapon)) Status($"Fitted {s.Def.Name} to weapon {_selectedWeapon + 1}"); else Status("Does not fit the selected weapon"); }
                    else if (s.Def.IsGear) { if (lo.WearFromBag(_hoverIdx)) Status($"Wearing {s.Def.Name}"); }
                    else profile.MoveBagToStash(_hoverIdx);
                    break;
                }
                case Region.Weapon: if (_hoverIdx < lo.Weapons.Count) _selectedWeapon = _hoverIdx; break;
                case Region.Attach: if (_hoverIdx < lo.Weapons.Count && lo.DetachToBag(_hoverIdx, SlotOrder[_hoverSub])) Status("Attachment removed to bag"); break;
                case Region.Helmet: if (profile.MoveGearToStash(GearSlot.Helmet)) Status("Helmet to stash"); break;
                case Region.Vest: if (profile.MoveGearToStash(GearSlot.Vest)) Status("Vest to stash"); break;
            }
            if (Hover(_stashAll, m)) profile.StashAll();
            if (Hover(_map, m)) return Action.ChooseMap;
            if (Hover(_deploy, m)) return Action.Deploy;
            if (Hover(_back, m)) return Action.Back;
        }
        if (input.RightPressed)
        {
            switch (_hover)
            {
                case Region.Bag: profile.MoveBagToStash(_hoverIdx); break;
                case Region.Weapon: if (!profile.MoveWeaponToStash(_hoverIdx)) Status("Stash full"); break;
                case Region.Helmet: lo.UnwearToBag(GearSlot.Helmet); break;
                case Region.Vest: lo.UnwearToBag(GearSlot.Vest); break;
            }
        }
        if (input.Pressed(Keys.Enter)) return Action.Deploy;
        if (input.Pressed(Keys.M)) return Action.ChooseMap;
        if (input.Pressed(Keys.Escape)) return Action.Back;
        return Action.None;
    }
    private void Status(string s) { _status = s; _statusTimer = 2f; }

    public void Draw(SpriteBatch sb, Profile profile, Vector2 mouse, int w, int h, float time)
    {
        Layout(w, h);
        DrawBackdrop(sb, w, h, time);
        var map = MapDef.ById(profile.SelectedMapId); var lo = profile.Loadout;
        Font.DrawShadowed(sb, "STASH", new Vector2(_stashSlots[0].X, 20), Color.White, 4f);
        Font.DrawShadowed(sb, $"GOLD {profile.Gold}    RAIDS {profile.Stats.Raids}   SURVIVAL {profile.Stats.SurvivalRate * 100:0}%   KILLS {profile.Stats.Kills}", new Vector2(_stashSlots[0].X, 58), UiDraw.Accent, 1.5f);

        // stash
        Font.DrawShadowed(sb, $"STASH   {CountUsed(profile.Stash)}/{profile.Stash.Count}   LMB = MOVE TO LOADOUT", new Vector2(_stashSlots[0].X, _stashSlots[0].Y - 16), Color.Gray, 1.5f);
        for (int i = 0; i < _stashSlots.Length; i++) DrawSlot(sb, _stashSlots[i], profile.Stash[i], null, _hover == Region.Stash && _hoverIdx == i);

        // loadout: weapons + attachment minis
        Font.DrawShadowed(sb, "WEAPONS   LMB SELECT   RMB TO STASH", new Vector2(_weaponSlots[0].X, _weaponSlots[0].Y - 16), Color.Gray, 1.5f);
        for (int i = 0; i < 3; i++)
        {
            var r = _weaponSlots[i]; bool has = i < lo.Weapons.Count, sel = has && i == _selectedWeapon;
            UiDraw.PanelBox(sb, Px, r, sel ? new Color(30, 45, 60, 230) : new Color(18, 20, 24, 220), sel ? new Color(120, 200, 255) : UiDraw.PanelLight);
            if (_hover == Region.Weapon && _hoverIdx == i) UiDraw.Fill(sb, Px, r, UiDraw.Hover);
            if (!has) { UiDraw.TextCentered(sb, Font, "EMPTY", r, Color.DimGray, 1.5f); continue; }
            var wl = lo.Weapons[i]; var def = wl.Def;
            Font.DrawShadowed(sb, def.ShortName, new Vector2(r.X + 6, r.Y + 5), Color.White, 1.5f);
            UiDraw.Icon(sb, Icons[wl.Gun].Albedo, new Rectangle(r.X + 2, r.Y + 18, 42, 42), 3);
            if (def.Mag is { } mg)
            {
                int mags = lo.Bag.CountOf(mg);
                Font.DrawShadowed(sb, $"{mags} MAG", new Vector2(r.X + 46, r.Y + 30), mags == 0 ? new Color(255, 110, 90) : UiDraw.Accent, 1.5f);
            }
            else Font.DrawShadowed(sb, "MELEE", new Vector2(r.X + 46, r.Y + 30), UiDraw.Accent, 1.5f);
            for (int a = 0; a < 4; a++)
            {
                var ar = _attachSlots[i, a]; var slot = SlotOrder[a];
                bool allowed = AttachPoints.Allows(def.Held, slot);
                UiDraw.PanelBox(sb, Px, ar, allowed ? new Color(18, 20, 24, 220) : new Color(10, 10, 12, 160), allowed ? UiDraw.PanelLight : new Color(255, 255, 255, 12));
                if (wl.Attachments.TryGetValue(slot, out var at)) UiDraw.Icon(sb, Icons[at].Albedo, ar, 2);
                else if (allowed) UiDraw.TextCentered(sb, Font, slot.ToString().Substring(0, 1), ar, Color.DimGray, 1f);
                if (_hover == Region.Attach && _hoverIdx == i && _hoverSub == a) UiDraw.Fill(sb, Px, ar, UiDraw.Hover);
            }
        }
        // gear
        Font.DrawShadowed(sb, "GEAR   LMB TO STASH   RMB TO BAG", new Vector2(_helmetSlot.X, _helmetSlot.Y - 16), Color.Gray, 1.5f);
        DrawGear(sb, _helmetSlot, "HELMET", lo.Helmet, _hover == Region.Helmet);
        DrawGear(sb, _vestSlot, "VEST", lo.Vest, _hover == Region.Vest);
        // bag
        Font.DrawShadowed(sb, "BAG   LMB FIT/WEAR/STASH   RMB STASH", new Vector2(_bagSlots[0].X, _bagSlots[0].Y - 16), Color.Gray, 1.5f);
        for (int i = 0; i < _bagSlots.Length; i++) DrawSlot(sb, _bagSlots[i], lo.Bag[i], i < Inventory.HotbarSize ? (i + 1).ToString() : null, _hover == Region.Bag && _hoverIdx == i);

        UiDraw.Button(sb, Px, Font, _stashAll, "STASH EVERYTHING", Hover(_stashAll, mouse), true, null, 1.5f);
        UiDraw.Button(sb, Px, Font, _map, $"MAP: {map.Name} ({map.Difficulty})   [M] CHANGE", Hover(_map, mouse), true, null, 1.5f);
        UiDraw.Button(sb, Px, Font, _deploy, "DEPLOY  [ENTER]", Hover(_deploy, mouse), true, new Color(40, 70, 40, 235));
        if (!lo.HasWeapon) Font.DrawShadowed(sb, "NO GUN IN LOADOUT - A FREE PISTOL WILL BE ISSUED", new Vector2(_deploy.X, _deploy.Bottom + 6), new Color(255, 200, 110), 1.5f);
        UiDraw.Button(sb, Px, Font, _back, "[ESC] MENU", Hover(_back, mouse), true, null, 1.5f);

        UiDraw.PanelBox(sb, Px, _details, new Color(14, 16, 20, 220));
        DrawDetails(sb, profile);
        if (_statusTimer > 0f) Font.DrawShadowed(sb, _status, new Vector2(_details.Right - Font.Measure(_status, 1.5f).X - 8, _details.Bottom - 16), UiDraw.Accent, 1.5f);
    }

    private void DrawGear(SpriteBatch sb, Rectangle r, string label, ItemType? item, bool hover)
    {
        UiDraw.PanelBox(sb, Px, r, new Color(18, 20, 24, 220), UiDraw.PanelLight);
        if (hover) UiDraw.Fill(sb, Px, r, UiDraw.Hover);
        Font.DrawShadowed(sb, label, new Vector2(r.X + 6, r.Y + 4), Color.Gray, 1.5f);
        if (item is { } it)
        {
            UiDraw.Icon(sb, Icons[it].Albedo, new Rectangle(r.Right - 42, r.Y + 2, 40, 40), 3);
            Font.DrawShadowed(sb, ItemDef.Get(it).Name.ToUpperInvariant(), new Vector2(r.X + 6, r.Y + 22), Color.White, 1.5f);
        }
        else Font.DrawShadowed(sb, "NONE", new Vector2(r.X + 6, r.Y + 22), Color.DimGray, 1.5f);
    }

    private void DrawDetails(SpriteBatch sb, Profile profile)
    {
        int x = _details.X + 10, y = _details.Y + 8; var lo = profile.Loadout;
        ItemStack s = default; WeaponDef? wdef = null; ItemType? single = null;
        switch (_hover)
        {
            case Region.Stash: s = profile.Stash[_hoverIdx]; break;
            case Region.Bag: s = lo.Bag[_hoverIdx]; break;
            case Region.Weapon: if (_hoverIdx < lo.Weapons.Count) wdef = lo.Weapons[_hoverIdx].Def; break;
            case Region.Attach: if (_hoverIdx < lo.Weapons.Count && lo.Weapons[_hoverIdx].Attachments.TryGetValue(SlotOrder[_hoverSub], out var at)) single = at; break;
            case Region.Helmet: single = lo.Helmet; break;
            case Region.Vest: single = lo.Vest; break;
        }
        if (single is { } si) s = new ItemStack(si, 1);
        if (!s.IsEmpty && s.Def.IsWeapon) wdef = WeaponDef.ForGunItem(s.Type);
        if (s.IsEmpty && wdef == null)
        {
            float yy = DrawWrapped(sb, "CLICK STASH ITEMS TO TAKE THEM. IN THE BAG: LMB FITS ATTACHMENTS TO THE SELECTED WEAPON AND WEARS GEAR - RMB SENDS ITEMS BACK.", new Vector2(x, y), _details.Width - 20, Color.Gray, 1.5f);
            DrawWrapped(sb, "EXTRACT TO KEEP WHAT YOU CARRY. DIE AND IT IS GONE. TIP: MAGS FOR EVERY GUN, A VEST, A MEDKIT, A GRENADE.", new Vector2(x, yy), _details.Width - 20, Color.DimGray, 1.5f);
            return;
        }
        string title = wdef != null ? wdef.Name : s.Def.Name; string sub = wdef != null ? wdef.Description : s.Def.Description;
        Font.DrawShadowed(sb, (s.IsEmpty || single != null ? title : $"{title}  x{s.Count}").ToUpperInvariant(), new Vector2(x, y), Color.White, 2f);
        Font.DrawShadowed(sb, sub, new Vector2(x, y + 20), Color.LightGray, 1.5f);
        if (wdef != null)
        {
            if (wdef.IsMelee) Font.DrawShadowed(sb, $"DMG {wdef.Damage:0}  SWING {60f / wdef.FireInterval:0}/MIN  REACH {wdef.Range:0}  SILENT", new Vector2(x, y + 40), UiDraw.Accent, 1.5f);
            else
            {
                int rpm = (int)MathF.Round(60f / wdef.FireInterval);
                Font.DrawShadowed(sb, $"DMG {wdef.Damage:0}{(wdef.Pellets > 1 ? $"x{wdef.Pellets}" : "")}  RPM {rpm}  MAG {wdef.MagSize}  RANGE {wdef.Range:0}  {(wdef.Automatic ? "AUTO" : "SEMI")}", new Vector2(x, y + 40), UiDraw.Accent, 1.5f);
                if (wdef.Mag is { } mag) Font.DrawShadowed(sb, $"USES {ItemDef.Get(mag).Name.ToUpperInvariant()}  STASH {profile.Stash.CountOf(mag)}  LOADOUT {lo.Bag.CountOf(mag)}", new Vector2(x, y + 58), Color.LightGray, 1.5f);
                string slots = ""; foreach (var sl in SlotOrder) if (AttachPoints.Allows(wdef.Held, sl)) slots += sl.ToString().ToUpperInvariant() + " ";
                Font.DrawShadowed(sb, $"ATTACHMENT SLOTS: {slots}", new Vector2(x, y + 76), Color.Gray, 1.5f);
            }
        }
        else if (!s.IsEmpty && s.Def.IsAttachment && AttachmentDef.For(s.Type) is { } ad)
        {
            Font.DrawShadowed(sb, $"SLOT {ad.Slot.ToString().ToUpperInvariant()}   SPREAD x{ad.SpreadMul:0.00}  RECOIL x{ad.RecoilMul:0.00}  FLASH x{ad.FlashMul:0.00}  RANGE +{ad.RangeAdd:0}", new Vector2(x, y + 40), UiDraw.Accent, 1.5f);
        }
        else if (!s.IsEmpty && s.Def.IsGear && GearDef.For(s.Type) is { } gd)
        {
            Font.DrawShadowed(sb, gd.Slot == GearSlot.Vest ? $"ARMOR {gd.MaxArmor:0}   ABSORBS {gd.Absorb * 100:0}%   SPEED x{gd.SpeedMul:0.00}" : $"DAMAGE TAKEN -{gd.DamageReduction * 100:0}%", new Vector2(x, y + 40), UiDraw.Accent, 1.5f);
        }
    }

    private static int CountUsed(Inventory inv) { int n = 0; for (int i = 0; i < inv.Count; i++) if (!inv[i].IsEmpty) n++; return n; }

    private void DrawSlot(SpriteBatch sb, Rectangle r, ItemStack stack, string? key, bool hover)
    {
        UiDraw.PanelBox(sb, Px, r, new Color(18, 20, 24, 220), UiDraw.PanelLight);
        if (hover) UiDraw.Fill(sb, Px, r, UiDraw.Hover);
        if (!stack.IsEmpty)
        {
            UiDraw.Icon(sb, Icons[stack.Type].Albedo, r, 7);
            string count = stack.Count.ToString();
            if (stack.Count > 1 || stack.Def.MaxStack > 1) Font.DrawShadowed(sb, count, new Vector2(r.Right - Font.Measure(count, 1.5f).X - 4, r.Bottom - 13), Color.White, 1.5f);
            if (stack.Def.IsWeapon) UiDraw.Fill(sb, Px, new Rectangle(r.X + 2, r.Bottom - 4, r.Width - 4, 2), new Color(120, 200, 255));
            else if (stack.Def.IsAttachment) UiDraw.Fill(sb, Px, new Rectangle(r.X + 2, r.Bottom - 4, r.Width - 4, 2), new Color(255, 200, 110));
            else if (stack.Def.IsGear) UiDraw.Fill(sb, Px, new Rectangle(r.X + 2, r.Bottom - 4, r.Width - 4, 2), new Color(140, 220, 140));
        }
        if (key != null) Font.DrawShadowed(sb, key, new Vector2(r.X + 4, r.Y + 3), UiDraw.Accent, 1.5f);
    }
}

// =================================================================================================== map select
public sealed class MapSelectScreen : MetaScreen
{
    public enum Action { None, Confirm, Back }
    private readonly List<Rectangle> _cards = new();
    private Rectangle _confirm, _back;
    private int _selected;

    public MapSelectScreen(PixelFont f, Texture2D px, Dictionary<ItemType, SpritePair> icons, Texture2D bg) : base(f, px, icons, bg) { }

    private void Layout(int w, int h)
    {
        _cards.Clear();
        int cardW = 300, cardH = 300, gap = 24; int total = MapDef.All.Count * cardW + (MapDef.All.Count - 1) * gap; int x0 = (w - total) / 2, y0 = 120;
        for (int i = 0; i < MapDef.All.Count; i++) _cards.Add(new Rectangle(x0 + i * (cardW + gap), y0, cardW, cardH));
        _confirm = new Rectangle(w / 2 - 210, y0 + cardH + 30, 420, 44);
        _back = new Rectangle(20, h - 50, 140, 34);
    }

    public Action Update(InputState input, Profile profile, int w, int h)
    {
        Layout(w, h);
        for (int i = 0; i < MapDef.All.Count; i++) if (MapDef.All[i].Id == profile.SelectedMapId) _selected = i;
        if (input.Pressed(Keys.Left)) _selected = (_selected + MapDef.All.Count - 1) % MapDef.All.Count;
        if (input.Pressed(Keys.Right)) _selected = (_selected + 1) % MapDef.All.Count;
        if (input.LeftPressed)
        {
            for (int i = 0; i < _cards.Count; i++) if (Hover(_cards[i], input.MouseScreen)) _selected = i;
            if (Hover(_confirm, input.MouseScreen)) { profile.SelectedMapId = MapDef.All[_selected].Id; return Action.Confirm; }
            if (Hover(_back, input.MouseScreen)) return Action.Back;
        }
        profile.SelectedMapId = MapDef.All[_selected].Id;
        if (input.Pressed(Keys.Enter)) return Action.Confirm;
        if (input.Pressed(Keys.Escape)) return Action.Back;
        return Action.None;
    }

    public void Draw(SpriteBatch sb, Profile profile, Vector2 mouse, int w, int h, float time)
    {
        Layout(w, h);
        DrawBackdrop(sb, w, h, time);
        UiDraw.TextCentered(sb, Font, "SELECT MAP", new Rectangle(0, 30, w, 40), Color.White, 4f);
        UiDraw.TextCentered(sb, Font, "LEFT/RIGHT OR CLICK A CARD   ENTER TO CONFIRM", new Rectangle(0, 76, w, 20), Color.Gray, 1.5f);
        for (int i = 0; i < MapDef.All.Count; i++)
        {
            var m = MapDef.All[i]; var r = _cards[i]; bool sel = i == _selected;
            UiDraw.PanelBox(sb, Px, r, sel ? new Color(28, 40, 50, 235) : UiDraw.Panel, sel ? new Color(120, 200, 255) : (Hover(r, mouse) ? Color.White : UiDraw.PanelLight));
            Font.DrawShadowed(sb, m.Name, new Vector2(r.X + 14, r.Y + 12), Color.White, 3f);
            var diffCol = m.Difficulty switch { "EASY" => new Color(120, 220, 120), "MEDIUM" => new Color(240, 200, 90), _ => new Color(240, 90, 80) };
            Font.DrawShadowed(sb, m.Difficulty, new Vector2(r.X + 14, r.Y + 44), diffCol, 2f);
            // mini map: props + extracts drawn to scale
            var mini = new Rectangle(r.X + 14, r.Y + 70, 120, 120);
            UiDraw.PanelBox(sb, Px, mini, new Color(10, 10, 12, 240));
            var preview = MapPreview.Get(m);
            foreach (var pr in preview.Props) UiDraw.Fill(sb, Px, new Rectangle(mini.X + (int)(pr.Min.X * 120), mini.Y + (int)(pr.Min.Y * 120), Math.Max(2, (int)(pr.Size.X * 120)), Math.Max(2, (int)(pr.Size.Y * 120))), new Color(90, 90, 95));
            foreach (var ex in m.Extracts) UiDraw.Fill(sb, Px, new Rectangle(mini.X + (int)(ex.RelX * 120) - 4, mini.Y + (int)(ex.RelY * 120) - 4, 8, 8), new Color(80, 240, 110));
            UiDraw.Fill(sb, Px, new Rectangle(mini.X + 58, mini.Y + 58, 4, 4), new Color(255, 220, 120));
            int tx = mini.Right + 10;
            Font.DrawShadowed(sb, $"SIZE {m.Size}", new Vector2(tx, mini.Y), Color.LightGray, 1.5f);
            Font.DrawShadowed(sb, $"EXITS {m.Extracts.Count}", new Vector2(tx, mini.Y + 16), Color.LightGray, 1.5f);
            Font.DrawShadowed(sb, $"TIMER {m.RaidMinutes:0} MIN", new Vector2(tx, mini.Y + 32), Color.LightGray, 1.5f);
            Font.DrawShadowed(sb, $"ENEMIES {m.MaxAlive}", new Vector2(tx, mini.Y + 48), Color.LightGray, 1.5f);
            Font.DrawShadowed(sb, $"GUNNERS {m.GunnerChance * 100:0}%", new Vector2(tx, mini.Y + 64), Color.LightGray, 1.5f);
            DrawWrapped(sb, m.Description, new Vector2(r.X + 14, r.Y + 200), r.Width - 28, Color.LightGray, 1.5f);
        }
        UiDraw.Button(sb, Px, Font, _confirm, $"DEPLOY TO {MapDef.All[_selected].Name}  [ENTER]", Hover(_confirm, mouse), true, new Color(40, 70, 40, 235));
        UiDraw.Button(sb, Px, Font, _back, "[ESC] BACK", Hover(_back, mouse), true, null, 1.5f);
    }

}

/// <summary>Cheap cached layout preview (relative rectangles) for the map cards.</summary>
public static class MapPreview
{
    public sealed class Data { public List<RectangleF> Props = new(); }
    private static readonly Dictionary<string, Data> Cache = new();
    public static Data Get(MapDef m)
    {
        if (Cache.TryGetValue(m.Id, out var d)) return d;
        var world = new GameWorld(); world.Generate(m, new Vector2(m.Size * 0.5f));
        d = new Data();
        foreach (var c in world.Crates) d.Props.Add(new RectangleF(new Vector2(c.Bounds.X / (float)m.Size, c.Bounds.Y / (float)m.Size), new Vector2(c.Bounds.Width / (float)m.Size, c.Bounds.Height / (float)m.Size)));
        Cache[m.Id] = d; return d;
    }
}

// =================================================================================================== summary
public sealed class SummaryScreen : MetaScreen
{
    public enum Action { None, Continue }
    private Rectangle _continue;
    public RaidOutcome Outcome; public string MapName = ""; public int Kills, Gold; public float Duration; public List<ItemStack> Brought = new(); public List<ItemType> Weapons = new();
    public SummaryScreen(PixelFont f, Texture2D px, Dictionary<ItemType, SpritePair> icons, Texture2D bg) : base(f, px, icons, bg) { }

    public Action Update(InputState input, int w, int h)
    {
        _continue = new Rectangle(w / 2 - 150, h - 110, 300, 44);
        if (input.Pressed(Keys.Enter) || input.Pressed(Keys.Space) || input.Pressed(Keys.Escape)) return Action.Continue;
        if (input.LeftPressed && Hover(_continue, input.MouseScreen)) return Action.Continue;
        return Action.None;
    }

    public void Draw(SpriteBatch sb, Vector2 mouse, int w, int h, float time)
    {
        DrawBackdrop(sb, w, h, time);
        bool ok = Outcome == RaidOutcome.Extracted;
        string title = Outcome switch { RaidOutcome.Extracted => "EXTRACTED", RaidOutcome.Killed => "KILLED IN ACTION", RaidOutcome.TimedOut => "MISSING IN ACTION", _ => "RAID OVER" };
        UiDraw.TextCentered(sb, Font, title, new Rectangle(0, 60, w, 40), ok ? new Color(120, 230, 130) : new Color(235, 80, 70), 6f);
        UiDraw.TextCentered(sb, Font, $"{MapName}   {TimeSpan.FromSeconds(Duration):mm\\:ss}   KILLS {Kills}   GOLD {(ok ? "+" : "")}{Gold}", new Rectangle(0, 120, w, 20), UiDraw.Accent, 2f);
        var panel = new Rectangle(w / 2 - 320, 160, 640, h - 300);
        UiDraw.PanelBox(sb, Px, panel);
        int x = panel.X + 16, y = panel.Y + 14;
        if (ok)
        {
            Font.DrawShadowed(sb, "BROUGHT BACK TO THE HIDEOUT:", new Vector2(x, y), Color.White, 2f); y += 26;
            foreach (var g in Weapons) { DrawRow(sb, Icons[g].Albedo, ItemDef.Get(g).Name, 1, x, y); y += 30; }
            foreach (var s in Brought) { DrawRow(sb, Icons[s.Type].Albedo, s.Def.Name, s.Count, x, y); y += 30; if (y > panel.Bottom - 40) break; }
            if (Weapons.Count == 0 && Brought.Count == 0) Font.DrawShadowed(sb, "(NOTHING - YOU LEFT EMPTY-HANDED)", new Vector2(x, y), Color.Gray, 1.5f);
        }
        else
        {
            Font.DrawShadowed(sb, "EVERYTHING YOU CARRIED IS LOST.", new Vector2(x, y), new Color(235, 120, 110), 2f); y += 26;
            Font.DrawShadowed(sb, "YOUR STASH IS SAFE. GEAR UP AND TRY AGAIN.", new Vector2(x, y), Color.LightGray, 1.5f); y += 20;
            if (Outcome == RaidOutcome.TimedOut) Font.DrawShadowed(sb, "THE RAID TIMER RAN OUT BEFORE YOU REACHED AN EXTRACT.", new Vector2(x, y), Color.LightGray, 1.5f);
        }
        UiDraw.Button(sb, Px, Font, _continue, "CONTINUE  [ENTER]", Hover(_continue, mouse));
    }

    private void DrawRow(SpriteBatch sb, Texture2D icon, string name, int count, int x, int y)
    {
        UiDraw.Icon(sb, icon, new Rectangle(x, y - 4, 28, 28), 2);
        Font.DrawShadowed(sb, $"{name.ToUpperInvariant()}  x{count}", new Vector2(x + 36, y + 4), Color.White, 1.5f);
    }
}
