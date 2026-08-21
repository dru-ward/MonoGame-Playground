using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace CharacterModels;

/// <summary>Per-bone local rotations plus a root offset.</summary>
public sealed class Pose
{
    public Quaternion[] Rotations;
    public Vector3 RootOffset;

    public Pose(int boneCount)
    {
        Rotations = new Quaternion[boneCount];
        Reset();
    }

    public void Reset()
    {
        for (int i = 0; i < Rotations.Length; i++) Rotations[i] = Quaternion.Identity;
        RootOffset = Vector3.Zero;
    }

    public void BlendFrom(Pose a, Pose b, float t)
    {
        for (int i = 0; i < Rotations.Length; i++) Rotations[i] = Quaternion.Slerp(a.Rotations[i], b.Rotations[i], t);
        RootOffset = Vector3.Lerp(a.RootOffset, b.RootOffset, t);
    }

    public void ApplyTo(Skeleton skeleton)
    {
        for (int i = 0; i < Rotations.Length; i++) skeleton[i].Rotation = Rotations[i];
        skeleton[0].Translation = RootOffset;
    }
}

/// <summary>
/// Convenience writer that hides the axis conventions. Character faces +Z, its left is +X.
/// "Hanging" bones (arms/legs) point -Y; "upright" bones (spine/neck/head) point +Y; feet point +Z.
/// </summary>
public readonly struct PoseWriter
{
    private readonly Pose _pose;
    private readonly Skeleton _skel;
    public PoseWriter(Pose pose, Skeleton skel) { _pose = pose; _skel = skel; }

    private static float Rad(float deg) => MathHelper.ToRadians(deg);

    /// <summary>Arm/leg style bone. forward = swing toward +Z; outward = abduct away from body; side = +1 left, -1 right.</summary>
    public void Hang(string bone, float forward, float outward = 0, float side = 1, float twist = 0)
    {
        if (!_skel.Has(bone)) return;
        _pose.Rotations[_skel[bone].Index] = Quaternion.CreateFromYawPitchRoll(Rad(twist * side), Rad(-forward), Rad(outward * side));
    }

    /// <summary>Spine-style bone. lean = tilt forward; tilt = toward character's left; twist = yaw (left shoulder back).</summary>
    public void Upright(string bone, float lean, float tilt = 0, float twist = 0)
    {
        if (!_skel.Has(bone)) return;
        _pose.Rotations[_skel[bone].Index] = Quaternion.CreateFromYawPitchRoll(Rad(twist), Rad(lean), Rad(-tilt));
    }

    /// <summary>Foot: toeUp = dorsiflex.</summary>
    public void Foot(string bone, float toeUp, float side = 1, float roll = 0)
    {
        if (!_skel.Has(bone)) return;
        _pose.Rotations[_skel[bone].Index] = Quaternion.CreateFromYawPitchRoll(0, Rad(-toeUp), Rad(roll * side));
    }

    public void Root(Vector3 offset) => _pose.RootOffset = offset;
}

public sealed class Clip
{
    public string Name;
    public Action<float, PoseWriter> Evaluate;
    public Clip(string name, Action<float, PoseWriter> evaluate) { Name = name; Evaluate = evaluate; }
}

/// <summary>Cross-fading clip player.</summary>
public sealed class AnimationPlayer
{
    private readonly Skeleton _skel;
    private readonly Pose _a, _b, _out;
    private Clip? _current, _previous;
    private float _blend = 1f, _time, _prevTime;
    public float BlendDuration = 0.35f;
    public float TimeOffset;
    public Clip? Current => _current;

    public AnimationPlayer(Skeleton skel)
    {
        _skel = skel;
        _a = new Pose(skel.Count); _b = new Pose(skel.Count); _out = new Pose(skel.Count);
    }

    public void Play(Clip clip)
    {
        if (_current == clip) return;
        _previous = _current; _prevTime = _time;
        _current = clip; _blend = _previous == null ? 1f : 0f;
    }

    public void Update(float dt)
    {
        _time += dt; _prevTime += dt;
        if (_blend < 1f) _blend = Math.Min(1f, _blend + dt / BlendDuration);

        _a.Reset();
        _current?.Evaluate(_time + TimeOffset, new PoseWriter(_a, _skel));
        if (_blend < 1f && _previous != null)
        {
            _b.Reset();
            _previous.Evaluate(_prevTime + TimeOffset, new PoseWriter(_b, _skel));
            float s = _blend * _blend * (3 - 2 * _blend);
            _out.BlendFrom(_b, _a, s);
            _out.ApplyTo(_skel);
        }
        else _a.ApplyTo(_skel);
        _skel.Update();
    }
}

/// <summary>Library of procedural clips shared by all characters.</summary>
public static class Clips
{
    private static float Key(float u, params (float t, float v)[] keys)
    {
        if (u <= keys[0].t) return keys[0].v;
        for (int i = 0; i < keys.Length - 1; i++)
        {
            if (u <= keys[i + 1].t)
            {
                float s = (u - keys[i].t) / (keys[i + 1].t - keys[i].t);
                s = s * s * (3 - 2 * s);
                return MathHelper.Lerp(keys[i].v, keys[i + 1].v, s);
            }
        }
        return keys[^1].v;
    }

    public static readonly Clip BindPose = new("Bind pose", (t, w) => { });

    public static readonly Clip Idle = new("Idle", (t, w) =>
    {
        float breath = MathF.Sin(t * 1.6f);
        float sway = MathF.Sin(t * 0.45f);
        w.Root(new Vector3(0.004f * sway, 0.003f * breath, 0));
        w.Upright("spine", 1.0f + 0.8f * breath, 1.2f * sway, 2f * sway);
        w.Upright("chest", 1.5f * breath, 0, -1.5f * sway);
        w.Upright("neck", -2f * breath, 0, 0);
        w.Upright("head", 3f * MathF.Sin(t * 0.7f), 2f * MathF.Sin(t * 0.33f), 9f * MathF.Sin(t * 0.5f));
        for (int s = -1; s <= 1; s += 2)
        {
            string L = s > 0 ? "L" : "R";
            w.Hang("arm" + L, 3f + 2f * sway, 7f + 1.5f * breath, s);
            w.Hang("fore" + L, 14f + 2f * breath, 2f, s);
            w.Hang("hand" + L, -5f, 0, s);
            w.Hang("thigh" + L, 0.5f, 2.5f, s);
            w.Hang("shin" + L, -3f, 0, s);
            w.Hang("clav" + L, 0, 1f * breath, s);
        }
    });

    public static readonly Clip Walk = new("Walk", (t, w) => Locomotion(t, w, 1.55f, 0.0f, 26f, 48f, 20f, 14f, 0.016f, 0f));
    public static readonly Clip Run = new("Run", (t, w) => Locomotion(t, w, 2.5f, 12f, 48f, 85f, 45f, 75f, 0.05f, 0.03f));

    private static void Locomotion(float t, PoseWriter w, float hz, float lean, float stride, float kneeSwing,
                                   float armSwing, float elbow, float bob, float flight)
    {
        float phi = t * MathHelper.TwoPi * hz;
        float s = MathF.Sin(phi), c = MathF.Cos(phi);

        w.Root(new Vector3(0.01f * s, bob * MathF.Cos(2 * phi) - bob * 0.5f + flight * MathF.Max(0, -MathF.Cos(2 * phi)), 0));
        w.Upright("spine", lean * 0.5f, 2f * s, 6f * s);
        w.Upright("chest", lean * 0.5f, -1f * s, 4f * s);
        w.Upright("neck", -lean * 0.4f, 0, -3f * s);
        w.Upright("head", -lean * 0.3f + 1.5f * MathF.Cos(2 * phi), 0, -4f * s);

        for (int side = -1; side <= 1; side += 2)
        {
            string L = side > 0 ? "L" : "R";
            float p = side > 0 ? phi : phi + MathHelper.Pi;
            float sp = MathF.Sin(p), cp = MathF.Cos(p);

            float thigh = stride * sp + lean * 0.3f;
            float knee = 6f + kneeSwing * MathF.Pow(MathF.Max(0, cp), 1.3f) + 10f * MathF.Max(0, -cp) * MathF.Max(0, sp);
            float toeUp = -(thigh - knee) * 0.75f - 18f * MathF.Max(0, -sp) * MathF.Max(0, -cp) + 8f * MathF.Max(0, sp) * MathF.Max(0, cp);
            w.Hang("thigh" + L, thigh, 2f, side);
            w.Hang("shin" + L, -knee, 0, side);
            w.Foot("foot" + L, toeUp, side);

            float arm = -armSwing * sp;
            w.Hang("arm" + L, arm, 6f, side);
            w.Hang("fore" + L, elbow + 0.4f * armSwing * MathF.Max(0, -sp), 3f, side);
            w.Hang("hand" + L, -4f, 0, side);
            w.Hang("clav" + L, 0, 1.5f * sp, side);
        }
    }

    public static readonly Clip Wave = new("Wave", (t, w) =>
    {
        Idle.Evaluate(t, w);
        float u = MathF.Min(1f, t * 1.5f);
        float raise = Key(u, (0, 0), (1, 1));
        float wave = MathF.Sin(t * 9f);
        w.Hang("armR", 15f * raise, 150f * raise + 5f, -1);
        w.Hang("foreR", 5f, -(35f + 22f * wave) * raise, -1);
        w.Hang("handR", 0, -12f * wave * raise, -1);
        w.Upright("head", 2f, -8f * raise, -15f * raise);
        w.Upright("chest", 0, 4f * raise, -6f * raise);
    });

    public static readonly Clip Attack = new("Attack", (t, w) =>
    {
        const float period = 1.4f;
        float u = (t % period) / period;

        float armF = Key(u, (0, 10), (0.25f, -70), (0.42f, -80), (0.58f, 100), (0.8f, 85), (1, 10));
        float armO = Key(u, (0, 10), (0.25f, 85), (0.42f, 95), (0.58f, 30), (0.8f, 25), (1, 10));
        float fore = Key(u, (0, 20), (0.25f, 95), (0.42f, 105), (0.58f, 10), (0.8f, 20), (1, 20));
        float twist = Key(u, (0, 0), (0.3f, -30), (0.45f, -32), (0.6f, 28), (1, 0));
        float lean = Key(u, (0, 2), (0.3f, -6), (0.6f, 14), (1, 2));
        float dip = Key(u, (0, 0), (0.3f, 0.01f), (0.6f, -0.05f), (1, 0));

        w.Root(new Vector3(0, dip, 0));
        w.Upright("spine", lean * 0.5f, 0, twist * 0.5f);
        w.Upright("chest", lean * 0.5f, 0, twist * 0.5f);
        w.Upright("neck", 0, 0, -twist * 0.4f);
        w.Upright("head", -lean * 0.3f, 0, -twist * 0.5f);

        w.Hang("armR", armF, armO, -1, 10);
        w.Hang("foreR", fore, -5, -1);
        w.Hang("handR", -10, 0, -1);
        w.Hang("clavR", 0, 8f * MathF.Max(0, armO - 20) / 75f, -1);

        w.Hang("armL", 25, 35, 1);
        w.Hang("foreL", 70, 10, 1);
        w.Hang("handL", -10, 0, 1);

        w.Hang("thighL", 20, 6, 1);
        w.Hang("shinL", -22, 0, 1);
        w.Foot("footL", 0, 1);
        w.Hang("thighR", -12, 8, -1);
        w.Hang("shinR", -15, 0, -1);
        w.Foot("footR", -5, -1);
    });

    public static readonly Clip Dance = new("Dance", (t, w) =>
    {
        float beat = t * 4.2f;
        float s = MathF.Sin(beat), s2 = MathF.Sin(beat * 0.5f), c2 = MathF.Cos(beat * 0.5f);
        w.Root(new Vector3(0.05f * s2, -0.04f + 0.04f * MathF.Abs(MathF.Cos(beat)), 0));
        w.Upright("spine", 4, 8f * s2, 12f * c2);
        w.Upright("chest", 2, -4f * s2, -8f * c2);
        w.Upright("head", 4f * s, 6f * c2, 10f * s2);
        for (int side = -1; side <= 1; side += 2)
        {
            string L = side > 0 ? "L" : "R";
            float ph = side > 0 ? 0 : MathHelper.Pi;
            float a = MathF.Sin(beat * 0.5f + ph);
            w.Hang("arm" + L, 40f + 30f * a, 60f + 25f * a, side);
            w.Hang("fore" + L, 80f + 30f * MathF.Sin(beat + ph), 20f, side);
            w.Hang("hand" + L, -10f * MathF.Sin(beat + ph), 0, side);
            w.Hang("thigh" + L, 10f + 12f * MathF.Max(0, a), 10f + 10f * MathF.Max(0, a), side);
            w.Hang("shin" + L, -20f - 25f * MathF.Max(0, a), 0, side);
            w.Foot("foot" + L, 8f * MathF.Max(0, a), side);
        }
    });

    public static readonly IReadOnlyList<Clip> All = new[] { BindPose, Idle, Walk, Run, Wave, Attack, Dance };
}
