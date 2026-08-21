---
name: monogame-hud-pixel-font
description: Draw a screen-space HUD in MonoGame without SpriteFont content — a procedural 5x7 bitmap PixelFont atlas built at start-up with measure/draw/drop-shadow, 1x1-pixel primitives (fill/frame/bar), stat bars, hotbar and grid slots with icons and counts, world-anchored labels via camera.WorldToScreen, contextual prompts, fading toasts, a crosshair whose gap follows recoil, full-screen hit flash and death overlay, a debug line, and the SpriteBatch state and draw ordering that keep the HUD unlit after post-processing. Use when a MonoGame game needs UI text or HUD elements and no font asset is available or wanted.
---

# HUD & procedural pixel font

## Why a bitmap font
SpriteFonts go through MGCB and need an installed system font (not portable, extra content step). A 5×7 glyph table
in code (~55 glyphs: A–Z, 0–9, punctuation) packed into one atlas texture at start-up needs nothing and reads well
at scales 1.5–2.5.

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
Call it from the render pipeline's overlay callback (back buffer, after the final post-process pass) so nothing here
is lit, bloomed or vignetted:
```csharp
sb.Begin(SpriteSortMode.Deferred, BlendState.NonPremultiplied, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
// order: full-screen overlays (hit flash, death) → world-anchored (entity bars) → panels → prompts/toasts → crosshair → debug
sb.End();
```
Primitives on a 1×1 white pixel: `Fill(rect, color)`, `Frame(rect, color, thickness)`, `Bar(rect, frac, color)` (dark back + fill + frame).

## Pieces worth reusing
- **World-anchored labels**: `var sp = camera.WorldToScreen(entity.Position);` skip if off-screen; bar width scales with
  `camera.Zoom`; colour by state (e.g. idle vs hostile); a shrinking square frame can show a wind-up/charge progress.
- **Hotbar / grid slot**: `DrawSlot(rect, stack, keyLabel)` = panel + frame + icon (sprite scaled to slot-14) + count
  bottom-right + key top-left. An inventory panel is the same slot renderer in a grid plus a text legend.
- **Prompt over an object**: `Centered("[E] <ACTION>", sp.Y - 60*zoom, ...)` at the object's screen position.
- **Toast**: a string plus a timer; alpha = `clamp(timer, 0, 1)` for the fade.
- **Crosshair**: 4 pixel bars, gap grows with the weapon's recoil value so it "blooms" while firing; hide it when a
  mouse-driven screen (inventory, menu) is open.
- **Hit flash**: full-screen `Fill(flashColor * (flash * 0.35))`, `flash -= 2.5*dt`, `flash += 0.6` per hit.
- **Death overlay**: dark tinted fill + large title + countdown text.
- **Debug line** (FPS, entity counts, pipeline flags) top-right, toggled with a function key.

## Layout tips
Anchor to `PresentationParameters.BackBufferWidth/Height` every frame (window is resizable). Keep constants for slot
size/gap; centre groups with `(screenW - total) / 2`. `Color * alpha` on `Color` premultiplies — fine with
`NonPremultiplied` for opaque-ish UI, but for translucent panels create `new Color(r,g,b,a)` directly.
