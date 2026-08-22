using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Entities;
using Game.Graphics;
using Game.Items;

namespace Game.UI;

/// <summary>
/// Screen-space, unlit UI drawn onto the back buffer after post-processing: bars, ammo, hotbar, inventory panel,
/// prompts/toasts, enemy health bars, crosshair, damage flash and death overlay. Uses the procedural PixelFont.
/// </summary>
public sealed class Hud
{
    private readonly PixelFont _font;
    private readonly Texture2D _pixel;
    private readonly Dictionary<ItemType, SpritePair> _icons;
    public string DebugLine = "";
    public bool ShowDebug = true;

    private static readonly Color Panel = new(0, 0, 0, 150);
    private static readonly Color PanelLight = new(255, 255, 255, 28);
    private static readonly Color HealthCol = new(220, 60, 50), ArmorCol = new(70, 130, 230), AmmoCol = new(240, 200, 90);

    public Hud(PixelFont font, Texture2D pixel, Dictionary<ItemType, SpritePair> icons) { _font = font; _pixel = pixel; _icons = icons; }

    public void Draw(SpriteBatch sb, GameContext ctx, Vector2 mouseScreen, bool mouseInWindow, int screenW, int screenH, Meta.Raid? raid = null)
    {
        var p = ctx.Player;
        sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);

        // ---- damage flash & death overlay ------------------------------------------------------------------
        if (p.DamageFlash > 0f) Fill(sb, new Rectangle(0, 0, screenW, screenH), new Color(180, 20, 10) * (p.DamageFlash * 0.35f));
        if (!p.IsAlive)
        {
            Fill(sb, new Rectangle(0, 0, screenW, screenH), new Color(40, 0, 0) * 0.55f);
            Centered(sb, "YOU DIED", screenH * 0.42f, new Color(230, 60, 50), 5f, screenW);
            Centered(sb, p.RespawnEnabled ? $"RESPAWNING IN {Math.Max(0f, p.RespawnTimer):0.0}" : "GEAR LOST - RETURNING TO HIDEOUT", screenH * 0.42f + 50, Color.White, 2f, screenW);
        }
        if (raid != null) DrawRaidInfo(sb, raid, ctx, screenW, screenH);

        // ---- enemy health bars + brawler wind-up rings ------------------------------------------------------
        foreach (var e in ctx.Enemies.Alive)
        {
            var sp = ctx.Camera.WorldToScreen(e.Position);
            if (sp.X < -50 || sp.Y < -50 || sp.X > screenW + 50 || sp.Y > screenH + 50) continue;
            float w = 44f * ctx.Camera.Zoom, h = 5f;
            var bar = new Rectangle((int)(sp.X - w / 2), (int)(sp.Y - 40f * ctx.Camera.Zoom), (int)w, (int)h);
            Fill(sb, bar, Color.Black * 0.6f);
            Fill(sb, new Rectangle(bar.X, bar.Y, (int)(w * e.Health / e.MaxHealth), (int)h), e.State == EnemyState.Idle ? new Color(200, 200, 60) : new Color(220, 70, 60));
            if (e.WindUpProgress > 0f)
            {
                int r = (int)(e.Radius * ctx.Camera.Zoom * (1.6f - 0.4f * e.WindUpProgress));
                Frame(sb, new Rectangle((int)sp.X - r, (int)sp.Y - r, r * 2, r * 2), new Color(255, 80, 60) * (0.5f + 0.5f * e.WindUpProgress), 2);
            }
        }

        // ---- top-left: health / armor -------------------------------------------------------------------
        int x = 16, y = 16;
        Fill(sb, new Rectangle(x - 6, y - 6, 262, 62), Panel);
        Bar(sb, new Rectangle(x, y, 250, 16), p.Health / p.MaxHealth, HealthCol);
        _font.DrawShadowed(sb, $"HP {p.Health:0}/{p.MaxHealth:0}", new Vector2(x + 6, y + 2), Color.White, 2f);
        Bar(sb, new Rectangle(x, y + 22, 250, 12), p.MaxArmor > 0 ? p.Armor / p.MaxArmor : 0f, ArmorCol);
        string armorText = p.Vest is { } vt ? $"{ItemDef.Get(vt).Name.ToUpperInvariant()} {p.Armor:0}/{p.MaxArmor:0}" : "NO VEST";
        if (p.Helmet is { } hl) armorText += $"   {ItemDef.Get(hl).Name.ToUpperInvariant()}";
        _font.DrawShadowed(sb, armorText, new Vector2(x + 6, y + 22), Color.White, 1.5f);
        _font.DrawShadowed(sb, $"GOLD {p.Gold}   SCORE {ctx.Score}   KILLS {ctx.Enemies.Kills}   ENEMIES {ctx.Enemies.Alive.Count}", new Vector2(x, y + 40), new Color(255, 225, 140), 1.5f);

        // ---- bottom-left: weapon -------------------------------------------------------------------------
        var w0 = p.CurrentWeapon; int wy = screenH - 70;
        Fill(sb, new Rectangle(10, wy - 6, 440, 60), Panel);
        _font.DrawShadowed(sb, w0.Def.ShortName, new Vector2(16, wy), Color.White, 2.5f);
        int mags = p.SpareMags(w0);
        string ammo = w0.Def.IsMelee ? "MELEE" : w0.IsReloading ? "RELOADING..." : $"{w0.AmmoInMag}/{w0.Def.MagSize}  |  {mags} MAG{(mags == 1 ? "" : "S")}";
        _font.DrawShadowed(sb, ammo, new Vector2(16, wy + 26), (w0.AmmoInMag == 0 && mags == 0) ? new Color(255, 90, 80) : AmmoCol, 2.5f);
        _font.DrawShadowed(sb, $"[Q] SWAP ({p.WeaponIndex + 1}/{p.Weapons.Count})  [R] RELOAD", new Vector2(270, wy + 2), Color.Gray, 1.5f);
        _font.DrawShadowed(sb, $"[G] GRENADE x{p.Grenades}", new Vector2(270, wy + 18), p.Grenades > 0 ? new Color(180, 230, 170) : Color.DimGray, 1.5f);
        if (w0.Attachments.Count > 0) { string at = ""; foreach (var a in w0.Attachments.Values) at += ItemDef.Get(a).Name.ToUpperInvariant() + " "; _font.DrawShadowed(sb, at, new Vector2(270, wy + 34), Color.Gray, 1.5f); }
        if (w0.IsReloading) Bar(sb, new Rectangle(16, wy + 50, 200, 4), 1f - w0.ReloadTimer / w0.Def.ReloadTime, AmmoCol);

        // ---- bottom-centre: hotbar -----------------------------------------------------------------------
        const int slot = 52, gap = 6; int total = Inventory.HotbarSize * slot + (Inventory.HotbarSize - 1) * gap;
        int hx = (screenW - total) / 2, hy = screenH - slot - 14;
        for (int i = 0; i < Inventory.HotbarSize; i++)
        {
            var r = new Rectangle(hx + i * (slot + gap), hy, slot, slot);
            DrawSlot(sb, r, p.Inventory[i], (i + 1).ToString());
        }
        _font.DrawShadowed(sb, "[TAB] INVENTORY   [E] SEARCH/OPEN   [1-5] USE", new Vector2(hx, hy - 16), Color.Gray, 1.5f);

        // ---- prompts & toasts ----------------------------------------------------------------------------
        if (p.NearbyBody != null && p.IsAlive && !p.InventoryOpen)
        {
            var sp = ctx.Camera.WorldToScreen(p.NearbyBody.Position);
            Centered(sb, "[E] SEARCH BODY", sp.Y - 50f * ctx.Camera.Zoom, new Color(255, 230, 150), 2f, (int)(sp.X * 2));
        }
        else if (p.NearbyPickup != null && p.IsAlive && !p.InventoryOpen)
        {
            var sp = ctx.Camera.WorldToScreen(p.NearbyPickup.Position);
            Centered(sb, $"[E] TAKE {p.NearbyPickup.Def.Name.ToUpperInvariant()}{(p.NearbyPickup.Stack.Count > 1 ? $" x{p.NearbyPickup.Stack.Count}" : "")}", sp.Y - 40f * ctx.Camera.Zoom, new Color(255, 230, 150), 2f, (int)(sp.X * 2));
        }
        else if (p.NearbyLootable != null && p.IsAlive && !p.InventoryOpen)
        {
            var sp = ctx.Camera.WorldToScreen(p.NearbyLootable.Center);
            Centered(sb, $"[E] OPEN {World.PropDefs.Name(p.NearbyLootable.Kind)}", sp.Y - 60f * ctx.Camera.Zoom, new Color(255, 230, 150), 2f, (int)(sp.X * 2));
        }
        if (p.Toast != null) Centered(sb, p.Toast, screenH * 0.68f, Color.White * MathHelper.Clamp(p.ToastTimer, 0f, 1f), 2f, screenW);

        // ---- crosshair -------------------------------------------------------------------------------------
        if (mouseInWindow && p.IsAlive && !p.InventoryOpen)
        {
            int cx = (int)mouseScreen.X, cy = (int)mouseScreen.Y, gapC = 5 + (int)(p.CurrentWeapon.Recoil * 4), len = 8;
            var c = new Color(240, 240, 240) * 0.85f;
            Fill(sb, new Rectangle(cx - gapC - len, cy - 1, len, 2), c); Fill(sb, new Rectangle(cx + gapC, cy - 1, len, 2), c);
            Fill(sb, new Rectangle(cx - 1, cy - gapC - len, 2, len), c); Fill(sb, new Rectangle(cx - 1, cy + gapC, 2, len), c);
        }

        // ---- debug -----------------------------------------------------------------------------------------
        if (ShowDebug && DebugLine.Length > 0)
            _font.DrawShadowed(sb, DebugLine, new Vector2(screenW - _font.Measure(DebugLine, 1.5f).X - 10, 10), Color.LightGray, 1.5f);

        sb.End();
    }

    /// <summary>Raid timer (top centre), extract compass + distance, and the hold-to-extract progress bar.</summary>
    private void DrawRaidInfo(SpriteBatch sb, Meta.Raid raid, GameContext ctx, int screenW, int screenH)
    {
        var p = ctx.Player;
        // timer
        var t = TimeSpan.FromSeconds(raid.TimeLeft);
        var tcol = raid.TimeLeft < 60f ? new Color(255, 90, 80) : raid.TimeLeft < 180f ? new Color(255, 200, 110) : Color.White;
        Fill(sb, new Rectangle(screenW / 2 - 70, 10, 140, 30), Panel);
        Centered(sb, $"{(int)t.TotalMinutes:00}:{t.Seconds:00}", 16, tcol, 2.5f, screenW);
        Centered(sb, raid.Map.Name, 44, Color.Gray, 1.5f, screenW);

        // compass to the nearest extract
        var ez = raid.World.NearestExtract(p.Position);
        if (ez != null && p.IsAlive)
        {
            var d = ez.Center - p.Position; float dist = d.Length();
            var dir = dist > 1e-3f ? d / dist : Vector2.UnitX;
            var c = new Vector2(screenW / 2f, 84f);
            // arrow: a short line + head, drawn with pixel rectangles along the direction
            for (int i = 0; i < 14; i++) { var q = c + dir * i; Fill(sb, new Rectangle((int)q.X - 1, (int)q.Y - 1, 3, 3), new Color(120, 240, 140)); }
            var tip = c + dir * 16; Fill(sb, new Rectangle((int)tip.X - 3, (int)tip.Y - 3, 6, 6), new Color(120, 240, 140));
            string label = raid.CurrentZone != null ? $"IN EXTRACT: {raid.CurrentZone.Name}" : $"{ez.Name}  {dist / 10f:0}M";
            Centered(sb, label, 100, new Color(150, 240, 160), 1.5f, screenW);
        }

        // hold-to-extract bar
        if (raid.ExtractProgress > 0f && p.IsAlive)
        {
            var bar = new Rectangle(screenW / 2 - 160, screenH / 2 + 90, 320, 18);
            Bar(sb, bar, raid.ExtractProgress, new Color(90, 220, 110));
            Centered(sb, raid.CurrentZone != null ? $"EXTRACTING... HOLD POSITION  {raid.ExtractProgress * 100:0}%" : "EXTRACTION INTERRUPTED", bar.Y - 18, Color.White, 1.5f, screenW);
        }
    }

    private void DrawSlot(SpriteBatch sb, Rectangle r, ItemStack stack, string? key)
    {
        Fill(sb, r, Panel); Frame(sb, r, PanelLight, 1);
        if (!stack.IsEmpty)
        {
            var icon = _icons[stack.Type].Albedo;
            float scale = (r.Width - 14) / (float)icon.Width;
            sb.Draw(icon, new Vector2(r.Center.X, r.Center.Y - 2), null, Color.White, 0f, new Vector2(icon.Width / 2f, icon.Height / 2f), scale, SpriteEffects.None, 0f);
            string count = stack.Count.ToString();
            _font.DrawShadowed(sb, count, new Vector2(r.Right - _font.Measure(count, 1.5f).X - 4, r.Bottom - 13), Color.White, 1.5f);
        }
        if (key != null) _font.DrawShadowed(sb, key, new Vector2(r.X + 4, r.Y + 3), new Color(255, 225, 140), 1.5f);
    }

    // ---- primitives ---------------------------------------------------------------------------------------
    private void Fill(SpriteBatch sb, Rectangle r, Color c) => sb.Draw(_pixel, r, c);
    private void Frame(SpriteBatch sb, Rectangle r, Color c, int t)
    {
        Fill(sb, new Rectangle(r.X, r.Y, r.Width, t), c); Fill(sb, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        Fill(sb, new Rectangle(r.X, r.Y, t, r.Height), c); Fill(sb, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }
    private void Bar(SpriteBatch sb, Rectangle r, float frac, Color c)
    {
        Fill(sb, r, Color.Black * 0.6f);
        Fill(sb, new Rectangle(r.X, r.Y, (int)(r.Width * MathHelper.Clamp(frac, 0f, 1f)), r.Height), c);
        Frame(sb, r, PanelLight, 1);
    }
    private void Centered(SpriteBatch sb, string text, float y, Color c, float scale, int width)
    {
        var size = _font.Measure(text, scale);
        _font.DrawShadowed(sb, text, new Vector2((width - size.X) / 2f, y), c, scale);
    }
}
