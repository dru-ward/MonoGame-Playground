using System;
using Microsoft.Xna.Framework;

namespace Game.Core;

/// <summary>
/// Non-rotating 2D camera. View = T(-Position) * S(Zoom) * T(screenCentre).
/// Because it never rotates, view space == render-target pixel space, which the lighting pipeline relies on.
/// </summary>
public sealed class Camera2D
{
    public Vector2 Position;
    public float Zoom = 1f;
    public float TargetZoom = 1f;
    public float MinZoom = 0.5f, MaxZoom = 2.5f;
    public Matrix View { get; private set; } = Matrix.Identity;
    public Matrix InverseView { get; private set; } = Matrix.Identity;
    public Vector2 ViewportSize { get; private set; }

    /// <summary>Applies a mouse-wheel delta (120 per notch) to the target zoom.</summary>
    public void ApplyScroll(int delta)
    {
        if (delta != 0) TargetZoom = MathHelper.Clamp(TargetZoom * MathF.Pow(1.15f, delta / 120f), MinZoom, MaxZoom);
    }

    /// <summary>Smoothly follows a target (with optional look-ahead) and clamps the view inside a world rectangle.</summary>
    public void Follow(Vector2 target, float dt, Vector2 viewportSize, float worldSize, float followK = 5f, float zoomK = 8f)
    {
        ViewportSize = viewportSize;
        // Never let the view grow past the world: the effective minimum zoom depends on the window size
        // (full screen / 4K windows would otherwise show black beyond the floor and the clamp would jump).
        float minZoomForWorld = MathF.Max(viewportSize.X, viewportSize.Y) / worldSize;
        float minZoom = MathF.Max(MinZoom, minZoomForWorld);
        TargetZoom = MathHelper.Clamp(TargetZoom, minZoom, MaxZoom);
        Zoom = MathHelper.Clamp(MathHelper.Lerp(Zoom, TargetZoom, MathUtil.Damp(zoomK, dt)), minZoom, MaxZoom);

        var followed = Vector2.Lerp(Position, target, MathUtil.Damp(followK, dt));
        var half = viewportSize / (2f * Zoom);
        // clamp per axis; an axis whose view is wider than the world is centred on that axis only
        float x = half.X * 2f >= worldSize ? worldSize * 0.5f : MathHelper.Clamp(followed.X, half.X, worldSize - half.X);
        float y = half.Y * 2f >= worldSize ? worldSize * 0.5f : MathHelper.Clamp(followed.Y, half.Y, worldSize - half.Y);
        Position = new Vector2(x, y);
        Rebuild();
    }

    public void SnapTo(Vector2 pos, Vector2 viewportSize) { Position = pos; ViewportSize = viewportSize; Rebuild(); }

    private void Rebuild()
    {
        View = Matrix.CreateTranslation(-Position.X, -Position.Y, 0f)
             * Matrix.CreateScale(Zoom, Zoom, 1f)
             * Matrix.CreateTranslation(ViewportSize.X * 0.5f, ViewportSize.Y * 0.5f, 0f);
        InverseView = Matrix.Invert(View);
    }

    public Vector2 WorldToScreen(Vector2 world) => Vector2.Transform(world, View);
    public Vector2 ScreenToWorld(Vector2 screen) => Vector2.Transform(screen, InverseView);

    /// <summary>World-space rectangle currently visible (used for culling).</summary>
    public RectangleF VisibleWorld => new(Position - ViewportSize / (2f * Zoom), ViewportSize / Zoom);
}

/// <summary>Minimal float rectangle (MonoGame's Rectangle is int-only).</summary>
public readonly record struct RectangleF(Vector2 Min, Vector2 Size)
{
    public Vector2 Max => Min + Size;
    public bool Contains(Vector2 p) => p.X >= Min.X && p.Y >= Min.Y && p.X <= Max.X && p.Y <= Max.Y;
    public bool Intersects(Rectangle r) => r.Right >= Min.X && r.Left <= Max.X && r.Bottom >= Min.Y && r.Top <= Max.Y;
    public RectangleF Inflate(float amount) => new(Min - new Vector2(amount), Size + new Vector2(amount * 2));
}
