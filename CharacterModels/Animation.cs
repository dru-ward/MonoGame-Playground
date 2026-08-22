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

    /// <summary>Arm/leg style bone. forward = swing toward +Z; outward = abduct away from body; side = +1 left, -1 right; twist = about the bone's own axis.</summary>
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

    /// <summary>Foot: toeUp = dorsiflex (relative to shin). toeBend bends the toe bone up (push-off).</summary>
    public void Foot(string bone, float toeUp, float side = 1, float roll = 0, float toeBend = 0)
    {
        if (!_skel.Has(bone)) return;
        _pose.Rotations[_skel[bone].Index] = Quaternion.CreateFromYawPitchRoll(0, Rad(-toeUp), Rad(roll * side));
        string toe = BoneNames.Toe(bone);
        if (_skel.Has(toe)) _pose.Rotations[_skel[toe].Index] = Quaternion.CreateFromYawPitchRoll(0, Rad(-toeBend), 0);
    }

    public void Root(Vector3 offset) => _pose.RootOffset = offset;

    /// <summary>Blends the pose written so far toward another pose (t = 1 replaces it).</summary>
    public void BlendToward(Pose other, float t) => _pose.BlendFrom(_pose, other, t);

    /// <summary>Character-space matrix of a bone under the pose written so far.</summary>
    public Matrix WorldOf(int index)
    {
        var b = _skel[index];
        var local = Matrix.CreateFromQuaternion(_pose.Rotations[index]) * Matrix.CreateFromQuaternion(b.BindRotation)
                  * Matrix.CreateTranslation(b.LocalOffset + (index == 0 ? _pose.RootOffset : Vector3.Zero));
        return b.Parent >= 0 ? local * WorldOf(b.Parent) : local;
    }

    public Vector3 PositionOf(string bone) => WorldOf(_skel[bone].Index).Translation;

    /// <summary>
    /// Two-bone analytic IK for an arm: puts the wrist at <paramref name="target"/> (character space) with the elbow
    /// pushed toward <paramref name="elbowHint"/>. weight blends against whatever FK rotations were already written.
    /// </summary>
    public void ArmIK(int side, Vector3 target, Vector3 elbowHint, float weight = 1f)
    {
        string L = side > 0 ? "L" : "R";
        if (!_skel.Has(BoneNames.Of("arm", L)) || weight <= 0) return;
        var arm = _skel[BoneNames.Of("arm", L)]; var fore = _skel[BoneNames.Of("fore", L)]; var hand = _skel[BoneNames.Of("hand", L)];
        float l1 = fore.LocalOffset.Length(), l2 = hand.LocalOffset.Length();

        var parentWorld = WorldOf(arm.Parent);
        var shoulder = Vector3.Transform(arm.LocalOffset, parentWorld);
        var toTarget = target - shoulder;
        float d = MathHelper.Clamp(toTarget.Length(), 0.05f, l1 + l2 - 0.005f);
        var dir = Vector3.Normalize(toTarget);

        float cosA = MathHelper.Clamp((l1 * l1 + d * d - l2 * l2) / (2 * l1 * d), -1, 1);
        float a = MathF.Acos(cosA);
        float cosE = MathHelper.Clamp((l1 * l1 + l2 * l2 - d * d) / (2 * l1 * l2), -1, 1);
        float flex = MathHelper.Pi - MathF.Acos(cosE);          // elbow flexion

        var perp = elbowHint - dir * Vector3.Dot(elbowHint, dir);
        if (perp.LengthSquared() < 1e-6f) perp = Vector3.Cross(dir, Vector3.Up);
        perp.Normalize();
        var u = dir * MathF.Cos(a) + perp * MathF.Sin(a);        // upper-arm direction
        var elbow = shoulder + u * l1;
        var f = Vector3.Normalize(target - elbow);               // forearm direction

        // Upper-arm frame: bone axis is -Y, elbow bends toward local +Z (rotation about local +X).
        var yl = -u;
        var zl = f - u * Vector3.Dot(f, u);
        if (zl.LengthSquared() < 1e-6f) zl = perp; else zl.Normalize();
        var xl = Vector3.Cross(yl, zl);
        var world = new Matrix(xl.X, xl.Y, xl.Z, 0, yl.X, yl.Y, yl.Z, 0, zl.X, zl.Y, zl.Z, 0, 0, 0, 0, 1);
        var parentRot = parentWorld; parentRot.Translation = Vector3.Zero;
        var qArm = Quaternion.CreateFromRotationMatrix(world * Matrix.Invert(parentRot));
        var qFore = Quaternion.CreateFromAxisAngle(Vector3.Right, -flex);

        int ia = arm.Index, ifo = fore.Index;
        _pose.Rotations[ia] = weight >= 1 ? qArm : Quaternion.Slerp(_pose.Rotations[ia], qArm, weight);
        _pose.Rotations[ifo] = weight >= 1 ? qFore : Quaternion.Slerp(_pose.Rotations[ifo], qFore, weight);
    }
}

/// <summary>
/// Side-suffixed bone names without per-call string concatenation: "arm" + "L" allocates a new string every
/// evaluation (dozens per character per frame). The cache turns it into one dictionary lookup on a value-tuple key.
/// </summary>
public static class BoneNames
{
    private static readonly Dictionary<(string, string), string> _side = new();
    private static readonly Dictionary<string, string> _toe = new();
    public static string Of(string prefix, string side)
    {
        if (!_side.TryGetValue((prefix, side), out var n)) { n = prefix + side; _side[(prefix, side)] = n; }
        return n;
    }
    public static string Toe(string footBone)
    {
        if (!_toe.TryGetValue(footBone, out var n)) { n = footBone.Replace("foot", "toe"); _toe[footBone] = n; }
        return n;
    }
}

public sealed class Clip
{
    public string Name;
    public Action<float, PoseWriter> Evaluate;
    /// <summary>Seconds for a one-shot clip; 0 = loops forever.</summary>
    public float Duration;
    public Clip(string name, Action<float, PoseWriter> evaluate, float duration = 0) { Name = name; Evaluate = evaluate; Duration = duration; }
}

/// <summary>Cross-fading clip player with a spring follow-through layer.</summary>
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
    public float Damping = 0.7f;   // ζ: 0.7 gives one small visible overshoot on stops/turns without ringing
    public float TimeOffset;
    public Clip? Current => _current;
    /// <summary>Time into the current clip (one-shots start at 0).</summary>
    public float ClipTime => _time + TimeOffset;

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
            _omega[i] = n.StartsWith("hand") ? MathHelper.TwoPi * 3.5f
                      : n.StartsWith("fore") ? MathHelper.TwoPi * 4.2f
                      : n.StartsWith("arm") ? MathHelper.TwoPi * 5f
                      : n == "head" ? MathHelper.TwoPi * 4f
                      : n == "neck" ? MathHelper.TwoPi * 6f
                      : 0f;   // trunk, pelvis and legs are never sprung: a lagging spine reads as wobble
        }
    }

    public void Play(Clip clip, bool restart = false)
    {
        if (_current == clip && !restart) return;
        _previous = _current; _prevTime = _time;
        _current = clip; _blend = _previous == null ? 1f : 0f;
        if (restart || clip.Duration > 0) _time = -TimeOffset;     // one-shots always start from zero
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

/// <summary>Joint-angle curves over one stride (u = 0 is heel strike of that leg, 0.6 ≈ toe-off).</summary>
public sealed class Gait
{
    public float Hz, Lean, Bob, Sway, Flight, ArmSwing, ElbowBase, ElbowSwing, PelvisYaw, PelvisDrop, ShoulderYaw;
    public (float, float)[] Hip = null!, Knee = null!, Ankle = null!, Toe = null!;
    /// <summary>Ground speed this gait was tuned for (m/s) at Height 1.8.</summary>
    public float Speed;
}

/// <summary>Library of procedural clips shared by all characters.</summary>
public static class Clips
{
    /// <summary>Cyclic Catmull-Rom keyframe curve over u in [0,1): continuous velocity, no stop-start at keys.</summary>
    public static float Key(float u, params (float t, float v)[] keys)
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

    /// <summary>Smooth max(0, x), C1 continuous so curves have no kinks.</summary>
    private static float Pos(float x, float k = 0.12f) => 0.5f * (x + MathF.Sqrt(x * x + k * k));
    public static float Smooth01(float x) { x = MathHelper.Clamp(x, 0, 1); return x * x * (3 - 2 * x); }

    /// <summary>Smooth, non-repeating -1..1 "life" noise from incommensurate sines; seed shifts the phase per use.</summary>
    public static float Life(float t, float seed) =>
        0.5f * MathF.Sin(t * 0.83f + seed) + 0.3f * MathF.Sin(t * 1.71f + seed * 1.7f + 0.9f) + 0.2f * MathF.Sin(t * 2.93f + seed * 2.3f + 2.1f);

    public static readonly Clip BindPose = new("Bind pose", (t, w) => { });

    public static readonly Clip Idle = new("Idle", (t, w) =>
    {
        float breath = MathF.Sin(t * 1.6f);
        float sway = MathF.Sin(t * 0.45f);
        float shift = Life(t * 0.35f, 1f);                       // slow weight shift from foot to foot
        float glance = Life(t * 0.5f, 2f);                       // occasional look around
        w.Root(new Vector3(0.004f * sway + 0.012f * shift, 0.003f * breath - 0.004f * MathF.Abs(shift), 0));
        w.Upright("hips", 0, 2.5f * shift, 1.5f * shift);
        w.Upright("spine", 1.0f + 0.8f * breath, 1.2f * sway - 1.5f * shift, 2f * sway + 1.5f * Life(t, 3f));
        w.Upright("chest", 1.5f * breath, -0.8f * shift, -1.5f * sway);
        w.Upright("neck", -2f * breath, 0, 4f * glance);
        w.Upright("head", 3f * MathF.Sin(t * 0.7f) + 2f * Life(t * 0.7f, 4f), 2f * MathF.Sin(t * 0.33f), 9f * MathF.Sin(t * 0.5f) + 8f * glance);
        for (int s = -1; s <= 1; s += 2)
        {
            string L = s > 0 ? "L" : "R";
            float life = Life(t * 0.9f, 5f + s);
            w.Hang(BoneNames.Of("arm", L), 3f + 2f * sway + 1.5f * life, 7f + 1.5f * breath + 1f * life, s);
            w.Hang(BoneNames.Of("fore", L), 14f + 2f * breath + 2f * Life(t * 0.8f, 6f + s), 2f, s);
            w.Hang(BoneNames.Of("hand", L), -5f + 4f * Life(t * 0.6f, 7f + s), 0, s, 6f * Life(t * 0.5f, 8f + s));
            // The unloaded leg bends a little as the weight shifts away from it.
            float unload = MathF.Max(0, -shift * s);
            w.Hang(BoneNames.Of("thigh", L), 0.5f + 3f * unload, 2.5f, s);
            w.Hang(BoneNames.Of("shin", L), -3f - 5f * unload, 0, s);
            w.Hang(BoneNames.Of("clav", L), 0, 1f * breath, s);
        }
    });

    // Joint curves modelled on human gait data (heel strike at 0, loading response, mid-stance, push-off ~0.6, swing).
    public static readonly Gait WalkGait = new()
    {
        Hz = 1.25f, Speed = 1.5f, Lean = 2f, Bob = 0.018f, Sway = 0.005f, Flight = 0,
        ArmSwing = 18f, ElbowBase = 12f, ElbowSwing = 14f, PelvisYaw = 4f, PelvisDrop = 2.5f, ShoulderYaw = 5f,
        Hip = new[] { (0f, 24f), (0.12f, 20f), (0.3f, 6f), (0.5f, -11f), (0.62f, -9f), (0.75f, 8f), (0.88f, 21f), (1f, 24f) },
        Knee = new[] { (0f, 6f), (0.12f, 18f), (0.3f, 8f), (0.45f, 9f), (0.55f, 32f), (0.68f, 62f), (0.8f, 44f), (0.9f, 14f), (1f, 6f) },
        Ankle = new[] { (0f, 3f), (0.08f, -5f), (0.3f, 5f), (0.45f, 9f), (0.56f, -14f), (0.66f, -8f), (0.82f, 4f), (1f, 3f) },
        Toe = new[] { (0f, 0f), (0.4f, 6f), (0.52f, 32f), (0.62f, 14f), (0.72f, 0f), (1f, 0f) }
    };

    public static readonly Gait RunGait = new()
    {
        Hz = 2.2f, Speed = 4.4f, Lean = 10f, Bob = 0.04f, Sway = 0.004f, Flight = 0.03f,
        ArmSwing = 40f, ElbowBase = 80f, ElbowSwing = 25f, PelvisYaw = 5f, PelvisDrop = 3f, ShoulderYaw = 7f,
        Hip = new[] { (0f, 34f), (0.15f, 26f), (0.35f, 2f), (0.5f, -17f), (0.6f, -9f), (0.75f, 22f), (0.9f, 34f), (1f, 34f) },
        Knee = new[] { (0f, 22f), (0.12f, 42f), (0.3f, 24f), (0.45f, 38f), (0.6f, 92f), (0.72f, 108f), (0.85f, 62f), (1f, 22f) },
        Ankle = new[] { (0f, 0f), (0.1f, -6f), (0.3f, 9f), (0.42f, 13f), (0.52f, -24f), (0.62f, -14f), (0.8f, 2f), (1f, 0f) },
        Toe = new[] { (0f, 0f), (0.35f, 8f), (0.5f, 38f), (0.6f, 18f), (0.7f, 0f), (1f, 0f) }
    };

    public static readonly Clip Walk = new("Walk", (t, w) => Locomotion(t * WalkGait.Hz, w, WalkGait, WalkGait, 0));
    public static readonly Clip Run = new("Run", (t, w) => Locomotion(t * RunGait.Hz, w, RunGait, RunGait, 0));

    /// <summary>Stride frequency for a ground speed (m/s, at Height 1.8): stride length grows with speed, so Hz grows sub-linearly.</summary>
    public static float StrideHz(float speed)
    {
        float r = Smooth01((speed - WalkGait.Speed) / (RunGait.Speed - WalkGait.Speed));
        float strideLen = MathHelper.Lerp(WalkGait.Speed / WalkGait.Hz, RunGait.Speed / RunGait.Hz, r);
        // Below walking pace the stride shortens too (stroll), so the feet still do not slide.
        if (speed < WalkGait.Speed) strideLen *= MathHelper.Lerp(0.6f, 1f, speed / WalkGait.Speed);
        return MathHelper.Clamp(speed / strideLen, 0.5f, 3.5f);
    }

    /// <summary>
    /// One stride of a gait blended between two gait definitions (r = 0 -> a, 1 -> b). u is the stride phase in cycles
    /// (left heel strike at 0); callers that change speed must integrate the phase themselves so it never jumps.
    /// </summary>
    public static void Locomotion(float u, PoseWriter w, Gait a, Gait b, float r)
    {
        u -= MathF.Floor(u);
        float c1 = MathF.Cos(MathHelper.TwoPi * u), s1 = MathF.Sin(MathHelper.TwoPi * u);
        float c2 = MathF.Cos(2 * MathHelper.TwoPi * u);
        float Mix(float x, float y) => MathHelper.Lerp(x, y, r);
        float lean = Mix(a.Lean, b.Lean), bob = Mix(a.Bob, b.Bob), sway = Mix(a.Sway, b.Sway), flight = Mix(a.Flight, b.Flight);
        float armSwing = Mix(a.ArmSwing, b.ArmSwing), elbowBase = Mix(a.ElbowBase, b.ElbowBase), elbowSwing = Mix(a.ElbowSwing, b.ElbowSwing);
        float pelvisYaw = Mix(a.PelvisYaw, b.PelvisYaw), pelvisDrop = Mix(a.PelvisDrop, b.PelvisDrop), shoulderYaw = Mix(a.ShoulderYaw, b.ShoulderYaw);

        // Root: lowest at double support, highest at mid-stance; a hint of sway over the stance foot; flight adds lift.
        w.Root(new Vector3(sway * s1, -0.5f * bob * c2 + flight * Pos(-c2, 0.3f), 0));
        // Pelvis and trunk counter-rotation; the head stays level and steady.
        w.Upright("hips", 0, -pelvisDrop * s1, -pelvisYaw * c1);
        w.Upright("spine", lean * 0.45f, 0.6f * s1, shoulderYaw * 0.45f * c1 + pelvisYaw * 0.5f * c1);
        w.Upright("chest", lean * 0.45f, -0.3f * s1, shoulderYaw * 0.55f * c1 + pelvisYaw * 0.5f * c1);
        w.Upright("neck", -lean * 0.4f, 0, -shoulderYaw * 0.4f * c1);
        w.Upright("head", -lean * 0.35f + 0.5f * c2, 0, -shoulderYaw * 0.4f * c1);

        for (int side = -1; side <= 1; side += 2)
        {
            string L = side > 0 ? "L" : "R";
            float p = side > 0 ? u : u + 0.5f;
            float hip = Mix(Key(p, a.Hip), Key(p, b.Hip)), knee = Mix(Key(p, a.Knee), Key(p, b.Knee));
            float ankle = Mix(Key(p, a.Ankle), Key(p, b.Ankle)), toe = Mix(Key(p, a.Toe), Key(p, b.Toe));
            w.Hang(BoneNames.Of("thigh", L), hip + lean * 0.3f, 2.5f, side);
            w.Hang(BoneNames.Of("shin", L), -knee, 0, side);
            w.Foot(BoneNames.Of("foot", L), ankle, side, 0, toe);

            // Arms swing opposite the same-side leg; the elbow bends more on the forward swing. A little life noise
            // keeps the two arms from being mirror images, and the wrist trails the swing.
            float life = Life(u * 1.3f, 9f + side);
            float armF = -(hip - 6f) * armSwing / 24f * (1f + 0.08f * life);
            float elbow = elbowBase + elbowSwing * Pos(armF / armSwing, 0.4f);
            float swingDir = -MathF.Sin(MathHelper.TwoPi * p);                // +1 when the arm is swinging forward
            w.Hang(BoneNames.Of("arm", L), armF, 6f + 1.5f * life, side);
            w.Hang(BoneNames.Of("fore", L), elbow, 3f, side);
            w.Hang(BoneNames.Of("hand", L), -4f - 8f * swingDir * (armSwing / 24f), 0, side, 4f * swingDir);
            // Shoulder girdle: the clavicle rides forward with the arm swing and rises at push-off.
            w.Hang(BoneNames.Of("clav", L), 3f * armF / 24f, 1.5f * MathF.Sin(MathHelper.TwoPi * p) + 1.0f, side);
        }
    }

    public static readonly Clip Wave = new("Wave", (t, w) =>
    {
        Idle.Evaluate(t, w);
        float raise = Smooth01(t * 1.4f);
        float wave = MathF.Sin(t * 7.5f) * Smooth01((t - 0.45f) * 1.6f);
        // Hand goes up in front of the head and swings left-right; the arm is solved with 2-bone IK.
        var rest = w.PositionOf("handR");
        var head = w.PositionOf("head");
        var target = head + new Vector3(-0.30f + 0.17f * wave, 0.30f, 0.20f);
        w.ArmIK(-1, Vector3.Lerp(rest, target, raise), new Vector3(-1f, -0.25f, 0.15f), raise);
        w.Hang("handR", 5f, -(20f + 18f * wave) * raise, -1, 0);
        w.Upright("head", 2f, -6f * raise, -10f * raise);
        w.Upright("chest", 0, 4f * raise, -6f * raise);
    }, 3.2f);

    // Keyframe tables are static: Key(u, params ...) would allocate a fresh array per call per frame.
    private static readonly (float, float)[] AtkArmf = { (0, 10), (0.22f, -60), (0.40f, -80), (0.52f, 60), (0.60f, 105), (0.75f, 90), (0.9f, 35), (1, 10) };
    private static readonly (float, float)[] AtkArmo = { (0, 10), (0.22f, 75), (0.40f, 95), (0.52f, 55), (0.60f, 30), (0.75f, 25), (0.9f, 15), (1, 10) };
    private static readonly (float, float)[] AtkFore = { (0, 20), (0.22f, 85), (0.40f, 105), (0.52f, 40), (0.60f, 8), (0.75f, 18), (0.9f, 22), (1, 20) };
    private static readonly (float, float)[] AtkTwist = { (0, 0), (0.25f, -26), (0.42f, -32), (0.55f, 10), (0.62f, 28), (0.8f, 20), (1, 0) };
    private static readonly (float, float)[] AtkLean = { (0, 2), (0.25f, -5), (0.42f, -7), (0.60f, 14), (0.78f, 10), (1, 2) };
    private static readonly (float, float)[] AtkDip = { (0, 0), (0.25f, 0.012f), (0.42f, 0.015f), (0.60f, -0.05f), (0.8f, -0.03f), (1, 0) };
    private static readonly (float, float)[] AtkStep = { (0, 0), (0.3f, -0.3f), (0.6f, 1f), (0.85f, 0.8f), (1, 0) };

    public static readonly Clip Attack = new("Attack", (t, w) =>
    {
        const float period = 1.4f;
        float u = (t % period) / period;

        float armF = Key(u, AtkArmf);
        float armO = Key(u, AtkArmo);
        float fore = Key(u, AtkFore);
        float twist = Key(u, AtkTwist);
        float lean = Key(u, AtkLean);
        float dip = Key(u, AtkDip);
        float step = Key(u, AtkStep);

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
        w.Foot("footR", -5 - 10 * step, -1, 0, 20f * Pos(step, 0.3f));
    }, 1.4f);

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
            w.Hang(BoneNames.Of("arm", L), 40f + 30f * a, 60f + 25f * a, side);
            w.Hang(BoneNames.Of("fore", L), 80f + 30f * MathF.Sin(beat + ph), 20f, side);
            w.Hang(BoneNames.Of("hand", L), -10f * MathF.Sin(beat + ph), 0, side);
            float lift = Pos(a, 0.3f);
            w.Hang(BoneNames.Of("thigh", L), 10f + 12f * lift, 10f + 10f * lift, side);
            w.Hang(BoneNames.Of("shin", L), -20f - 25f * lift, 0, side);
            w.Foot(BoneNames.Of("foot", L), 8f * lift, side);
        }
    });

    /// <summary>
    /// One-shot reach to the weapon sockets (draw and sheathe share it; the weapon itself is re-attached by the
    /// character half-way through). Built per character because the sockets differ by weapon.
    /// </summary>
    public static Clip Reach(Skeleton skel, IReadOnlyList<(int side, string socket, Vector3 hint)> reaches, float duration)
    {
        return new Clip("Draw", (t, w) =>
        {
            Idle.Evaluate(t + 3f, w);
            float u = MathHelper.Clamp(t / duration, 0, 1);
            float reach = u < 0.45f ? Smooth01(u / 0.45f) : u < 0.6f ? 1f : 1f - Smooth01((u - 0.6f) / 0.4f);
            w.Upright("chest", 2f * reach, 0, 0);
            foreach (var (side, socket, hint) in reaches)
            {
                if (!skel.Has(socket)) continue;
                string L = side > 0 ? "L" : "R";
                var rest = w.PositionOf(BoneNames.Of("hand", L));
                var target = w.PositionOf(socket);
                w.ArmIK(side, Vector3.Lerp(rest, target, reach), hint, reach);
            }
        }, duration);
    }

    public static readonly IReadOnlyList<Clip> All = new[] { BindPose, Idle, Walk, Run, Wave, Attack, Dance };
}
