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
        var m = Matrix.CreateRotationY(Rad(twist * side)) * Matrix.CreateRotationX(Rad(-forward)) * Matrix.CreateRotationZ(Rad(outward * side));
        _pose.Rotations[_skel[bone].Index] = Quaternion.CreateFromRotationMatrix(m);
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
    private readonly Pose _a, _b, _out, _smooth;
    private readonly Quaternion[] _vel;
    private Vector3 _rootVel;
    private readonly float[] _omega;     // per-bone spring frequency (rad/s); 0 = no smoothing
    private Clip? _current, _previous;
    private float _blend = 1f, _time, _prevTime;
    private bool _primed;
    public float BlendDuration = 0.4f;
    public float Damping = 0.72f;
    public float TimeOffset;
    public Clip? Current => _current;

    public AnimationPlayer(Skeleton skel)
    {
        _skel = skel;
        _a = new Pose(skel.Count); _b = new Pose(skel.Count); _out = new Pose(skel.Count); _smooth = new Pose(skel.Count);
        _vel = new Quaternion[skel.Count];
        _omega = new float[skel.Count];
        for (int i = 0; i < skel.Count; i++)
        {
            string n = skel[i].Name;
            // Extremities lag and overshoot a little (follow-through); legs stay crisp so feet read as planted.
            _omega[i] = n.StartsWith("hand") ? MathHelper.TwoPi * 5.5f
                      : n.StartsWith("fore") ? MathHelper.TwoPi * 7f
                      : n.StartsWith("arm") ? MathHelper.TwoPi * 8.5f
                      : n == "head" ? MathHelper.TwoPi * 5f
                      : n == "neck" ? MathHelper.TwoPi * 7f
                      : n.StartsWith("clav") || n == "chest" || n == "spine" ? MathHelper.TwoPi * 9f
                      : 0f;
        }
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
            float s = _blend * _blend * _blend * (_blend * (_blend * 6 - 15) + 10);   // smootherstep
            _out.BlendFrom(_b, _a, s);
        }
        else _out.BlendFrom(_a, _a, 0);

        FollowThrough(dt);
        _smooth.ApplyTo(_skel);
        _skel.Update();
    }

    /// <summary>Damped second-order spring per bone so extremities lag and settle naturally (overlapping action).</summary>
    private void FollowThrough(float dt)
    {
        if (!_primed || dt <= 0)
        {
            for (int i = 0; i < _vel.Length; i++) { _smooth.Rotations[i] = _out.Rotations[i]; _vel[i] = default; }
            _smooth.RootOffset = _out.RootOffset; _rootVel = Vector3.Zero; _primed = true;
            return;
        }
        dt = Math.Min(dt, 1f / 30f);
        for (int i = 0; i < _vel.Length; i++)
        {
            var target = _out.Rotations[i];
            float w = _omega[i];
            if (w <= 0) { _smooth.Rotations[i] = target; continue; }
            var x = _smooth.Rotations[i];
            if (Quaternion.Dot(x, target) < 0) target = -target;        // shortest arc
            var v = _vel[i];
            // Spring on quaternion components (fine for the small per-frame deltas), then renormalise.
            var acc = (target - x) * (w * w) - v * (2 * Damping * w);
            v += acc * dt;
            x += v * dt;
            x.Normalize();
            _vel[i] = v; _smooth.Rotations[i] = x;
        }
        const float rw = MathHelper.TwoPi * 9f;
        var ra = (_out.RootOffset - _smooth.RootOffset) * (rw * rw) - _rootVel * (2 * Damping * rw);
        _rootVel += ra * dt;
        _smooth.RootOffset += _rootVel * dt;
    }
}

/// <summary>Library of procedural clips shared by all characters.</summary>
public static class Clips
{
    /// <summary>Cyclic Catmull-Rom keyframe curve over u in [0,1): continuous velocity, no stop-start at keys.</summary>
    private static float Key(float u, params (float t, float v)[] keys)
    {
        int n = keys.Length - 1;               // last key duplicates the first (t = 1)
        u -= MathF.Floor(u);
        int i = 0;
        while (i < n - 1 && u > keys[i + 1].t) i++;
        float t0 = keys[i].t, t1 = keys[i + 1].t;
        float s = (u - t0) / (t1 - t0);
        float p1 = keys[i].v, p2 = keys[i + 1].v;
        float p0 = i > 0 ? keys[i - 1].v : keys[n - 1].v;
        float p3 = i + 2 <= n ? keys[i + 2].v : keys[1].v;
        float s2 = s * s, s3 = s2 * s;
        return 0.5f * ((2 * p1) + (-p0 + p2) * s + (2 * p0 - 5 * p1 + 4 * p2 - p3) * s2 + (-p0 + 3 * p1 - 3 * p2 + p3) * s3);
    }

    /// <summary>Smooth max(0, x), C1 continuous so joint curves have no kinks.</summary>
    private static float Pos(float x, float k = 0.12f) => 0.5f * (x + MathF.Sqrt(x * x + k * k));
    private static float Smooth01(float x) { x = MathHelper.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

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

    public static readonly Clip Walk = new("Walk", (t, w) => Locomotion(t, w, 1.3f, 0.0f, 22f, 45f, 18f, 14f, 0.014f, 0f));
    public static readonly Clip Run = new("Run", (t, w) => Locomotion(t, w, 2.3f, 12f, 38f, 85f, 42f, 75f, 0.045f, 0.03f));

    private static void Locomotion(float t, PoseWriter w, float hz, float lean, float stride, float kneeSwing,
                                   float armSwing, float elbow, float bob, float flight)
    {
        float phi = t * MathHelper.TwoPi * hz;
        float s = MathF.Sin(phi), c = MathF.Cos(phi);

        w.Root(new Vector3(0.01f * s, bob * MathF.Cos(2 * phi) - bob * 0.5f + flight * Pos(-MathF.Cos(2 * phi), 0.3f), 0));
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
            float swing = Pos(cp, 0.25f);
            float knee = 6f + kneeSwing * swing * swing / (swing + 0.35f) * 1.35f + 10f * Pos(-cp, 0.25f) * Pos(sp, 0.25f);
            float toeUp = -(thigh - knee) * 0.75f - 18f * Pos(-sp, 0.3f) * Pos(-cp, 0.3f) + 8f * Pos(sp, 0.3f) * Pos(cp, 0.3f);
            w.Hang("thigh" + L, thigh, 2f, side);
            w.Hang("shin" + L, -knee, 0, side);
            w.Foot("foot" + L, toeUp, side);

            float arm = -armSwing * sp;
            w.Hang("arm" + L, arm, 6f, side);
            w.Hang("fore" + L, elbow + 0.4f * armSwing * Pos(-sp, 0.3f), 3f, side);
            w.Hang("hand" + L, -4f, 0, side);
            w.Hang("clav" + L, 0, 1.5f * sp, side);
        }
    }

    public static readonly Clip Wave = new("Wave", (t, w) =>
    {
        Idle.Evaluate(t, w);
        float raise = Smooth01(t * 1.3f);
        float wave = MathF.Sin(t * 8f) * Smooth01((t - 0.5f) * 1.5f);
        // Upper arm out to the side (~100 deg) and a little forward; elbow flexes ~85 deg so the hand points up.
        // The side-to-side wave is a twist about the upper arm's own axis, which swings the bent forearm in a cone.
        w.Hang("armR", 18f * raise, 100f * raise + 7f, -1, 30f * wave);
        w.Hang("foreR", 14f - 8f * wave * raise, 85f * raise, -1);
        w.Hang("handR", 0, 10f * wave * raise, -1);
        w.Upright("head", 2f, -6f * raise, -12f * raise);
        w.Upright("chest", 0, 4f * raise, -6f * raise);
    });

    public static readonly Clip Attack = new("Attack", (t, w) =>
    {
        const float period = 1.4f;
        float u = (t % period) / period;

        float armF = Key(u, (0, 10), (0.22f, -60), (0.40f, -80), (0.52f, 60), (0.60f, 105), (0.75f, 90), (0.9f, 35), (1, 10));
        float armO = Key(u, (0, 10), (0.22f, 75), (0.40f, 95), (0.52f, 55), (0.60f, 30), (0.75f, 25), (0.9f, 15), (1, 10));
        float fore = Key(u, (0, 20), (0.22f, 85), (0.40f, 105), (0.52f, 40), (0.60f, 8), (0.75f, 18), (0.9f, 22), (1, 20));
        float twist = Key(u, (0, 0), (0.25f, -26), (0.42f, -32), (0.55f, 10), (0.62f, 28), (0.8f, 20), (1, 0));
        float lean = Key(u, (0, 2), (0.25f, -5), (0.42f, -7), (0.60f, 14), (0.78f, 10), (1, 2));
        float dip = Key(u, (0, 0), (0.25f, 0.012f), (0.42f, 0.015f), (0.60f, -0.05f), (0.8f, -0.03f), (1, 0));
        float step = Key(u, (0, 0), (0.3f, -0.3f), (0.6f, 1f), (0.85f, 0.8f), (1, 0));

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

        w.Hang("thighL", 20 + 12 * step, 6, 1);
        w.Hang("shinL", -22 - 14 * step, 0, 1);
        w.Foot("footL", 2 * step, 1);
        w.Hang("thighR", -12 - 8 * step, 8, -1);
        w.Hang("shinR", -15 - 6 * step, 0, -1);
        w.Foot("footR", -5 - 10 * step, -1);
    });

    public static readonly Clip Dance = new("Dance", (t, w) =>
    {
        float beat = t * 4.2f;
        float s = MathF.Sin(beat), s2 = MathF.Sin(beat * 0.5f), c2 = MathF.Cos(beat * 0.5f);
        float bounce = 0.5f + 0.5f * MathF.Cos(2 * beat);
        w.Root(new Vector3(0.05f * s2, -0.045f + 0.045f * bounce, 0));
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
            float lift = Pos(a, 0.3f);
            w.Hang("thigh" + L, 10f + 12f * lift, 10f + 10f * lift, side);
            w.Hang("shin" + L, -20f - 25f * lift, 0, side);
            w.Foot("foot" + L, 8f * lift, side);
        }
    });

    public static readonly IReadOnlyList<Clip> All = new[] { BindPose, Idle, Walk, Run, Wave, Attack, Dance };
}
