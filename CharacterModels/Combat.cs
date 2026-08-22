using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace CharacterModels;

public enum AttackKind { Light, Heavy, Special }

/// <summary>
/// Per-weapon combat authored as hand paths: every stance and attack places the weapon hand at a point in character
/// space (metres; +Z forward, +X the character's left, Y up) with an explicit weapon direction, solved with
/// PoseWriter.WeaponIK. The trunk, hips and legs are driven from the same timeline so the body produces the swing.
///
/// Principles (monogame-weapon-combat skill): wind-up 0.1-0.2 s light / 0.3-0.45 s heavy, peak velocity ~65 % through
/// the swing, recovery ~40-50 % of the cycle with chaining allowed from ~70 %, power from hips -> shoulders -> arm,
/// two-handed grips never separate, the idle dagger stays in guard, bows nock-draw-release-watch.
/// </summary>
public static class Combat
{
    // ------------------------------------------------------------------ curves

    private static float Bump(float u, float a, float peak, float b)
    {
        if (u <= a || u >= b) return 0f;
        float x = u < peak ? (u - a) / (peak - a) : 1f - (u - peak) / (b - peak);
        return x * x * (3f - 2f * x);
    }
    private static float Ramp(float u, float a, float b) => Clips.Smooth01((u - a) / (b - a));
    /// <summary>0 to 1 with the velocity peak at ~65 % (slow start, whip through, soft stop).</summary>
    private static float Whip(float u, float a, float b)
    {
        float x = MathHelper.Clamp((u - a) / (b - a), 0, 1);
        float tail = MathHelper.Clamp(1f - (x - 0.65f) / 0.35f, 0f, 1f);
        return x < 0.65f ? 0.5f * MathF.Pow(x / 0.65f, 2.2f) : 0.5f + 0.5f * (1f - MathF.Pow(tail, 1.8f));
    }
    /// <summary>Catmull-Rom through points (t in 0..1 over the whole list).</summary>
    private static Vector3 Path(float t, Vector3[] p)
    {
        int n = p.Length - 1;
        float f = MathHelper.Clamp(t, 0, 0.9999f) * n;
        int i = (int)f; float s = f - i;
        Vector3 p0 = p[Math.Max(i - 1, 0)], p1 = p[i], p2 = p[Math.Min(i + 1, n)], p3 = p[Math.Min(i + 2, n)];
        return 0.5f * ((2 * p1) + (-p0 + p2) * s + (2 * p0 - 5 * p1 + 4 * p2 - p3) * (s * s) + (-p0 + 3 * p1 - 3 * p2 + p3) * (s * s * s));
    }
    private static Vector3 V(float x, float y, float z) => new(x, y, z);

    // Body helpers ----------------------------------------------------------

    /// <summary>Hips lead, shoulders follow, head stays on target. twist + = left shoulder back. Degrees.</summary>
    private static void Trunk(PoseWriter w, float twist, float lean, float tilt = 0f)
    {
        w.Upright("hips", lean * 0.2f, tilt * 0.3f, twist * 0.5f);
        w.Upright("spine", lean * 0.4f, tilt * 0.3f, twist * 0.3f);
        w.Upright("chest", lean * 0.4f, tilt * 0.4f, twist * 0.35f);
        w.Upright("neck", -lean * 0.3f, 0, -twist * 0.5f);
        w.Upright("head", -lean * 0.4f, 0, -twist * 0.6f);
    }
    /// <summary>step: +1 left foot forward lunge, -1 right foot forward; crouch 0..1.</summary>
    private static void Legs(PoseWriter w, float step, float crouch)
    {
        w.Hang("thighL", 22f * step + 14f * crouch, 5, 1);
        w.Hang("shinL", -24f * MathF.Max(0, step) - 26f * crouch, 0, 1);
        w.Foot("footL", 6f * step, 1);
        w.Hang("thighR", -22f * step + 14f * crouch, 7, -1);
        w.Hang("shinR", -24f * MathF.Max(0, -step) - 26f * crouch, 0, -1);
        w.Foot("footR", -6f * step, -1, 0, 20f * MathF.Max(0, step));
        w.Root(new Vector3(0, -0.12f * crouch - 0.03f * MathF.Abs(step), 0));
    }

    // ------------------------------------------------------------------ stances

    /// <summary>Drawn-weapon stance overlay. amount 0..1; speedNorm m/s; t seconds (for breathing).</summary>
    public static void Stance(Weapon weapon, float amount, float speedNorm, float t, PoseWriter w, Skeleton skel)
    {
        if (amount <= 0.001f) return;
        float a = amount;
        float still = 1f - Clips.Smooth01(speedNorm / 0.7f);
        float br = MathF.Sin(t * 1.7f) * 0.01f;
        switch (weapon)
        {
            case Weapon.Sword:
                // Guard: sword held at waist height in front-right, point forward and slightly up; shield square to the front.
                w.WeaponIK(-1, V(-0.22f, 0.95f + br, 0.30f), V(-1f, -0.4f, -0.3f), V(-0.15f, 0.35f, 1f), V(0, 1, 0), a);
                w.Hang("armL", 40f * a, 30f * a, 1, 25f * a); w.Hang("foreL", 100f * a, 10f * a, 1); w.Hang("handL", 0, 0, 1, 20f * a);
                Trunk(w, 14f * a, 3f * a); w.Upright("head", 0, 0, -14f * a);
                Legs(w, 0.35f * a * still, 0.12f * a);
                break;
            case Weapon.Axe:
                // Two hands on the haft, axe across the body at hip height, head to the right; weight back.
                w.WeaponIK(-1, V(-0.18f, 0.92f + br, 0.22f), V(-1f, -0.5f, 0f), V(0.55f, 0.15f, 0.8f), V(0, 1, 0), a);
                w.ArmIK(1, w.WeaponPoint(-1, 0.42f), V(1f, -0.4f, 0.5f), a);
                Trunk(w, 10f * a, -3f * a); w.Upright("head", 2f * a, 0, -10f * a);
                Legs(w, -0.3f * a * still, 0.15f * a);
                break;
            case Weapon.Daggers:
                // Low crouch, both blades forward and low, elbows tucked; the whole body bobs.
                float bob = MathF.Sin(t * 2.4f) * 0.015f;
                w.WeaponIK(-1, V(-0.20f, 0.78f + bob, 0.30f), V(-1f, -0.6f, -0.2f), V(-0.1f, -0.2f, 1f), V(0, 1, 0), a);
                w.WeaponIK(1, V(0.20f, 0.82f - bob, 0.26f), V(1f, -0.6f, -0.2f), V(0.1f, -0.2f, 1f), V(0, 1, 0), a);
                Trunk(w, 0, 14f * a); w.Upright("head", -10f * a, 0, 0);
                Legs(w, 0.2f * a * still, 0.4f * a);
                break;
            case Weapon.Staff:
                // Staff planted ahead and angled back to the shoulder, both hands on it.
                w.WeaponIK(-1, V(-0.16f, 1.05f + br, 0.18f), V(-1f, -0.3f, 0.2f), V(0.05f, 1f, 0.35f), V(0, 0, 1), a);
                w.ArmIK(1, w.WeaponPoint(-1, 0.5f), V(1f, -0.2f, 0.5f), a);
                Trunk(w, -6f * a, -2f * a); w.Upright("head", -2f * a, 0, 6f * a);
                Legs(w, 0.15f * a * still, 0.05f * a);
                break;
            case Weapon.Bow:
                // Bow in the left hand, held low and vertical in front of the hip; right hand near the string; side-on.
                w.WeaponIK(1, V(0.18f, 0.85f + br, 0.25f), V(1f, -0.5f, 0f), V(0.05f, 1f, 0.1f), V(0, 0, 1), a);
                w.ArmIK(-1, V(-0.02f, 0.95f, 0.12f), V(-1f, -0.3f, -0.5f), a);
                Trunk(w, 24f * a, 2f * a); w.Upright("head", 0, 0, -24f * a);
                Legs(w, 0.3f * a * still, 0.08f * a);
                break;
        }
    }

    public static (float speed, float stride) Locomotion(Weapon weapon) => weapon switch
    {
        Weapon.Sword => (0.85f, 0.85f), Weapon.Axe => (0.8f, 0.9f), Weapon.Daggers => (1.05f, 0.8f),
        Weapon.Staff => (0.8f, 0.95f), Weapon.Bow => (0.9f, 0.9f), _ => (1f, 1f)
    };

    // ------------------------------------------------------------------ attacks

    public sealed record AttackDef(string Name, float Duration, float CancelFrom, float HitAt, float RootAdvance, Action<float, PoseWriter, Skeleton> Pose);

    public static AttackDef Get(Weapon weapon, AttackKind kind) => weapon switch
    {
        Weapon.Sword => kind switch { AttackKind.Light => SwordCut, AttackKind.Heavy => SwordOverhead, _ => ShieldBash },
        Weapon.Axe => kind switch { AttackKind.Light => AxeSweep, AttackKind.Heavy => AxeSmash, _ => AxeSpin },
        Weapon.Daggers => kind switch { AttackKind.Light => DaggerStabs, AttackKind.Heavy => DaggerLunge, _ => DaggerFlurry },
        Weapon.Staff => kind switch { AttackKind.Light => StaffBolt, AttackKind.Heavy => StaffNova, _ => StaffBeam },
        Weapon.Bow => kind switch { AttackKind.Light => BowShot, AttackKind.Heavy => BowFullDraw, _ => BowVolley },
        _ => Punch
    };

    /// <summary>Three-phase weapon-hand motion: rest to path[0] over the wind-up, whip along the path, path[^1] back to rest in recovery.</summary>
    private static void SwingArc(PoseWriter w, int side, float u, float windEnd, float swingEnd, float recStart,
                                 Vector3 rest, Vector3 restDir, Vector3[] path, Vector3[] dirs, Vector3 elbowWind, Vector3 elbowSwing, Vector3 edge)
    {
        if (u < windEnd)
        {
            float k = Ramp(u, 0, windEnd);
            w.WeaponIK(side, Vector3.Lerp(rest, path[0], k), elbowWind, Vector3.Lerp(restDir, dirs[0], k), edge);
        }
        else if (u < recStart)
        {
            float k = Whip(u, windEnd, swingEnd);
            w.WeaponIK(side, Path(k, path), elbowSwing, Path(k, dirs), edge);
        }
        else
        {
            float k = Ramp(u, recStart, 1f);
            w.WeaponIK(side, Vector3.Lerp(path[^1], rest, k), elbowWind, Vector3.Lerp(dirs[^1], restDir, k), edge);
        }
    }
    private static readonly Vector3 Up = Vector3.Up, Right = Vector3.Left;   // "Right" = the character's right (-X)

    // ---- Sword (Knight)
    private static readonly Vector3 SwordRest = V(-0.22f, 0.95f, 0.30f), SwordRestDir = V(-0.15f, 0.35f, 1f);
    private static readonly Vector3[] CutPath = { V(-0.48f, 1.25f, -0.12f), V(-0.5f, 1.12f, 0.25f), V(-0.1f, 1.0f, 0.62f), V(0.35f, 0.95f, 0.5f), V(0.45f, 1.0f, 0.25f) };
    private static readonly Vector3[] CutDir = { V(-0.6f, 0.5f, -0.6f), V(-0.7f, 0.3f, 0.6f), V(-0.2f, 0f, 1f), V(0.6f, -0.1f, 0.8f), V(0.9f, 0f, 0.3f) };
    private static readonly AttackDef SwordCut = new("Cut", 0.75f, 0.55f, 0.32f, 0.25f, (u, w, sk) =>
    {
        SwingArc(w, -1, u, 0.2f, 0.48f, 0.6f, SwordRest, SwordRestDir, CutPath, CutDir, V(-1f, -0.2f, -0.4f), V(-1f, -0.3f, 0.2f), Up);
        float wind = Ramp(u, 0, 0.2f), rec = Ramp(u, 0.6f, 1f), s = Whip(u, 0.2f, 0.48f) * (1f - rec);
        Trunk(w, 30f * wind * (1 - s) - 35f * s, 6f * s);
        w.Hang("armL", 40f, 30f, 1, 25f); w.Hang("foreL", 100f, 10f, 1);
        Legs(w, 0.3f * wind * (1 - s) + 0.8f * s, 0.15f);
    });
    private static readonly Vector3[] OverPath = { V(-0.25f, 1.75f, -0.35f), V(-0.2f, 1.95f, 0.05f), V(-0.15f, 1.5f, 0.55f), V(-0.12f, 0.8f, 0.7f), V(-0.15f, 0.55f, 0.6f) };
    private static readonly Vector3[] OverDir = { V(-0.1f, 0.3f, -1f), V(-0.05f, 1f, 0.2f), V(0f, 0.4f, 1f), V(0f, -0.6f, 0.8f), V(0f, -0.9f, 0.4f) };
    private static readonly AttackDef SwordOverhead = new("Overhead", 1.15f, 0.7f, 0.47f, 0.35f, (u, w, sk) =>
    {
        SwingArc(w, -1, u, 0.32f, 0.56f, 0.7f, SwordRest, SwordRestDir, OverPath, OverDir, V(-1f, 0.3f, -0.4f), V(-1f, 0.2f, -0.2f), Right);
        float wind = Ramp(u, 0, 0.32f), rec = Ramp(u, 0.7f, 1f), c = Whip(u, 0.34f, 0.56f) * (1f - rec);
        Trunk(w, -8f * wind * (1 - c) + 6f * c, -12f * wind * (1 - c) + 26f * c);
        w.Hang("armL", 40f, 30f, 1, 25f); w.Hang("foreL", 100f, 10f, 1);
        Legs(w, 0.9f * c, 0.1f + 0.35f * c);
    });
    private static readonly AttackDef ShieldBash = new("Shield bash", 0.8f, 0.55f, 0.3f, 0.45f, (u, w, sk) =>
    {
        float wind = Bump(u, 0, 0.18f, 0.3f), b = Whip(u, 0.18f, 0.38f) * (1f - Ramp(u, 0.55f, 1f));
        w.WeaponIK(-1, V(-0.25f, 0.95f, 0.25f), V(-1f, -0.4f, -0.3f), V(-0.2f, 0.4f, 1f), Up);
        w.Hang("armL", 40f - 10f * wind + 50f * b, 30f + 5f * b, 1, 25f);
        w.Hang("foreL", 100f - 15f * wind - 55f * b, 10f, 1);
        Trunk(w, 18f * wind - 28f * b, -4f * wind + 14f * b);
        Legs(w, 0.9f * b, 0.2f);
    });

    // ---- Axe (Barbarian): two hands on the haft throughout; the off-hand rides the haft via IK each frame.
    private static readonly Vector3 AxeRest = V(-0.18f, 0.92f, 0.22f), AxeRestDir = V(0.55f, 0.25f, 0.8f);
    private static void AxeOffHand(PoseWriter w) => w.ArmIK(1, w.WeaponPoint(-1, 0.42f), V(1f, -0.4f, 0.4f));
    private static readonly Vector3[] SweepPath = { V(-0.55f, 1.05f, -0.35f), V(-0.6f, 1.0f, 0.15f), V(-0.15f, 0.95f, 0.62f), V(0.4f, 0.95f, 0.5f), V(0.6f, 1.05f, 0.15f) };
    private static readonly Vector3[] SweepDir = { V(-0.8f, 0.2f, -0.5f), V(-0.9f, 0.1f, 0.4f), V(-0.2f, 0f, 1f), V(0.7f, 0f, 0.7f), V(1f, 0.1f, 0f) };
    private static readonly AttackDef AxeSweep = new("Sweep", 1.15f, 0.75f, 0.5f, 0.3f, (u, w, sk) =>
    {
        SwingArc(w, -1, u, 0.34f, 0.6f, 0.68f, AxeRest, AxeRestDir, SweepPath, SweepDir, V(-1f, -0.3f, -0.4f), V(-1f, -0.3f, 0f), Up);
        AxeOffHand(w);
        float wind = Ramp(u, 0, 0.34f), rec = Ramp(u, 0.68f, 1f), s = Whip(u, 0.34f, 0.6f) * (1f - rec), over = Bump(u, 0.58f, 0.68f, 0.82f);
        Trunk(w, 40f * wind * (1 - s) - 45f * s - 10f * over, 4f * s, -6f * wind * (1 - s) + 8f * s);
        Legs(w, -0.3f * wind * (1 - s) + 0.8f * s, 0.2f + 0.1f * s);
    });
    private static readonly Vector3[] SmashPath = { V(-0.15f, 1.85f, -0.45f), V(-0.05f, 2.05f, 0.0f), V(0f, 1.55f, 0.6f), V(0.02f, 0.7f, 0.8f), V(0.02f, 0.45f, 0.7f) };
    private static readonly Vector3[] SmashDir = { V(0f, 0.4f, -1f), V(0f, 1f, 0.1f), V(0f, 0.3f, 1f), V(0f, -0.7f, 0.7f), V(0f, -0.95f, 0.3f) };
    private static readonly AttackDef AxeSmash = new("Smash", 1.55f, 0.85f, 0.52f, 0.5f, (u, w, sk) =>
    {
        SwingArc(w, -1, u, 0.4f, 0.6f, 0.74f, AxeRest, AxeRestDir, SmashPath, SmashDir, V(-1f, 0.3f, -0.4f), V(-1f, 0.3f, -0.2f), Right);
        AxeOffHand(w);
        float wind = Ramp(u, 0, 0.4f), rec = Ramp(u, 0.74f, 1f), c = Whip(u, 0.42f, 0.6f) * (1f - rec);
        Trunk(w, -6f * wind * (1 - c) + 8f * c, -16f * wind * (1 - c) + 32f * c);
        Legs(w, 1f * c, 0.1f + 0.5f * c);
    });
    private static readonly AttackDef AxeSpin = new("Whirlwind", 1.6f, 0.9f, 0.55f, 0.2f, (u, w, sk) =>
    {
        float wind = Ramp(u, 0, 0.22f), ext = Bump(u, 0.15f, 0.55f, 0.95f);
        float ang = MathHelper.TwoPi * Ramp(u, 0.22f, 0.9f);
        float r = 0.35f + 0.3f * ext;
        var hand = V(-MathF.Sin(ang) * r, 1.15f, MathF.Cos(ang) * r * 0.6f + 0.1f);
        var dir = V(-MathF.Cos(ang), 0.1f, -MathF.Sin(ang) * 0.6f);
        w.WeaponIK(-1, hand, V(-1f, -0.2f, 0f), dir, Up);
        AxeOffHand(w);
        Trunk(w, -35f * wind * (1 - ext) + 40f * MathF.Sin(ang) * ext, 6f * ext, 8f * MathF.Cos(ang) * ext);
        Legs(w, 0.3f * MathF.Sin(ang) * ext, 0.3f * ext);
    });

    // ---- Daggers (Rogue)
    private static readonly Vector3 DagGuardR = V(-0.20f, 0.78f, 0.30f), DagGuardL = V(0.20f, 0.82f, 0.26f);
    private static void Dagger(PoseWriter w, int side, float k)
    {
        bool r = side < 0;
        var g = r ? DagGuardR : DagGuardL;
        var stab = V(r ? -0.08f : 0.08f, 1.0f, 0.78f);
        var dir = Vector3.Lerp(V(r ? -0.1f : 0.1f, -0.2f, 1f), V(0, 0.05f, 1f), k);
        w.WeaponIK(side, Vector3.Lerp(g, stab, k), r ? V(-1f, -0.6f, -0.2f) : V(1f, -0.6f, -0.2f), dir, Up);
    }
    private static readonly AttackDef DaggerStabs = new("Stabs", 0.6f, 0.4f, 0.2f, 0.2f, (u, w, sk) =>
    {
        float r = Bump(u, 0.0f, 0.18f, 0.42f), l = Bump(u, 0.3f, 0.48f, 0.75f);
        Dagger(w, -1, Whip(u, 0, 0.18f) * (1 - Ramp(u, 0.18f, 0.42f)));
        Dagger(w, 1, Whip(u, 0.3f, 0.48f) * (1 - Ramp(u, 0.48f, 0.75f)));
        Trunk(w, 28f * r - 28f * l, 14f + 8f * (r + l));
        Legs(w, 0.6f * (r - l), 0.45f);
    });
    private static readonly AttackDef DaggerLunge = new("Lunge", 0.85f, 0.55f, 0.36f, 0.9f, (u, w, sk) =>
    {
        float wind = Bump(u, 0, 0.2f, 0.3f), k = Whip(u, 0.2f, 0.42f) * (1f - Ramp(u, 0.6f, 1f));
        Dagger(w, -1, k); Dagger(w, 1, k);
        Trunk(w, 0, 10f - 8f * wind + 30f * k);
        Legs(w, 1f * k, 0.45f + 0.3f * k);
    });
    private static readonly AttackDef DaggerFlurry = new("Flurry", 1.0f, 0.7f, 0.25f, 0.3f, (u, w, sk) =>
    {
        float r1 = Bump(u, 0, 0.1f, 0.24f), l1 = Bump(u, 0.18f, 0.28f, 0.42f), r2 = Bump(u, 0.36f, 0.46f, 0.6f), both = Bump(u, 0.56f, 0.7f, 0.92f);
        Dagger(w, -1, MathF.Max(MathF.Max(r1, r2), both)); Dagger(w, 1, MathF.Max(l1, both));
        Trunk(w, 22f * (r1 + r2) - 22f * l1, 14f + 12f * both);
        Legs(w, 0.4f * (r1 - l1 + r2) + 0.8f * both, 0.45f);
    });

    // ---- Staff (Mage)
    private static readonly Vector3 StaffRest = V(-0.16f, 1.05f, 0.18f), StaffRestDir = V(0.05f, 1f, 0.35f);
    private static void StaffOffHand(PoseWriter w, float weight = 1f) => w.ArmIK(1, w.WeaponPoint(-1, 0.5f), V(1f, -0.2f, 0.5f), weight);
    private static readonly AttackDef StaffBolt = new("Bolt", 0.8f, 0.55f, 0.36f, 0.1f, (u, w, sk) =>
    {
        float wind = Ramp(u, 0, 0.22f) * (1f - Ramp(u, 0.22f, 0.44f)), k = Whip(u, 0.22f, 0.44f) * (1f - Ramp(u, 0.62f, 1f));
        var back = V(-0.3f, 1.15f, -0.15f); var fwd = V(-0.12f, 1.1f, 0.55f);
        var pos = StaffRest + (back - StaffRest) * wind + (fwd - StaffRest) * k;
        var dir = Vector3.Lerp(StaffRestDir, V(0f, 0.15f, 1f), MathF.Max(wind * 0.4f, k));
        w.WeaponIK(-1, pos, V(-1f, -0.2f, -0.3f), dir, V(0, 1, 0));
        StaffOffHand(w, 1f - k);
        w.Hang("armL", 45f * k, 35f * k, 1, -20f * k); w.Hang("foreL", 40f * k, 0, 1); w.Hang("handL", -30f * k, 0, 1);
        Trunk(w, 14f * wind - 20f * k, -4f * wind + 10f * k);
        Legs(w, 0.6f * k, 0.1f);
    });
    private static readonly AttackDef StaffNova = new("Nova", 1.6f, 0.8f, 0.62f, 0f, (u, w, sk) =>
    {
        float raise = Ramp(u, 0, 0.45f) * (1f - Ramp(u, 0.55f, 0.68f)), slam = Whip(u, 0.55f, 0.68f) * (1f - Ramp(u, 0.82f, 1f));
        float circle = Ramp(u, 0.08f, 0.5f) * MathHelper.TwoPi;
        var high = V(-0.1f + 0.2f * MathF.Sin(circle), 1.85f, 0.05f + 0.15f * MathF.Cos(circle));
        var low = V(-0.15f, 0.7f, 0.35f);
        var pos = StaffRest + (high - StaffRest) * raise + (low - StaffRest) * slam;
        var dir = Vector3.Lerp(StaffRestDir, V(0f, 1f, 0f), raise); dir = Vector3.Lerp(dir, V(0f, 1f, -0.1f), slam);
        w.WeaponIK(-1, pos, V(-1f, 0.2f, -0.2f), dir, V(0, 0, 1));
        StaffOffHand(w);
        Trunk(w, 10f * MathF.Sin(circle) * raise, -8f * raise + 18f * slam, 6f * MathF.Cos(circle) * raise);
        Legs(w, 0.3f * slam, 0.05f + 0.4f * slam);
    });
    private static readonly AttackDef StaffBeam = new("Channel", 1.8f, 0.9f, 0.3f, 0f, (u, w, sk) =>
    {
        float wind = Ramp(u, 0, 0.25f), hold = Ramp(u, 0.25f, 0.35f) * (1f - Ramp(u, 0.8f, 1f));
        float shake = MathF.Sin(u * 90f) * 0.01f * hold;
        var pos = Vector3.Lerp(StaffRest, V(-0.1f, 1.2f + shake, 0.5f), wind);
        var dir = Vector3.Lerp(StaffRestDir, V(0f, 0.2f, 1f), wind);
        w.WeaponIK(-1, pos, V(-1f, -0.1f, -0.2f), dir, V(0, 1, 0));
        StaffOffHand(w, 1f - hold);
        w.Hang("armL", 55f * hold, 20f * hold, 1, -20f * hold); w.Hang("foreL", 30f * hold, 0, 1); w.Hang("handL", -25f * hold, 0, 1);
        Trunk(w, -12f * hold, 8f * hold);
        Legs(w, 0.5f * hold, 0.2f * hold);
    });

    // ---- Bow (Ranger)
    private static void BowPose(PoseWriter w, float raise, float draw)
    {
        var carry = V(0.18f, 0.85f, 0.25f); var aim = V(0.12f, 1.38f, 0.62f);
        var bowDir = Vector3.Lerp(V(0.05f, 1f, 0.1f), V(0f, 1f, 0f), raise);
        var bowHand = Vector3.Lerp(carry, aim, raise);
        w.WeaponIK(1, bowHand, V(1f, 0.2f, 0f), bowDir, V(0, 0, 1));
        var nock = bowHand + V(-0.12f, 0.02f, -0.06f);
        var anchor = V(-0.12f, 1.45f, 0.05f);
        w.ArmIK(-1, Vector3.Lerp(nock, anchor, draw), V(-1f, 0.3f, -0.6f));
        w.Hang("handR", 0, 0, -1, 30f * draw);
        Trunk(w, 24f + 14f * raise, 2f - 2f * draw); w.Upright("head", -3f * draw, 0, -26f - 10f * raise);
        Legs(w, 0.3f + 0.2f * raise, 0.08f + 0.05f * draw);
    }
    private static readonly AttackDef BowShot = new("Shot", 0.95f, 0.65f, 0.56f, 0f, (u, w, sk) =>
    {
        float raise = Ramp(u, 0, 0.22f) * (1f - Ramp(u, 0.72f, 1f)), draw = Ramp(u, 0.18f, 0.48f) * (1f - Ramp(u, 0.56f, 0.6f));
        float recoil = Bump(u, 0.56f, 0.61f, 0.74f);
        BowPose(w, raise, draw);
        if (recoil > 0) w.ArmIK(-1, V(-0.35f, 1.5f, -0.15f), V(-1f, 0.3f, -0.6f), recoil);
    });
    private static readonly AttackDef BowFullDraw = new("Full draw", 1.7f, 0.8f, 1.2f, 0f, (u, w, sk) =>
    {
        float raise = Ramp(u, 0, 0.18f) * (1f - Ramp(u, 0.86f, 1f)), draw = Ramp(u, 0.15f, 0.5f) * (1f - Ramp(u, 0.71f, 0.74f));
        float strain = Ramp(u, 0.5f, 0.71f) * (1f - Ramp(u, 0.71f, 0.74f));
        float recoil = Bump(u, 0.71f, 0.76f, 0.9f);
        BowPose(w, raise, draw);
        float tremor = MathF.Sin(u * 140f) * 0.006f * strain;
        if (strain > 0) w.ArmIK(-1, V(-0.14f + tremor, 1.45f + tremor, 0.02f), V(-1f, 0.3f, -0.6f), strain);
        if (recoil > 0) w.ArmIK(-1, V(-0.4f, 1.5f, -0.2f), V(-1f, 0.3f, -0.6f), recoil);
        w.Upright("chest", 2f - 3f * strain, 0, 38f + 4f * strain);
    });
    private static readonly AttackDef BowVolley = new("Volley", 1.45f, 0.9f, 0.3f, 0f, (u, w, sk) =>
    {
        float raise = Ramp(u, 0, 0.15f) * (1f - Ramp(u, 0.88f, 1f));
        float d1 = Bump(u, 0.12f, 0.26f, 0.3f), d2 = Bump(u, 0.36f, 0.5f, 0.54f), d3 = Bump(u, 0.6f, 0.74f, 0.78f);
        float rec = Bump(u, 0.3f, 0.33f, 0.4f) + Bump(u, 0.54f, 0.57f, 0.64f) + Bump(u, 0.78f, 0.81f, 0.88f);
        BowPose(w, raise, MathF.Max(MathF.Max(d1, d2), d3) * 0.85f);
        if (rec > 0) w.ArmIK(-1, V(-0.32f, 1.48f, -0.12f), V(-1f, 0.3f, -0.6f), MathF.Min(1f, rec));
    });

    private static readonly AttackDef Punch = new("Punch", 0.6f, 0.4f, 0.25f, 0.15f, (u, w, sk) =>
    {
        float wind = Bump(u, 0, 0.15f, 0.25f), k = Whip(u, 0.15f, 0.35f) * (1f - Ramp(u, 0.5f, 1f));
        w.ArmIK(-1, Vector3.Lerp(V(-0.3f, 1.1f, -0.1f), V(-0.05f, 1.25f, 0.7f), k), V(-1f, -0.3f, -0.3f));
        w.Hang("armL", 30f, 25f, 1, 0); w.Hang("foreL", 110f, 0, 1);
        Trunk(w, 20f * wind - 25f * k, 6f * k);
        Legs(w, 0.5f * k, 0.15f);
    });
}
