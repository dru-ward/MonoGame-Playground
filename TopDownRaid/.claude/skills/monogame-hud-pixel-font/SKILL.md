---
name: monogame-hud-pixel-font
description: Draw a screen-space HUD in MonoGame without SpriteFont content — a procedural 5x7 bitmap PixelFont atlas built at start-up, pixel-rectangle primitives (fill/frame/bar), health/armor/ammo panels, hotbar and inventory grid with icons, world-anchored labels (enemy health bars via camera.WorldToScreen), prompts/toasts, crosshair with recoil spread, damage flash and death overlay, all drawn after post-processing so they stay unlit. Use when a MonoGame game needs UI text or HUD elements and no font asset is available or wanted.
---

# HUD & procedural pixel font

## Why a bitmap font
SpriteFonts go through MGCB and need an installed system font (not portable, extra content step). A 5×7 glyph table
in code (~55 glyphs: A–Z, 0–9, punctuation) packed into one atlas texture at start-up needs nothing and looks right
for a retro-tech HUD at scales 1.5–2.5.

```csharp
public sealed class PixelFont {
  public const int GlyphW = 5, GlyphH = 7, Advance = 6;
  // ('A', new[]{" ### ","#   #","#   #","#####","#   #","#   #","#   #"}), ...  '#' = pixel
  public PixelFont(GraphicsDevice gd) { build Color[] atlas (count*Advance x 7), one Rectangle per char, '?' = fallback }
  public Vector2 Measure(string text, float scale) => new(text.Length * Advance * scale - scale, GlyphH * scale);
  public void Draw(SpriteBatch sb, string text, Vector2 pos, Color c, float scale = 2f)   // uppercases input
  public void DrawShadowed(...)   // 1px black drop shadow first — readable over any scene
}
```
Sample with `SamplerState.PointClamp` and integer-ish scales for crisp pixels.

## HUD draw pass
Called by the render pipeline's overlay callback (back buffer, after FinalCombine) so nothing here is lit, bloomed or
vignetted:
```csharp
sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
// order: full-screen overlays (damage flash, death) → world-anchored (enemy bars) → panels → prompts/toasts → crosshair → debug
sb.End();
```
Primitives on a 1×1 white pixel: `Fill(rect, color)`, `Frame(rect, color, thickness)`, `Bar(rect, frac, color)` (dark back + fill + frame).

## Pieces worth reusing
- **World-anchored labels**: `var sp = camera.WorldToScreen(enemy.Position);` skip if off-screen; bar width scales with
  `camera.Zoom`; colour by state (yellow idle, red hostile); a shrinking square frame shows melee wind-up progress.
- **Hotbar / inventory slot**: `DrawSlot(rect, stack, keyLabel)` = panel + frame + icon (`SpritePair.Albedo`, scaled to
  slot-14) + count bottom-right + key top-left. The inventory panel is the same slot renderer in a grid + a legend.
- **Prompt over an object**: `Centered("[E] OPEN CRATE", sp.Y - 60*zoom, ...)` at the crate's screen position.
- **Toast**: `player.Toast` with a timer; alpha = `clamp(timer, 0, 1)` for the fade.
- **Crosshair**: 4 pixel bars, gap grows with `weapon.Recoil` so it "blooms" while firing; hidden when the inventory is open.
- **Damage flash**: full-screen `Fill(new Color(180,20,10) * (flash * 0.35))`, `flash -= 2.5*dt`, +0.6 per hit.
- **Death overlay**: dark red fill + big "YOU DIED" + "RESPAWNING IN x.x".
- **Debug line** (FPS, counts, pipeline flags) top-right, toggled with F11.

## Layout tips
Anchor to `PresentationParameters.BackBufferWidth/Height` every frame (window is resizable). Keep constants for slot
size/gap; centre groups with `(screenW - total) / 2`. `Color * alpha` on `Color` premultiplies — fine with
`NonPremultiplied` for opaque-ish UI, but for translucent panels create `new Color(r,g,b,a)` directly.
