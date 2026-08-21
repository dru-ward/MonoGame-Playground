using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

namespace Game.Core;

/// <summary>
/// Snapshot of keyboard + mouse for the current frame with edge detection against the previous frame.
/// Call <see cref="Update"/> once at the top of Game.Update.
/// </summary>
public sealed class InputState
{
    private KeyboardState _keys, _prevKeys;
    private MouseState _mouse, _prevMouse;

    public bool WindowActive { get; private set; }
    public Rectangle Viewport { get; private set; }

    public void Update(bool windowActive, Rectangle viewport)
    {
        _prevKeys = _keys; _keys = Keyboard.GetState();
        _prevMouse = _mouse; _mouse = Mouse.GetState();
        WindowActive = windowActive; Viewport = viewport;
    }

    public bool Down(Keys k)     => _keys.IsKeyDown(k);
    public bool Pressed(Keys k)  => _keys.IsKeyDown(k) && !_prevKeys.IsKeyDown(k);
    public bool Released(Keys k) => !_keys.IsKeyDown(k) && _prevKeys.IsKeyDown(k);
    public bool AnyDown(params Keys[] keys) { foreach (var k in keys) if (_keys.IsKeyDown(k)) return true; return false; }

    public Vector2 MouseScreen => new(_mouse.X, _mouse.Y);
    /// <summary>True when the cursor is inside the client area and the window has focus.</summary>
    public bool MouseInWindow => WindowActive && Viewport.Contains(_mouse.X, _mouse.Y);
    public bool LeftDown     => MouseInWindow && _mouse.LeftButton == ButtonState.Pressed;
    public bool LeftPressed  => MouseInWindow && _mouse.LeftButton == ButtonState.Pressed && _prevMouse.LeftButton == ButtonState.Released;
    public bool RightDown    => MouseInWindow && _mouse.RightButton == ButtonState.Pressed;
    public bool RightPressed => MouseInWindow && _mouse.RightButton == ButtonState.Pressed && _prevMouse.RightButton == ButtonState.Released;
    public int  ScrollDelta  => _mouse.ScrollWheelValue - _prevMouse.ScrollWheelValue;

    /// <summary>WASD / arrow keys as a normalised movement vector (screen +Y is down).</summary>
    public Vector2 MoveAxis()
    {
        var v = Vector2.Zero;
        if (AnyDown(Keys.W, Keys.Up))    v.Y -= 1f;
        if (AnyDown(Keys.S, Keys.Down))  v.Y += 1f;
        if (AnyDown(Keys.A, Keys.Left))  v.X -= 1f;
        if (AnyDown(Keys.D, Keys.Right)) v.X += 1f;
        return v == Vector2.Zero ? v : Vector2.Normalize(v);
    }
}
