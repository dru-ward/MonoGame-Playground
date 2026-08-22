using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace CharacterModels;

public enum AttackKind { Light, Heavy, Special }

/// <summary>
/// Per-weapon combat: a stance overlay (how the weapon is held and how the body moves while it is drawn) and
/// three attacks (light / heavy / special) with distinct timing, body mechanics and root motion.
///
/// Principles baked in (see the monogame-weapon-combat skill):
///   * anticipation 0.1–0.2 s (light) / 0.3–0.45 s (heavy), peak velocity at ~60–70 % of the swing, recovery ≈ 50 % of the
///     cycle so a chain can start at ~70 % of the previous swing;
///   * power comes from the trunk: hips lead, shoulders follow, and the body keeps rotating past the arm before re-centring;
///   * two-handed weapons keep both hands on the grip (off-hand IK'd to the haft); dual wield alternates and the idle
///     blade stays in guard; bows nock–draw–release with a post-release watch; staves telegraph with sweeping gestures.
/// </summary>
public static class Combat
{
    // ------------------------------------------------------------------ helpers

    /// <summary>Smooth unit bump: 0 at a, 1 at peak, 0 at b (C1 at both ends).</summary>
    private static float Bump(float u, float a, float peak, float b)
    {
        if (u <= a || u >= b) return 0f;
        float x = u < peak ? (u - a) / (peak - a) : 1f - (u - peak) / (b - peak);
        return x * x * (3f - 2f * x);
    }
    /// <summary>Smooth step from 0 at a to 1 at b, held.</summary>
    private static float Ramp(float u, float a, float b) => Clips.Smooth01((u - a) / (b - a));
    /// <summary>Swing profile: slow wind-up then a whip through the strike with peak velocity at ~65 % of [a,b]; 0→1.</summary>
    private static float Whip(float u, float a, float b)
    {
        float x = MathHelper.Clamp((u - a) / (b - a), 0, 1);
        // Clamp the inner term: at x == 1 rounding makes it slightly negative and Pow(negative, 1.8) is NaN,
        // which then poisons every bone of the skeleton (the character vanishes).
        float tail = MathHelper.Clamp(1f - (x - 0.65f) / 0.35f, 0f, 1f);
        return x < 0.65f ? 0.5f * MathF.Pow(x / 0.65f, 2.2f) : 0.5f + 0.5f * (1f - MathF.Pow(tail, 1.8f));
    }

    private static void TwoHandedOffHand(PoseWriter w, int side, Vector3 local, Vector3 hint, float weight)
    {
        // The off-hand follows a point on the weapon (character space) via IK so a two-handed grip never separates.
        if (weight <= 0) return;
        w.ArmIK(side, local, hint, weight);
    }

    // ------------------------------------------------------------------ stances

    /// <summary>Applies the drawn-weapon stance over whatever locomotion pose was just written. amount 0..1 (DrawBlend × not-attacking).</summary>
    public static void Stance(Weapon weapon, float amount, float speedNorm, float t, PoseWriter w, Skeleton skel)
    {
        if (amount <= 0.001f) return;
        float a = amount;
        float moving = Clips.Smooth01(speedNorm / 0.7f);
        switch (weapon)
        {
            case Weapon.Sword:
                // Knight: sword low at the hip, point forward; shield arm raised across the chest, left foot leads.
                w.Hang("armR", 18f * a, 18f * a, -1, 15f * a);
                w.Hang("foreR", 55f * a, -10f * a, -1);
                w.Hang("handR", -5f * a, 0, -1, -20f * a);
                w.Hang("armL", 38f * a, 42f * a, 1, 35f * a);
                w.Hang("foreL", 95f * a, 0f, 1);
                w.Hang("handL", 0, 0, 1, 20f * a);
                w.Upright("chest", 4f * a, 0, 14f * a);
                w.Upright("spine", 3f * a, 0, 6f * a);
                w.Upright("head", 0, 0, -16f * a);
                if (moving < 0.5f) { w.Hang("thighL", 8f * a, 4f * a, 1); w.Hang("shinL", -12f * a, 0, 1); w.Hang("thighR", -4f * a, 6f * a, -1); }
                break;
            case Weapon.Axe:
                // Barbarian: two-handed, axe held across the body at waist height, head out to the right, weight back.
                w.Hang("armR", 30f * a, 28f * a, -1, 10f * a);
                w.Hang("foreR", 70f * a, -15f * a, -1);
                w.Hang("handR", -8f * a, 0, -1, -15f * a);
                w.Upright("chest", -3f * a, 0, 10f * a);
                w.Upright("spine", -2f * a, 0, 4f * a);
                w.Upright("head", 2f * a, 0, -10f * a);
                if (moving < 0.5f) { w.Hang("thighL", 6f * a, 8f * a, 1); w.Hang("shinL", -10f * a, 0, 1); w.Hang("thighR", -6f * a, 10f * a, -1); w.Hang("shinR", -8f * a, 0, -1); }
                TwoHandedOffHand(w, 1, w.PositionOf("weaponR") + Vector3.TransformNormal(new Vector3(0, -0.38f, 0), w.WorldOf(skel["weaponR"].Index)), new Vector3(1f, -0.5f, 0.6f), a);
                break;
            case Weapon.Daggers:
                // Rogue: low crouch, blades reversed along the forearms, elbows out, constant small weight shifts.
                float bob = MathF.Sin(t * 2.1f) * 0.5f + 0.5f;
                w.Root(new Vector3(0, -0.06f * a, 0));
                w.Upright("hips", 6f * a, 0, 0);
                w.Upright("spine", 10f * a, 0, 8f * a * MathF.Sin(t * 1.3f));
                w.Upright("chest", 6f * a, 0, 0);
                w.Upright("head", -8f * a, 0, 0);
                for (int s = -1; s <= 1; s += 2)
                {
                    string L = s > 0 ? "L" : "R";
                    w.Hang(BoneNames.Of("arm", L), 15f * a + 6f * a * bob, 40f * a, s, 30f * a);
                    w.Hang(BoneNames.Of("fore", L), 110f * a, 5f * a, s);
                    w.Hang(BoneNames.Of("hand", L), 10f * a, 0, s, -25f * a);
                }
                if (moving < 0.5f) { w.Hang("thighL", 18f * a, 10f * a, 1); w.Hang("shinL", -35f * a, 0, 1); w.Hang("thighR", 12f * a, 10f * a, -1); w.Hang("shinR", -30f * a, 0, -1); w.Foot("footL", 8f * a, 1); w.Foot("footR", 6f * a, -1); }
                break;
            case Weapon.Staff:
                // Mage: staff planted forward and angled, both hands on it, upright and still.
                w.Hang("armR", 35f * a, 10f * a, -1, -10f * a);
                w.Hang("foreR", 25f * a, 0, -1);
                w.Hang("handR", 10f * a, 0, -1, 10f * a);
                w.Upright("chest", -2f * a, 0, -6f * a);
                w.Upright("head", -2f * a, 0, 6f * a);
                TwoHandedOffHand(w, 1, w.PositionOf("weaponR") + Vector3.TransformNormal(new Vector3(0, -0.45f, 0), w.WorldOf(skel["weaponR"].Index)), new Vector3(1f, -0.3f, 0.5f), a);
                break;
            case Weapon.Bow:
                // Ranger: bow in the left hand held low and across, right hand resting by the string, side-on to the target.
                w.Hang("armL", 20f * a, 25f * a, 1, 20f * a);
                w.Hang("foreL", 30f * a, 0, 1);
                w.Hang("handL", 0, 0, 1, 30f * a);
                w.Hang("armR", 10f * a, 20f * a, -1, 0);
                w.Hang("foreR", 80f * a, -10f * a, -1);
                w.Hang("handR", 0, 0, -1, 20f * a);
                w.Upright("chest", 2f * a, 0, 18f * a);
                w.Upright("spine", 0, 0, 8f * a);
                w.Upright("head", 0, 0, -22f * a);
                w.WeaponTilt("weaponL", -35f * a);
                break;
        }
    }

    /// <summary>Movement modifiers while the weapon is drawn: (speed multiplier, stride shortening).</summary>
    public static (float speed, float stride) Locomotion(Weapon weapon) => weapon switch
    {
        Weapon.Sword => (0.85f, 0.85f),      // guard up, shorter steps
        Weapon.Axe => (0.8f, 0.9f),          // heavy, deliberate
        Weapon.Daggers => (1.05f, 0.8f),     // quick, low, short steps
        Weapon.Staff => (0.8f, 0.95f),       // unhurried
        Weapon.Bow => (0.9f, 0.9f),
        _ => (1f, 1f)
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

    // Common trunk mechanics: hips lead the rotation, shoulders follow, head stays on target.
    private static void Trunk(PoseWriter w, float twist, float lean, float tilt = 0)
    {
        w.Upright("hips", lean * 0.25f, tilt * 0.3f, twist * 0.45f);
        w.Upright("spine", lean * 0.35f, tilt * 0.3f, twist * 0.35f);
        w.Upright("chest", lean * 0.4f, tilt * 0.4f, twist * 0.4f);
        w.Upright("neck", -lean * 0.3f, 0, -twist * 0.45f);
        w.Upright("head", -lean * 0.4f, 0, -twist * 0.6f);
    }
    private static void Legs(PoseWriter w, float step, float crouch)
    {
        // step: +1 left foot forward lunge; crouch 0..1.
        w.Hang("thighL", 18f * step + 12f * crouch, 6, 1);
        w.Hang("shinL", -20f * step - 22f * crouch, 0, 1);
        w.Foot("footL", 4f * step, 1);
        w.Hang("thighR", -12f * step + 10f * crouch, 8, -1);
        w.Hang("shinR", -12f * step - 20f * crouch, 0, -1);
        w.Foot("footR", -6f * step, -1, 0, 18f * MathF.Max(0, step));
    }

    // ---- Sword (Knight): one-handed, fast, wrist snap; shield arm stays up.
    private static readonly AttackDef SwordCut = new("Cut", 0.7f, 0.5f, 0.3f, 0.25f, (u, w, sk) =>
    {
        float wind = Bump(u, 0, 0.2f, 0.32f), swing = Whip(u, 0.18f, 0.42f), rec = Ramp(u, 0.55f, 1f);
        float s = swing * (1f - rec);
        Trunk(w, -28f * wind + 38f * s, 4f * s, 0);
        w.Hang("armR", 10f + 35f * wind - 20f * s + 70f * s, 70f * wind + 20f * (1 - s) * (1 - wind) + 10f, -1, 40f * wind - 30f * s);
        w.Hang("foreR", 70f * wind + 20f, -10f, -1);
        w.Hang("handR", -10f - 25f * s, 0, -1, -35f * s);      // wrist snap through the cut
        w.Hang("armL", 38f, 42f, 1, 35f); w.Hang("foreL", 95f, 0, 1);
        Legs(w, 0.6f * s, 0.15f);
        w.Root(new Vector3(0, -0.02f * s, 0));
    });
    private static readonly AttackDef SwordOverhead = new("Overhead", 1.1f, 0.7f, 0.45f, 0.35f, (u, w, sk) =>
    {
        float wind = Ramp(u, 0, 0.32f) * (1f - Ramp(u, 0.36f, 0.5f)), chop = Whip(u, 0.36f, 0.52f), rec = Ramp(u, 0.7f, 1f);
        float c = chop * (1f - rec);
        Trunk(w, -12f * wind + 10f * c, -10f * wind + 22f * c, 0);
        w.Hang("armR", -40f * wind + 150f * wind - 10f + 70f * c, 20f + 25f * wind, -1, 0);   // raised high behind, then down
        w.Hang("foreR", 90f * wind + 10f - 5f * c, 0, -1);
        w.Hang("handR", -20f * c, 0, -1, 0);
        w.Hang("armL", 38f, 42f, 1, 35f); w.Hang("foreL", 95f, 0, 1);
        Legs(w, 0.9f * c, 0.1f + 0.35f * c);
        w.Root(new Vector3(0, 0.02f * wind - 0.08f * c, 0));
    });
    private static readonly AttackDef ShieldBash = new("Shield bash", 0.8f, 0.55f, 0.3f, 0.45f, (u, w, sk) =>
    {
        float wind = Bump(u, 0, 0.18f, 0.3f), bash = Whip(u, 0.18f, 0.38f), rec = Ramp(u, 0.55f, 1f);
        float b = bash * (1f - rec);
        Trunk(w, 20f * wind - 30f * b, -4f * wind + 14f * b, 0);
        w.Hang("armL", 20f * wind + 60f * b + 20f, 30f + 10f * b, 1, 35f);
        w.Hang("foreL", 95f - 50f * b, 0, 1);
        w.Hang("armR", 18f, 18f, -1, 15f); w.Hang("foreR", 55f, -10f, -1);
        Legs(w, -0.7f * b, 0.2f);
    });

    // ---- Axe (Barbarian): two-handed, body-driven, long recovery, overshoot past the target.
    private static void AxeOffHand(PoseWriter w, Skeleton sk, float weight)
        => TwoHandedOffHand(w, 1, w.PositionOf("weaponR") + Vector3.TransformNormal(new Vector3(0, -0.38f, 0), w.WorldOf(sk["weaponR"].Index)), new Vector3(1f, -0.5f, 0.6f), weight);
    private static readonly AttackDef AxeSweep = new("Sweep", 1.1f, 0.75f, 0.42f, 0.3f, (u, w, sk) =>
    {
        float wind = Ramp(u, 0, 0.3f) * (1f - Ramp(u, 0.32f, 0.46f)), swing = Whip(u, 0.3f, 0.5f), over = Bump(u, 0.46f, 0.6f, 0.8f), rec = Ramp(u, 0.62f, 1f);
        float s = swing * (1f - rec);
        Trunk(w, -45f * wind + 55f * s + 12f * over, 6f * s, -6f * wind + 8f * s);
        w.Hang("armR", 20f + 30f * wind + 40f * s, 45f * wind + 40f * s + 15f, -1, 20f * wind - 20f * s);
        w.Hang("foreR", 60f * wind + 25f, -10f, -1);
        w.Hang("handR", -10f, 0, -1, -20f * s);
        Legs(w, 0.8f * s, 0.2f + 0.1f * s);
        w.Root(new Vector3(0, -0.03f * s, 0));
        AxeOffHand(w, sk, 1f);
    });
    private static readonly AttackDef AxeSmash = new("Smash", 1.5f, 0.85f, 0.5f, 0.5f, (u, w, sk) =>
    {
        float wind = Ramp(u, 0, 0.38f) * (1f - Ramp(u, 0.42f, 0.56f)), smash = Whip(u, 0.42f, 0.58f), rec = Ramp(u, 0.75f, 1f);
        float c = smash * (1f - rec);
        Trunk(w, -10f * wind + 8f * c, -16f * wind + 30f * c, 0);
        w.Hang("armR", 165f * wind - 5f + 60f * c, 25f + 10f * wind, -1, -10f * wind);
        w.Hang("foreR", 70f * wind + 10f, 0, -1);
        w.Hang("handR", -15f * c, 0, -1, 0);
        Legs(w, 1f * c, 0.1f + 0.5f * c);
        w.Root(new Vector3(0, 0.05f * wind - 0.12f * c, 0));
        AxeOffHand(w, sk, 1f);
    });
    private static readonly AttackDef AxeSpin = new("Whirlwind", 1.6f, 0.9f, 0.55f, 0.2f, (u, w, sk) =>
    {
        float wind = Ramp(u, 0, 0.25f), spin = Ramp(u, 0.25f, 0.85f), rec = Ramp(u, 0.85f, 1f);
        float yaw = spin * 360f * (1f - rec) + 360f * rec;     // full turn carried by the trunk (the game adds root yaw)
        float ext = Bump(u, 0.2f, 0.55f, 0.9f);
        Trunk(w, -40f * wind * (1 - spin), 8f * ext, 0);
        w.Hang("armR", 40f + 50f * ext, 60f * ext + 20f, -1, -30f * ext);
        w.Hang("foreR", 40f - 30f * ext + 40f * wind * (1 - spin), 0, -1);
        Legs(w, 0, 0.35f * ext);
        w.Root(new Vector3(0, -0.05f * ext, 0));
        AxeOffHand(w, sk, 1f);
    });

    // ---- Daggers (Rogue): alternating, fast, the idle blade stays in guard.
    private static void DaggerGuard(PoseWriter w, int s)
    {
        string L = s > 0 ? "L" : "R";
        w.Hang(BoneNames.Of("arm", L), 15f, 40f, s, 30f); w.Hang(BoneNames.Of("fore", L), 110f, 5f, s); w.Hang(BoneNames.Of("hand", L), 10f, 0, s, -25f);
    }
    private static void DaggerStab(PoseWriter w, int s, float k)
    {
        string L = s > 0 ? "L" : "R";
        w.Hang(BoneNames.Of("arm", L), 15f + 70f * k, 40f - 30f * k, s, 30f - 40f * k);
        w.Hang(BoneNames.Of("fore", L), 110f - 95f * k, 5f, s);
        w.Hang(BoneNames.Of("hand", L), 10f - 20f * k, 0, s, -25f + 25f * k);
    }
    private static readonly AttackDef DaggerStabs = new("Stabs", 0.6f, 0.4f, 0.2f, 0.2f, (u, w, sk) =>
    {
        float r = Bump(u, 0.02f, 0.2f, 0.42f), l = Bump(u, 0.32f, 0.5f, 0.75f);
        Trunk(w, 25f * r - 25f * l, 10f + 6f * (r + l), 0);
        DaggerStab(w, -1, r); DaggerStab(w, 1, l);
        w.Root(new Vector3(0, -0.06f, 0)); w.Upright("hips", 6f, 0, 0);
        Legs(w, 0.5f * (r - l), 0.45f);
    });
    private static readonly AttackDef DaggerLunge = new("Lunge", 0.8f, 0.55f, 0.35f, 0.9f, (u, w, sk) =>
    {
        float wind = Bump(u, 0, 0.2f, 0.3f), lunge = Whip(u, 0.2f, 0.42f), rec = Ramp(u, 0.6f, 1f);
        float k = lunge * (1f - rec);
        Trunk(w, 0, -8f * wind + 28f * k, 0);
        DaggerStab(w, -1, k); DaggerStab(w, 1, k);
        w.Root(new Vector3(0, -0.06f - 0.1f * k, 0)); w.Upright("hips", 6f + 10f * k, 0, 0);
        Legs(w, 1f * k, 0.5f + 0.3f * k);
    });
    private static readonly AttackDef DaggerFlurry = new("Flurry", 1.0f, 0.7f, 0.25f, 0.3f, (u, w, sk) =>
    {
        float r1 = Bump(u, 0, 0.12f, 0.26f), l1 = Bump(u, 0.18f, 0.3f, 0.44f), r2 = Bump(u, 0.36f, 0.48f, 0.62f), both = Bump(u, 0.56f, 0.7f, 0.92f);
        float r = MathF.Max(MathF.Max(r1, r2), both), l = MathF.Max(l1, both);
        Trunk(w, 20f * (r1 + r2) - 20f * l1, 10f + 12f * both, 0);
        DaggerStab(w, -1, r); DaggerStab(w, 1, l);
        w.Root(new Vector3(0, -0.06f - 0.04f * both, 0)); w.Upright("hips", 6f, 0, 0);
        Legs(w, 0.4f * (r1 - l1 + r2) + 0.8f * both, 0.45f);
    });

    // ---- Staff (Mage): sweeping, telegraphed; staff tip is the VFX point.
    private static void StaffOffHand(PoseWriter w, Skeleton sk, float weight)
        => TwoHandedOffHand(w, 1, w.PositionOf("weaponR") + Vector3.TransformNormal(new Vector3(0, -0.45f, 0), w.WorldOf(sk["weaponR"].Index)), new Vector3(1f, -0.3f, 0.5f), weight);
    private static readonly AttackDef StaffBolt = new("Bolt", 0.8f, 0.55f, 0.35f, 0.1f, (u, w, sk) =>
    {
        float wind = Bump(u, 0, 0.22f, 0.34f), thrust = Whip(u, 0.22f, 0.42f), rec = Ramp(u, 0.6f, 1f);
        float k = thrust * (1f - rec);
        Trunk(w, 12f * wind - 18f * k, -4f * wind + 10f * k, 0);
        w.Hang("armR", 35f - 20f * wind + 70f * k, 10f + 10f * wind, -1, -10f);
        w.Hang("foreR", 25f + 50f * wind - 25f * k, 0, -1);
        w.Hang("handR", 10f - 20f * k, 0, -1, 10f);
        Legs(w, 0.5f * k, 0.1f);
        StaffOffHand(w, sk, 1f - 0.6f * k);
        // Free hand opens toward the target on release.
        w.Hang("armL", 40f * k, 30f * k, 1, 0); w.Hang("foreL", 60f * k, 0, 1);
    });
    private static readonly AttackDef StaffNova = new("Nova", 1.6f, 0.8f, 0.62f, 0f, (u, w, sk) =>
    {
        float raise = Ramp(u, 0, 0.45f) * (1f - Ramp(u, 0.55f, 0.7f)), slam = Whip(u, 0.55f, 0.68f), rec = Ramp(u, 0.8f, 1f);
        float k = slam * (1f - rec);
        float circle = Ramp(u, 0.1f, 0.5f) * MathHelper.TwoPi;
        Trunk(w, 12f * MathF.Sin(circle) * raise, -10f * raise + 18f * k, 6f * MathF.Cos(circle) * raise);
        w.Hang("armR", 35f + 130f * raise + 40f * k - 30f * raise * MathF.Cos(circle) * 0.3f, 10f + 30f * raise * MathF.Sin(circle), -1, -10f);
        w.Hang("foreR", 25f + 20f * raise - 15f * k, 0, -1);
        w.Hang("armL", 30f + 120f * raise + 30f * k, 30f * raise, 1, 0); w.Hang("foreL", 30f + 10f * raise, 0, 1);
        Legs(w, 0.3f * k, 0.1f + 0.4f * k);
        w.Root(new Vector3(0, 0.03f * raise - 0.1f * k, 0));
    });
    private static readonly AttackDef StaffBeam = new("Channel", 1.8f, 0.9f, 0.3f, 0f, (u, w, sk) =>
    {
        float wind = Ramp(u, 0, 0.25f), hold = Ramp(u, 0.25f, 0.35f) * (1f - Ramp(u, 0.8f, 1f));
        float shake = MathF.Sin(u * 90f) * 2f * hold;
        Trunk(w, -14f * hold, 6f * hold + shake * 0.3f, 0);
        w.Hang("armR", 35f + 50f * wind + shake, 10f + 5f * wind, -1, -10f);
        w.Hang("foreR", 25f - 15f * wind, 0, -1);
        w.Hang("armL", 60f * hold, 20f * hold, 1, -10f * hold); w.Hang("foreL", 30f - 10f * hold, 0, 1); w.Hang("handL", -20f * hold, 0, 1);
        Legs(w, 0.4f * hold, 0.25f * hold);
        w.Root(new Vector3(0, -0.03f * hold, 0));
    });

    // ---- Bow (Ranger): nock – draw – hold – release – watch. The string hand moves to the cheek; the bow arm extends.
    private static void BowPose(PoseWriter w, float draw, float raise)
    {
        // Bow arm: out toward the target, rotated with the torso side-on. String arm: to the anchor at the cheek as draw→1.
        w.Hang("armL", 20f + 60f * raise, 25f + 15f * raise, 1, 20f);
        w.Hang("foreL", 30f - 28f * raise, 0, 1);
        w.Hang("handL", 0, 0, 1, 30f);
        w.Hang("armR", 10f + 40f * raise + 10f * draw, 20f + 45f * raise, -1, 0);
        w.Hang("foreR", 80f + 40f * raise - 5f * draw, -10f, -1);
        w.Hang("handR", 0, 0, -1, 20f + 20f * draw);
        w.Upright("chest", 2f, 0, 18f + 14f * raise);
        w.Upright("spine", 0, 0, 8f + 4f * raise);
        w.Upright("head", -2f * draw, 0, -22f - 12f * raise);
        w.Hang("clavR", 0, 6f * draw, -1);
        // Present the bow: as the arm comes up the bow rotates to stand perpendicular to it, limbs vertical.
        w.WeaponTilt("weaponL", -35f - 55f * raise);
    }
    private static readonly AttackDef BowShot = new("Shot", 0.9f, 0.65f, 0.55f, 0f, (u, w, sk) =>
    {
        float raise = Ramp(u, 0, 0.2f) * (1f - Ramp(u, 0.7f, 1f)), draw = Ramp(u, 0.15f, 0.45f) * (1f - Ramp(u, 0.55f, 0.6f));
        float recoil = Bump(u, 0.55f, 0.6f, 0.72f);
        BowPose(w, draw, raise);
        w.Hang("armR", 10f + 40f * raise + 10f * draw + 25f * recoil, 20f + 45f * raise + 10f * recoil, -1, 0);   // string hand flies back on release
        Legs(w, 0.35f * raise, 0.1f);
        w.Upright("head", -2f * draw + 3f * recoil, 0, -22f - 12f * raise);
    });
    private static readonly AttackDef BowFullDraw = new("Full draw", 1.6f, 0.8f, 1.15f, 0f, (u, w, sk) =>
    {
        float raise = Ramp(u, 0, 0.18f) * (1f - Ramp(u, 0.85f, 1f)), draw = Ramp(u, 0.15f, 0.5f) * (1f - Ramp(u, 0.72f, 0.76f));
        float strain = Ramp(u, 0.5f, 0.72f) * (1f - Ramp(u, 0.72f, 0.76f));
        float recoil = Bump(u, 0.72f, 0.77f, 0.9f);
        BowPose(w, draw, raise);
        float tremor = MathF.Sin(u * 140f) * 1.2f * strain;
        w.Hang("armR", 10f + 40f * raise + 10f * draw + 4f * strain + tremor + 30f * recoil, 20f + 45f * raise + 12f * recoil, -1, 0);
        w.Upright("chest", 2f - 3f * strain, 0, 18f + 14f * raise + 4f * strain);
        Legs(w, 0.4f * raise, 0.15f + 0.1f * strain);
        w.Root(new Vector3(0, -0.02f * strain, 0));
    });
    private static readonly AttackDef BowVolley = new("Volley", 1.4f, 0.9f, 0.3f, 0f, (u, w, sk) =>
    {
        float raise = Ramp(u, 0, 0.15f) * (1f - Ramp(u, 0.88f, 1f));
        // Three quick shots: draw short, release, nock again.
        float d1 = Bump(u, 0.12f, 0.26f, 0.3f), d2 = Bump(u, 0.36f, 0.5f, 0.54f), d3 = Bump(u, 0.6f, 0.74f, 0.78f);
        float draw = MathF.Max(MathF.Max(d1, d2), d3) * 0.8f;
        float rec = Bump(u, 0.3f, 0.32f, 0.38f) + Bump(u, 0.54f, 0.56f, 0.62f) + Bump(u, 0.78f, 0.8f, 0.86f);
        BowPose(w, draw, raise);
        w.Hang("armR", 10f + 40f * raise + 10f * draw + 20f * rec, 20f + 45f * raise + 8f * rec, -1, 0);
        Legs(w, 0.3f * raise, 0.1f);
    });

    private static readonly AttackDef Punch = new("Punch", 0.6f, 0.4f, 0.25f, 0.15f, (u, w, sk) =>
    {
        float wind = Bump(u, 0, 0.15f, 0.25f), hit = Whip(u, 0.15f, 0.35f), rec = Ramp(u, 0.5f, 1f);
        float k = hit * (1f - rec);
        Trunk(w, 20f * wind - 25f * k, 6f * k, 0);
        w.Hang("armR", 40f * wind + 90f * k, 20f, -1, 0); w.Hang("foreR", 110f * wind + 110f * (1 - k) * (1 - wind), 0, -1);
        w.Hang("armL", 30f, 25f, 1, 0); w.Hang("foreL", 110f, 0, 1);
        Legs(w, 0.5f * k, 0.15f);
    });
}
