using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace Game.Graphics;

/// <summary>A dynamic point light. Position is world pixels; Height is the distance above the z=0 plane.</summary>
public sealed class PointLight
{
    public Vector2 Position;
    public float   Height = 100f;
    public float   Radius = 400f;
    public float   Intensity = 1f;
    public Vector3 Color = Vector3.One;
    public bool    Enabled = true;
    /// <summary>Spot light: direction (world/screen space, no rotation between them) and cone angles in degrees; 0 = omni.</summary>
    public Vector2 Direction = Vector2.UnitX;
    public float   ConeOuterDeg, ConeInnerDeg;
    public bool    IsSpot => ConeOuterDeg > 0f;
    public Vector2 ConeCos => IsSpot ? new Vector2(MathF.Cos(MathHelper.ToRadians(ConeOuterDeg)), MathF.Cos(MathHelper.ToRadians(MathF.Max(1f, ConeInnerDeg)))) : new Vector2(-2f, -2f);
    /// <summary>Optional lifetime for transient lights (muzzle flashes, ricochet sparks); <= 0 means persistent.</summary>
    public float   TimeToLive;
    public float   InitialTtl;
    public float   EffectiveIntensity => Enabled ? (InitialTtl > 0f ? Intensity * MathHelper.Clamp(TimeToLive / InitialTtl, 0f, 1f) : Intensity) : 0f;
}

/// <summary>
/// Owns persistent lights (orbiting ambience, lantern) and short-lived flashes. Hands the renderer the list of
/// active lights sorted strongest-first so the single-pass path can take the top MAX_LIGHTS.
/// </summary>
public sealed class LightManager
{
    private readonly List<PointLight> _persistent = new();
    private readonly List<PointLight> _transient = new();
    private readonly List<PointLight> _active = new();

    public IReadOnlyList<PointLight> Persistent => _persistent;

    public PointLight Add(PointLight l) { _persistent.Add(l); return l; }
    public void Remove(PointLight l) => _persistent.Remove(l);

    /// <summary>Spawns a light that fades out over ttl seconds and is then dropped.</summary>
    public PointLight Flash(Vector2 pos, Vector3 color, float radius, float intensity, float ttl, float height = 40f)
    {
        var l = new PointLight { Position = pos, Color = color, Radius = radius, Intensity = intensity, TimeToLive = ttl, InitialTtl = ttl, Height = height };
        _transient.Add(l);
        return l;
    }

    public void Update(float dt)
    {
        for (int i = _transient.Count - 1; i >= 0; i--)
        {
            _transient[i].TimeToLive -= dt;
            if (_transient[i].TimeToLive <= 0f) _transient.RemoveAt(i);
        }
    }

    /// <summary>Lights worth drawing this frame (enabled, intensity > 0, within the padded view), strongest first.</summary>
    public IReadOnlyList<PointLight> GetActive(Vector2 viewMin, Vector2 viewMax)
    {
        _active.Clear();
        void Consider(PointLight l)
        {
            if (l.EffectiveIntensity <= 0.001f) return;
            if (l.Position.X + l.Radius < viewMin.X || l.Position.X - l.Radius > viewMax.X ||
                l.Position.Y + l.Radius < viewMin.Y || l.Position.Y - l.Radius > viewMax.Y) return;
            _active.Add(l);
        }
        foreach (var l in _persistent) Consider(l);
        foreach (var l in _transient) Consider(l);
        _active.Sort((a, b) => (b.EffectiveIntensity * b.Radius).CompareTo(a.EffectiveIntensity * a.Radius));
        return _active;
    }
}
