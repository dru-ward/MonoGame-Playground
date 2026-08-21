using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Graphics;

namespace Game.UI;

/// <summary>Pixel-rectangle UI primitives shared by the HUD and the inventory screen.</summary>
public static class UiDraw
{
    public static readonly Color Panel = new(8, 10, 13, 215);
    public static readonly Color PanelLight = new(255, 255, 255, 28);
    public static readonly Color Accent = new(255, 200, 110);
    public static readonly Color Hover = new(255, 220, 140, 70);
    public static readonly Color Selected = new(120, 200, 255, 90);

    public static void Fill(SpriteBatch sb, Texture2D px, Rectangle r, Color c) => sb.Draw(px, r, c);

    public static void Frame(SpriteBatch sb, Texture2D px, Rectangle r, Color c, int t = 1)
    {
        Fill(sb, px, new Rectangle(r.X, r.Y, r.Width, t), c); Fill(sb, px, new Rectangle(r.X, r.Bottom - t, r.Width, t), c);
        Fill(sb, px, new Rectangle(r.X, r.Y, t, r.Height), c); Fill(sb, px, new Rectangle(r.Right - t, r.Y, t, r.Height), c);
    }

    public static void Bar(SpriteBatch sb, Texture2D px, Rectangle r, float frac, Color c)
    {
        Fill(sb, px, r, Color.Black * 0.6f);
        Fill(sb, px, new Rectangle(r.X, r.Y, (int)(r.Width * MathHelper.Clamp(frac, 0f, 1f)), r.Height), c);
        Frame(sb, px, r, PanelLight);
    }

    public static void PanelBox(SpriteBatch sb, Texture2D px, Rectangle r, Color? fill = null, Color? edge = null)
    {
        Fill(sb, px, r, fill ?? Panel);
        Frame(sb, px, r, edge ?? new Color(255, 255, 255, 50));
    }

    /// <summary>Icon centred in a rectangle, scaled to fit with padding.</summary>
    public static void Icon(SpriteBatch sb, Texture2D icon, Rectangle r, int pad = 7, Color? tint = null)
    {
        float scale = (r.Width - pad * 2) / (float)System.Math.Max(icon.Width, icon.Height);
        sb.Draw(icon, new Vector2(r.Center.X, r.Center.Y), null, tint ?? Color.White, 0f, new Vector2(icon.Width / 2f, icon.Height / 2f), scale, SpriteEffects.None, 0f);
    }

    /// <summary>Panel-style button; returns nothing — hit-testing is done by the caller with the same rect.</summary>
    public static void Button(SpriteBatch sb, Texture2D px, PixelFont font, Rectangle r, string label, bool hovered, bool enabled = true, Color? fill = null, float scale = 2f)
    {
        var bg = fill ?? new Color(28, 34, 40, 235);
        if (!enabled) bg = new Color(20, 20, 22, 200);
        PanelBox(sb, px, r, bg, hovered && enabled ? Color.White : PanelLight);
        if (hovered && enabled) Fill(sb, px, r, Hover);
        TextCentered(sb, font, label, r, enabled ? Color.White : Color.DimGray, scale);
    }

    public static void TextCentered(SpriteBatch sb, PixelFont font, string text, Rectangle r, Color c, float scale)
    {
        var size = font.Measure(text, scale);
        font.DrawShadowed(sb, text, new Vector2(r.Center.X - size.X / 2f, r.Center.Y - size.Y / 2f), c, scale);
    }
}
