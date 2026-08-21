using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game.Graphics;

/// <summary>
/// A tiny procedural 5x7 bitmap font so the HUD needs no SpriteFont content (fonts through MGCB require an
/// installed system font, which is not portable). Lower-case letters map to upper-case. Glyphs are packed
/// into a single atlas texture at start-up and drawn with SpriteBatch at any integer-ish scale.
/// </summary>
public sealed class PixelFont
{
    public const int GlyphW = 5, GlyphH = 7, Advance = 6;   // 1px spacing
    private readonly Texture2D _atlas;
    private readonly Dictionary<char, Rectangle> _glyphs = new();
    private readonly Rectangle _unknown;

    public PixelFont(GraphicsDevice gd)
    {
        var defs = Glyphs();
        int count = defs.Count;
        var data = new Color[count * Advance * GlyphH];
        int idx = 0;
        foreach (var (ch, rows) in defs)
        {
            int x0 = idx * Advance;
            for (int y = 0; y < GlyphH; y++)
            for (int x = 0; x < GlyphW; x++)
                if (rows[y][x] == '#') data[y * (count * Advance) + x0 + x] = Color.White;
            _glyphs[ch] = new Rectangle(x0, 0, GlyphW, GlyphH);
            idx++;
        }
        _atlas = new Texture2D(gd, count * Advance, GlyphH);
        _atlas.SetData(data);
        _unknown = _glyphs['?'];
    }

    public Vector2 Measure(string text, float scale = 1f) => new(text.Length * Advance * scale - scale, GlyphH * scale);

    public void Draw(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 2f)
    {
        float x = pos.X;
        foreach (char raw in text)
        {
            char c = char.ToUpperInvariant(raw);
            if (c != ' ')
            {
                var src = _glyphs.TryGetValue(c, out var r) ? r : _unknown;
                sb.Draw(_atlas, new Vector2(x, pos.Y), src, color, 0f, Vector2.Zero, scale, SpriteEffects.None, 0f);
            }
            x += Advance * scale;
        }
    }

    /// <summary>Text with a 1px dark drop shadow (readable over any background).</summary>
    public void DrawShadowed(SpriteBatch sb, string text, Vector2 pos, Color color, float scale = 2f)
    {
        Draw(sb, text, pos + new Vector2(scale, scale), Color.Black * 0.8f, scale);
        Draw(sb, text, pos, color, scale);
    }

    private static List<(char, string[])> Glyphs() => new()
    {
        ('A', new[]{" ### ","#   #","#   #","#####","#   #","#   #","#   #"}),
        ('B', new[]{"#### ","#   #","#   #","#### ","#   #","#   #","#### "}),
        ('C', new[]{" ### ","#   #","#    ","#    ","#    ","#   #"," ### "}),
        ('D', new[]{"#### ","#   #","#   #","#   #","#   #","#   #","#### "}),
        ('E', new[]{"#####","#    ","#    ","#### ","#    ","#    ","#####"}),
        ('F', new[]{"#####","#    ","#    ","#### ","#    ","#    ","#    "}),
        ('G', new[]{" ### ","#   #","#    ","# ###","#   #","#   #"," ### "}),
        ('H', new[]{"#   #","#   #","#   #","#####","#   #","#   #","#   #"}),
        ('I', new[]{" ### ","  #  ","  #  ","  #  ","  #  ","  #  "," ### "}),
        ('J', new[]{"  ###","   # ","   # ","   # ","   # ","#  # "," ##  "}),
        ('K', new[]{"#   #","#  # ","# #  ","##   ","# #  ","#  # ","#   #"}),
        ('L', new[]{"#    ","#    ","#    ","#    ","#    ","#    ","#####"}),
        ('M', new[]{"#   #","## ##","# # #","# # #","#   #","#   #","#   #"}),
        ('N', new[]{"#   #","##  #","# # #","#  ##","#   #","#   #","#   #"}),
        ('O', new[]{" ### ","#   #","#   #","#   #","#   #","#   #"," ### "}),
        ('P', new[]{"#### ","#   #","#   #","#### ","#    ","#    ","#    "}),
        ('Q', new[]{" ### ","#   #","#   #","#   #","# # #","#  # "," ## #"}),
        ('R', new[]{"#### ","#   #","#   #","#### ","# #  ","#  # ","#   #"}),
        ('S', new[]{" ####","#    ","#    "," ### ","    #","    #","#### "}),
        ('T', new[]{"#####","  #  ","  #  ","  #  ","  #  ","  #  ","  #  "}),
        ('U', new[]{"#   #","#   #","#   #","#   #","#   #","#   #"," ### "}),
        ('V', new[]{"#   #","#   #","#   #","#   #","#   #"," # # ","  #  "}),
        ('W', new[]{"#   #","#   #","#   #","# # #","# # #","## ##","#   #"}),
        ('X', new[]{"#   #","#   #"," # # ","  #  "," # # ","#   #","#   #"}),
        ('Y', new[]{"#   #","#   #"," # # ","  #  ","  #  ","  #  ","  #  "}),
        ('Z', new[]{"#####","    #","   # ","  #  "," #   ","#    ","#####"}),
        ('0', new[]{" ### ","#   #","#  ##","# # #","##  #","#   #"," ### "}),
        ('1', new[]{"  #  "," ##  ","  #  ","  #  ","  #  ","  #  "," ### "}),
        ('2', new[]{" ### ","#   #","    #","   # ","  #  "," #   ","#####"}),
        ('3', new[]{"#####","   # ","  #  ","   # ","    #","#   #"," ### "}),
        ('4', new[]{"   # ","  ## "," # # ","#  # ","#####","   # ","   # "}),
        ('5', new[]{"#####","#    ","#### ","    #","    #","#   #"," ### "}),
        ('6', new[]{"  ## "," #   ","#    ","#### ","#   #","#   #"," ### "}),
        ('7', new[]{"#####","    #","   # ","  #  "," #   "," #   "," #   "}),
        ('8', new[]{" ### ","#   #","#   #"," ### ","#   #","#   #"," ### "}),
        ('9', new[]{" ### ","#   #","#   #"," ####","    #","   # "," ##  "}),
        ('.', new[]{"     ","     ","     ","     ","     "," ##  "," ##  "}),
        (',', new[]{"     ","     ","     ","     "," ##  "," ##  ","  #  "}),
        (':', new[]{"     "," ##  "," ##  ","     "," ##  "," ##  ","     "}),
        ('!', new[]{"  #  ","  #  ","  #  ","  #  ","  #  ","     ","  #  "}),
        ('?', new[]{" ### ","#   #","    #","   # ","  #  ","     ","  #  "}),
        ('-', new[]{"     ","     ","     ","#####","     ","     ","     "}),
        ('+', new[]{"     ","  #  ","  #  ","#####","  #  ","  #  ","     "}),
        ('/', new[]{"    #","    #","   # ","  #  "," #   ","#    ","#    "}),
        ('%', new[]{"##  #","##  #","   # ","  #  "," #   ","#  ##","#  ##"}),
        ('(', new[]{"   # ","  #  "," #   "," #   "," #   ","  #  ","   # "}),
        (')', new[]{" #   ","  #  ","   # ","   # ","   # ","  #  "," #   "}),
        ('[', new[]{" ### "," #   "," #   "," #   "," #   "," #   "," ### "}),
        (']', new[]{" ### ","   # ","   # ","   # ","   # ","   # "," ### "}),
        ('<', new[]{"   # ","  #  "," #   ","#    "," #   ","  #  ","   # "}),
        ('>', new[]{" #   ","  #  ","   # ","    #","   # ","  #  "," #   "}),
        ('=', new[]{"     ","     ","#####","     ","#####","     ","     "}),
        ('_', new[]{"     ","     ","     ","     ","     ","     ","#####"}),
        ('\'',new[]{"  #  ","  #  ","     ","     ","     ","     ","     "}),
        ('"', new[]{" # # "," # # ","     ","     ","     ","     ","     "}),
        ('#', new[]{" # # ","#####"," # # "," # # ","#####"," # # ","     "}),
        ('*', new[]{"     ","# # #"," ### ","#####"," ### ","# # #","     "}),
        ('|', new[]{"  #  ","  #  ","  #  ","  #  ","  #  ","  #  ","  #  "}),
    };
}
