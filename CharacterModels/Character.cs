using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace CharacterModels;

public enum HeadGear { Bald, ShortHair, Helmet, Hood, WizardHat, Bandana }
public enum Weapon { None, Sword, Staff, Bow, Daggers, Axe }
public enum Sleeves { Short, Long, Bracers }

public sealed class CharacterSpec
{
    public string Name = "Character";
    public float Height = 1.8f;
    public float Bulk = 1f;          // limb/torso thickness
    public float Shoulders = 1f;
    public float Hips = 1f;
    public float HeadSize = 1f;

    public Color Skin = new(222, 178, 148);
    public Color Hair = new(70, 45, 30);
    public Color Eye = new(70, 110, 150);
    public Color Shirt = new(90, 110, 150);
    public Color Pants = new(70, 65, 80);
    public Color Boots = new(60, 45, 35);
    public Color Leather = new(95, 65, 40);
    public Color Metal = new(195, 200, 210);
    public Color Accent = new(200, 50, 50);
    public Vector2 ShirtMaterial = Mat.Cloth;

    public HeadGear HeadGear = HeadGear.ShortHair;
    public Weapon Weapon = Weapon.None;
    public Sleeves Sleeves = Sleeves.Short;
    public bool Beard, Ponytail, Pauldrons, ChestPlate, Shield, Robe, Quiver, Backpack, Belt = true, Gloves;
}

/// <summary>A built, renderable skinned character.</summary>
public sealed class Character
{
    public CharacterSpec Spec = null!;
    public Skeleton Skeleton = null!;
    public AnimationPlayer Player = null!;
    public VertexBuffer VertexBuffer = null!;
    public IndexBuffer IndexBuffer = null!;
    public Vector3 Position;
    public float Yaw;
    public int Triangles, Vertices;
    public SkinnedVertex[] MeshVertices = Array.Empty<SkinnedVertex>();
    public int[] MeshIndices = Array.Empty<int>();

    public Matrix World => Matrix.CreateRotationY(Yaw) * Matrix.CreateTranslation(Position);

    // ---- Animation state machine -------------------------------------------------------------
    /// <summary>Looping clip used when no action is playing (idle/walk/run, or whatever the viewer chose).</summary>
    public Clip Locomotion = Clips.Idle;
    /// <summary>Speed-driven locomotion: set <see cref="Speed"/> every frame and use this as the Locomotion clip.
    /// Idle, walk and run share one continuously integrated stride phase, so changing speed never cross-fades
    /// two out-of-phase cycles (the source of the old back-and-forth wobble).</summary>
    public Clip Move { get; private set; } = null!;
    public float Speed;                 // world m/s
    private float _stridePhase;
    public float StridePhase => _stridePhase;
    private Pose? _locoPose;
    public Clip? Action { get; private set; }
    public Clip? Queued;
    private float _actionTime;
    public bool Busy => Action != null;

    /// <summary>Weapon state: 1 = in hand, 0 = in the sheath socket.</summary>
    public bool Drawn;
    public float DrawBlend;
    public bool HasWeapon => Spec.Weapon != Weapon.None;
    private Clip? _reach;
    private float _swapAt = -1;

    public void CancelAction() { Action = null; Queued = null; _swapAt = -1; }

    public void PlayAction(Clip clip)
    {
        Action = clip; _actionTime = 0;
        Player.Play(clip, restart: true);
    }

    /// <summary>Starts the draw or sheathe reach; the weapon swaps sockets at the middle of the reach.</summary>
    public void ToggleWeapon()
    {
        if (!HasWeapon || Busy) return;
        _reach ??= BuildReach();
        _swapAt = _reach.Duration * 0.5f;
        PlayAction(_reach);
    }

    private Clip BuildReach()
    {
        var reaches = new List<(int, string, Vector3)>();
        if (Skeleton.Has("sheathR")) reaches.Add((-1, "sheathR", Spec.Weapon == Weapon.Daggers ? new Vector3(-1f, 0.2f, 0.6f) : new Vector3(-0.8f, 0.9f, -0.3f)));
        if (Skeleton.Has("sheathL")) reaches.Add((1, "sheathL", Spec.Weapon == Weapon.Daggers ? new Vector3(1f, 0.2f, 0.6f) : new Vector3(0.8f, 0.9f, -0.3f)));
        return Clips.Reach(Skeleton, reaches, 0.9f);
    }

    /// <summary>Builds the speed-driven Move clip; needs the skeleton (CharacterBuilder calls it).</summary>
    public void InitMove()
    {
        _locoPose = new Pose(Skeleton.Count);
        Move = new Clip("Move", (t, w) =>
        {
            float sp = Speed / (Spec.Height / 1.8f);                    // normalise to the gait tables' reference height
            float amp = Clips.Smooth01(sp / 0.7f);                      // idle -> stroll over the first 0.7 m/s
            float r = Clips.Smooth01((sp - Clips.WalkGait.Speed) / (Clips.RunGait.Speed - Clips.WalkGait.Speed));
            Clips.Idle.Evaluate(t, w);
            if (amp <= 0.001f) return;
            _locoPose!.Reset();
            Clips.Locomotion(_stridePhase, new PoseWriter(_locoPose, Skeleton), Clips.WalkGait, Clips.RunGait, r);
            w.BlendToward(_locoPose, amp);
        });
    }

    public void Update(float dt)
    {
        // Integrate the stride phase from speed so the feet match the ground and the cycle never jumps.
        float normSpeed = Speed / (Spec.Height / 1.8f);
        if (normSpeed > 0.05f) _stridePhase += dt * Clips.StrideHz(normSpeed);
        else
        {
            // Settle to the nearest double-support pose so a stop never freezes mid-swing.
            float target = MathF.Round(_stridePhase * 2f) / 2f;
            _stridePhase = MathHelper.Lerp(_stridePhase, target, 1 - MathF.Exp(-dt * 10f));
        }
        if (Action != null)
        {
            _actionTime += dt;
            if (_swapAt >= 0 && _actionTime >= _swapAt) { Drawn = !Drawn; _swapAt = -1; }
            if (Action.Duration > 0 && _actionTime >= Action.Duration)
            {
                Action = null;
                if (Queued != null) { var q = Queued; Queued = null; PlayAction(q); }
            }
        }
        if (Action == null) Player.Play(Locomotion);
        Player.Update(dt);

        DrawBlend = MathHelper.Lerp(DrawBlend, Drawn ? 1f : 0f, 1 - MathF.Exp(-dt * 14f));
        AttachWeapons();
    }

    /// <summary>Weapon bones follow the hand when drawn and the sheath socket when holstered (blended while swapping).</summary>
    private void AttachWeapons()
    {
        for (int side = -1; side <= 1; side += 2)
        {
            string L = side > 0 ? "L" : "R";
            if (!Skeleton.Has(BoneNames.Of("sheath", L))) continue;
            var weapon = Skeleton[BoneNames.Of("weapon", L)];
            var socket = Skeleton[BoneNames.Of("sheath", L)];
            var inHand = weapon.World; var inSheath = socket.World;
            float t = DrawBlend;
            Matrix world;
            if (t >= 0.999f) world = inHand;
            else if (t <= 0.001f) world = inSheath;
            else
            {
                var q = Quaternion.Slerp(Quaternion.CreateFromRotationMatrix(inSheath), Quaternion.CreateFromRotationMatrix(inHand), t);
                var pos = Vector3.Lerp(inSheath.Translation, inHand.Translation, t);
                world = Matrix.CreateFromQuaternion(q) * Matrix.CreateTranslation(pos);
            }
            weapon.World = world;
            Skeleton.Palette[weapon.Index] = weapon.InverseBind * world;
        }
    }

    /// <summary>Writes the bind-pose mesh as a Wavefront OBJ with per-vertex colours (Blender reads the "v x y z r g b" extension).</summary>
    public void ExportObj(string path)
    {
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        using var w = new System.IO.StreamWriter(path);
        w.WriteLine($"# {Spec.Name} - generated by CharacterModels (MonoGame), bind pose, metres, Y up, faces +Z");
        w.WriteLine($"o {Spec.Name}");
        foreach (var v in MeshVertices)
            w.WriteLine(string.Format(ci, "v {0:F5} {1:F5} {2:F5} {3:F4} {4:F4} {5:F4}", v.Position.X, v.Position.Y, v.Position.Z,
                v.Color.R / 255f, v.Color.G / 255f, v.Color.B / 255f));
        foreach (var v in MeshVertices)
            w.WriteLine(string.Format(ci, "vn {0:F4} {1:F4} {2:F4}", v.Normal.X, v.Normal.Y, v.Normal.Z));
        for (int i = 0; i < MeshIndices.Length; i += 3)
        {
            int a = MeshIndices[i] + 1, b = MeshIndices[i + 1] + 1, c = MeshIndices[i + 2] + 1;
            w.WriteLine($"f {a}//{a} {b}//{b} {c}//{c}");
        }
    }
}

/// <summary>Builds the skeleton and skinned mesh for a spec. All geometry is generated; no assets.</summary>
public static class CharacterBuilder
{
    private const int Seg = 24;   // ring segments for limbs/torso

    public static Character Build(GraphicsDevice device, CharacterSpec spec)
    {
        var skel = BuildSkeleton(spec);
        var mb = new MeshBuilder();
        BuildBody(mb, skel, spec);
        var (vb, ib) = mb.Upload(device);
        skel.Update();
        var character = new Character
        {
            Spec = spec, Skeleton = skel, Player = new AnimationPlayer(skel),
            VertexBuffer = vb, IndexBuffer = ib, Triangles = mb.TriangleCount, Vertices = mb.VertexCount,
            MeshVertices = mb.Vertices, MeshIndices = mb.Indices
        };
        character.InitMove();
        return character;
    }

    // --------------------------------------------------------------- skeleton

    private static Skeleton BuildSkeleton(CharacterSpec spec)
    {
        float s = spec.Height / 1.8f;
        var sk = new Skeleton();
        sk.Add("hips", null, new Vector3(0, 0.95f, 0) * s, new Vector3(0, 0.12f, 0) * s);
        sk.Add("spine", "hips", new Vector3(0, 0.12f, 0) * s, new Vector3(0, 0.18f, 0) * s);
        sk.Add("chest", "spine", new Vector3(0, 0.18f, 0) * s, new Vector3(0, 0.20f, 0) * s);
        sk.Add("neck", "chest", new Vector3(0, 0.20f, 0) * s, new Vector3(0, 0.08f, 0) * s);
        sk.Add("head", "neck", new Vector3(0, 0.08f, 0) * s, new Vector3(0, 0.24f, 0) * s * spec.HeadSize);

        float sw = 0.21f * s * spec.Shoulders;
        float hw = 0.10f * s * spec.Hips;
        for (int side = -1; side <= 1; side += 2)
        {
            string L = side > 0 ? "L" : "R";
            sk.Add(BoneNames.Of("clav", L), "chest", new Vector3(side * 0.03f * s, 0.16f * s, 0), new Vector3(side * (sw - 0.03f * s), 0.01f * s, 0));
            sk.Add(BoneNames.Of("arm", L), BoneNames.Of("clav", L), new Vector3(side * (sw - 0.03f * s), 0.01f * s, 0), new Vector3(0, -0.30f * s, 0));
            sk.Add(BoneNames.Of("fore", L), BoneNames.Of("arm", L), new Vector3(0, -0.30f * s, 0), new Vector3(0, -0.27f * s, 0));
            sk.Add(BoneNames.Of("hand", L), BoneNames.Of("fore", L), new Vector3(0, -0.27f * s, 0), new Vector3(0, -0.17f * s, 0));
            sk.Add(BoneNames.Of("thigh", L), "hips", new Vector3(side * hw, -0.03f * s, 0), new Vector3(0, -0.44f * s, 0));
            sk.Add(BoneNames.Of("shin", L), BoneNames.Of("thigh", L), new Vector3(0, -0.44f * s, 0), new Vector3(0, -0.42f * s, 0));
            sk.Add(BoneNames.Of("foot", L), BoneNames.Of("shin", L), new Vector3(0, -0.42f * s, 0), new Vector3(0, -0.035f * s, 0.11f * s));
            sk.Add(BoneNames.Of("toe", L), BoneNames.Of("foot", L), new Vector3(0, -0.035f * s, 0.11f * s), new Vector3(0, 0, 0.07f * s));

            // Weapon bone sits at the hand; its BindRotation turns the hanging bind-pose weapon into a natural grip
            // (blade forward-down across the fist for swords/axes/daggers, vertical for staff and bow).
            bool grip = spec.Weapon is Weapon.Sword or Weapon.Axe or Weapon.Daggers;
            var wb = sk.Add(BoneNames.Of("weapon", L), BoneNames.Of("hand", L), Vector3.Zero, new Vector3(0, -0.3f * s, 0));
            wb.BindRotation = grip ? Quaternion.CreateFromAxisAngle(Vector3.Right, MathHelper.ToRadians(-58f)) : Quaternion.Identity;
        }

        // Sheath sockets: where the weapon bone goes when holstered (chest for back carry, hips for daggers).
        switch (spec.Weapon)
        {
            case Weapon.Sword:
            case Weapon.Axe:
                Socket(sk, "sheathR", "chest", new Vector3(-0.08f, -0.02f, -0.19f) * s, 180f, -22f);
                break;
            case Weapon.Staff:
                Socket(sk, "sheathR", "chest", new Vector3(-0.10f, -0.30f, -0.20f) * s, 0f, -32f);
                break;
            case Weapon.Bow:
                Socket(sk, "sheathL", "chest", new Vector3(0.04f, -0.10f, -0.21f) * s, 0f, 28f);
                break;
            case Weapon.Daggers:
                Socket(sk, "sheathR", "hips", new Vector3(-0.20f, 0.03f, 0.03f) * s, 0f, -12f);
                Socket(sk, "sheathL", "hips", new Vector3(0.20f, 0.03f, 0.03f) * s, 0f, 12f);
                break;
        }
        return sk;
    }

    private static void Socket(Skeleton sk, string name, string parent, Vector3 offset, float flipDeg, float tiltDeg)
    {
        var b = sk.Add(name, parent, offset, new Vector3(0, 0.1f, 0));
        b.BindRotation = Quaternion.CreateFromAxisAngle(Vector3.Right, MathHelper.ToRadians(flipDeg))
                       * Quaternion.CreateFromAxisAngle(Vector3.Backward, MathHelper.ToRadians(tiltDeg));
    }

    // ------------------------------------------------------------------- body

    private static void BuildBody(MeshBuilder mb, Skeleton sk, CharacterSpec spec)
    {
        float s = spec.Height / 1.8f;
        float b = spec.Bulk;
        Vector3 P(float x, float y, float z) => new Vector3(x, y, z) * s;

        var torsoW = new Weighter(sk, 4, "hips", "spine", "chest", "neck");

        // ---- Torso (or robe) -------------------------------------------------
        {
            Color torsoCol(float y) => spec.ChestPlate ? spec.Metal : spec.Shirt;
            Vector2 torsoMat(float y) => spec.ChestPlate ? Mat.Metal : spec.ShirtMaterial;
            float sh = MathF.Sqrt(spec.Shoulders), hp = spec.Hips;
            var rings = new List<Ring>();
            if (spec.Robe)
            {
                rings.Add(new Ring(P(0, 0.10f, 0), 0.30f * s * b, 0.26f * s * b, spec.Shirt, spec.ShirtMaterial));
                rings.Add(new Ring(P(0, 0.40f, 0), 0.25f * s * b, 0.21f * s * b, spec.Shirt, spec.ShirtMaterial));
                rings.Add(new Ring(P(0, 0.70f, 0), 0.19f * s * b, 0.15f * s * b, spec.Shirt, spec.ShirtMaterial));
            }
            (float y, float rx, float rz)[] prof =
            {
                (0.87f, 0.14f * hp, 0.10f), (0.94f, 0.165f * hp, 0.12f), (1.02f, 0.15f, 0.105f), (1.10f, 0.152f, 0.11f),
                (1.20f, 0.17f, 0.125f), (1.30f, 0.185f * sh, 0.13f), (1.39f, 0.175f * sh, 0.12f), (1.45f, 0.11f, 0.09f)
            };
            foreach (var (y, rx, rz) in prof)
                rings.Add(new Ring(P(0, y, 0), rx * s * b, rz * s * b, torsoCol(y), torsoMat(y)));
            var w = spec.Robe ? new Weighter(sk, 4, "hips", "spine", "chest", "neck", "thighL", "thighR") : torsoW;
            mb.Loft(rings, Seg, w, Vector3.Backward, capStart: true, capEnd: true);
        }

        // ---- Belt -------------------------------------------------------------
        if (spec.Belt)
        {
            float rx = 0.16f * spec.Hips * s * b + 0.012f * s, rz = 0.115f * s * b + 0.012f * s;
            mb.Loft(new[]
            {
                new Ring(P(0, 0.99f, 0), rx, rz, spec.Leather, Mat.Leather),
                new Ring(P(0, 1.06f, 0), rx, rz, spec.Leather, Mat.Leather)
            }, Seg, new Weighter(sk, 4, "hips", "spine"), Vector3.Backward, capStart: true, capEnd: true, capSteps: 2);
            mb.Box(P(0, 1.025f, 0) + new Vector3(0, 0, rz), new Vector3(0.05f, 0.045f, 0.015f) * s, spec.Metal, Mat.Metal, Weighter.Fixed(sk, "spine"));
        }

        // ---- Neck ---------------------------------------------------------------
        mb.Loft(new[]
        {
            new Ring(P(0, 1.42f, 0), 0.058f * s * b, 0.055f * s * b, spec.Skin, Mat.Skin),
            new Ring(P(0, 1.56f, 0), 0.052f * s * b, 0.05f * s * b, spec.Skin, Mat.Skin)
        }, Seg, new Weighter(sk, 4, "chest", "neck", "head"), Vector3.Backward);

        BuildHead(mb, sk, spec);

        // ---- Limbs ----------------------------------------------------------------
        float sw = 0.21f * s * spec.Shoulders, hw = 0.10f * s * spec.Hips;
        for (int side = -1; side <= 1; side += 2)
        {
            string L = side > 0 ? "L" : "R";
            float x = side * sw;

            // Arm
            Color sleeve = spec.ChestPlate ? spec.Shirt : spec.Shirt;
            Color foreCol = spec.Sleeves switch { Sleeves.Long => spec.Shirt, Sleeves.Bracers => spec.Leather, _ => spec.Skin };
            Vector2 foreMat = spec.Sleeves switch { Sleeves.Long => spec.ShirtMaterial, Sleeves.Bracers => Mat.Leather, _ => Mat.Skin };
            float wide = spec.Robe ? 1.6f : 1f;
            var armW = new Weighter(sk, 4, BoneNames.Of("clav", L), BoneNames.Of("arm", L), BoneNames.Of("fore", L), BoneNames.Of("hand", L));
            mb.Loft(new[]
            {
                new Ring(new Vector3(x, 1.45f * s, 0), 0.07f * s * b, sleeve, spec.ShirtMaterial),
                new Ring(new Vector3(x, 1.38f * s, 0), 0.063f * s * b, sleeve, spec.ShirtMaterial),
                new Ring(new Vector3(x, 1.27f * s, 0), 0.053f * s * b, sleeve, spec.ShirtMaterial),
                new Ring(new Vector3(x, 1.17f * s, 0), 0.047f * s * b, sleeve, spec.ShirtMaterial),
                new Ring(new Vector3(x, 1.12f * s, 0), 0.045f * s * b, foreCol, foreMat),
                new Ring(new Vector3(x, 1.04f * s, 0), 0.047f * s * b * wide, foreCol, foreMat),
                new Ring(new Vector3(x, 0.95f * s, 0), 0.044f * s * b * wide, foreCol, foreMat),
                new Ring(new Vector3(x, 0.87f * s, 0), 0.036f * s * b * (spec.Robe ? 2.2f : 1f), foreCol, foreMat)
            }, Seg, armW, Vector3.Backward, capStart: true, capEnd: !spec.Robe);

            // Hand (mitten + thumb)
            Color handCol = spec.Gloves ? spec.Leather : spec.Skin;
            Vector2 handMat = spec.Gloves ? Mat.Leather : Mat.Skin;
            var handW = new Weighter(sk, 4, BoneNames.Of("fore", L), BoneNames.Of("hand", L));
            mb.Loft(new[]
            {
                new Ring(new Vector3(x, 0.86f * s, 0), 0.024f * s * b, 0.036f * s * b, handCol, handMat),
                new Ring(new Vector3(x, 0.80f * s, 0.005f * s), 0.027f * s * b, 0.046f * s * b, handCol, handMat),
                new Ring(new Vector3(x, 0.745f * s, 0.008f * s), 0.025f * s * b, 0.044f * s * b, handCol, handMat),
                new Ring(new Vector3(x, 0.70f * s, 0.008f * s), 0.019f * s * b, 0.032f * s * b, handCol, handMat)
            }, 16, handW, Vector3.Backward, capEnd: true);
            mb.Loft(new[]
            {
                new Ring(new Vector3(x - side * 0.008f * s, 0.83f * s, 0.03f * s), 0.014f * s * b, handCol, handMat),
                new Ring(new Vector3(x - side * 0.022f * s, 0.79f * s, 0.05f * s), 0.012f * s * b, handCol, handMat),
                new Ring(new Vector3(x - side * 0.03f * s, 0.765f * s, 0.06f * s), 0.009f * s * b, handCol, handMat)
            }, 10, Weighter.Fixed(sk, BoneNames.Of("hand", L)), Vector3.Backward, capEnd: true, capSteps: 3);

            // Leg
            float lx = side * hw;
            var legW = new Weighter(sk, 4, "hips", BoneNames.Of("thigh", L), BoneNames.Of("shin", L), BoneNames.Of("foot", L));
            Color legCol(float y) => y < 0.31f ? spec.Boots : spec.Pants;
            Vector2 legMat(float y) => y < 0.31f ? Mat.Leather : Mat.Cloth;
            (float y, float r)[] lp =
            {
                (0.96f, 0.088f), (0.86f, 0.082f), (0.72f, 0.071f), (0.58f, 0.061f), (0.48f, 0.057f),
                (0.40f, 0.059f), (0.32f, 0.057f), (0.30f, 0.056f), (0.18f, 0.047f), (0.09f, 0.043f)
            };
            var legRings = new List<Ring>();
            foreach (var (y, r) in lp) legRings.Add(new Ring(new Vector3(lx, y * s, 0), r * s * b, legCol(y), legMat(y)));
            mb.Loft(legRings, Seg, legW, Vector3.Backward, capStart: true, capEnd: true);

            // Boot cuff
            mb.Loft(new[]
            {
                new Ring(new Vector3(lx, 0.22f * s, 0), 0.062f * s * b, spec.Boots, Mat.Leather),
                new Ring(new Vector3(lx, 0.30f * s, 0), 0.066f * s * b, spec.Boots, Mat.Leather)
            }, Seg, new Weighter(sk, 4, BoneNames.Of("shin", L), BoneNames.Of("foot", L)), Vector3.Backward, capStart: true, capEnd: true, capSteps: 2);

            // Foot
            var footW = new Weighter(sk, 5, BoneNames.Of("shin", L), BoneNames.Of("foot", L), BoneNames.Of("toe", L));
            mb.Loft(new[]
            {
                new Ring(new Vector3(lx, 0.05f * s, -0.05f * s), 0.046f * s * b, 0.042f * s * b, spec.Boots, Mat.Leather),
                new Ring(new Vector3(lx, 0.045f * s, 0.02f * s), 0.052f * s * b, 0.045f * s * b, spec.Boots, Mat.Leather),
                new Ring(new Vector3(lx, 0.040f * s, 0.08f * s), 0.051f * s * b, 0.040f * s * b, spec.Boots, Mat.Leather),
                new Ring(new Vector3(lx, 0.036f * s, 0.12f * s), 0.048f * s * b, 0.035f * s * b, spec.Boots, Mat.Leather),
                new Ring(new Vector3(lx, 0.032f * s, 0.17f * s), 0.040f * s * b, 0.028f * s * b, spec.Boots, Mat.Leather)
            }, 16, footW, Vector3.Up, capStart: true, capEnd: true);

            // Pauldron
            if (spec.Pauldrons)
            {
                mb.Ellipsoid(new Vector3(side * (sw + 0.015f * s), 1.455f * s, 0), new Vector3(0.105f, 0.075f, 0.1f) * s * b, 20, 12,
                    spec.Metal, Mat.Metal, new Weighter(sk, 3, BoneNames.Of("clav", L), BoneNames.Of("arm", L)),
                    d => MathHelper.Lerp(0.55f, 1f, Sat((d.Y + 0.35f) / 0.25f)) * (1f + 0.04f * MathF.Max(0, MathF.Sin(d.Y * 14f))));
            }

            BuildWeapon(mb, sk, spec, side);
        }

        // ---- Back gear ------------------------------------------------------------
        var backW = new Weighter(sk, 3, "spine", "chest");
        if (spec.Quiver)
        {
            var q0 = P(0.07f, 0.92f, -0.15f); var q1 = P(-0.09f, 1.43f, -0.19f);
            mb.Loft(new[]
            {
                new Ring(q0, 0.045f * s, spec.Leather, Mat.Leather),
                new Ring(q1, 0.05f * s, spec.Leather, Mat.Leather)
            }, 14, backW, Vector3.Backward, capStart: true);
            var dir = Vector3.Normalize(q1 - q0);
            var rnd = new Random(7);
            for (int i = 0; i < 5; i++)
            {
                var off = new Vector3((float)(rnd.NextDouble() - 0.5) * 0.05f, 0, (float)(rnd.NextDouble() - 0.5) * 0.05f) * s;
                var a0 = q1 + off; var a1 = a0 + dir * 0.18f * s;
                mb.Loft(new[] { new Ring(a0, 0.005f * s, new Color(140, 110, 80), Mat.Wood), new Ring(a1, 0.005f * s, new Color(140, 110, 80), Mat.Wood) },
                    6, Weighter.Fixed(sk, "chest"), Vector3.Backward);
                mb.Box(a1 - dir * 0.025f * s, new Vector3(0.03f, 0.04f, 0.006f) * s, spec.Accent, Mat.Cloth, Weighter.Fixed(sk, "chest"),
                    Quaternion.CreateFromAxisAngle(Vector3.Right, 0.3f));
            }
        }
        if (spec.Backpack)
        {
            mb.Box(P(0, 1.2f, -0.2f), new Vector3(0.28f, 0.32f, 0.14f) * s, spec.Leather, Mat.Leather, Weighter.Fixed(sk, "chest"));
            mb.Box(P(0, 1.39f, -0.2f), new Vector3(0.29f, 0.06f, 0.15f) * s, spec.Leather * 0.8f, Mat.Leather, Weighter.Fixed(sk, "chest"));
        }
    }

    private static float Sat(float v) => MathHelper.Clamp(v, 0, 1);

    // ------------------------------------------------------------------- head

    private static void BuildHead(MeshBuilder mb, Skeleton sk, CharacterSpec spec)
    {
        float s = spec.Height / 1.8f;
        float hs = s * spec.HeadSize;
        var hc = new Vector3(0, 1.52f * s + 0.135f * hs, 0.01f * hs);
        Vector3 HP(float x, float y, float z) => hc + new Vector3(x, y, z) * hs;
        Vector3 HR(float x, float y, float z) => new Vector3(x, y, z) * hs;
        var headW = Weighter.Fixed(sk, "head");
        var headNeckW = new Weighter(sk, 3, "neck", "head");

        // Skull with jaw taper, chin and brow.
        mb.Ellipsoid(hc, HR(0.106f, 0.126f, 0.116f), 28, 20, spec.Skin, Mat.Skin, headNeckW, d =>
        {
            float k = 1f;
            float below = MathF.Max(0, -d.Y);
            float front = MathF.Max(0, d.Z);
            k *= 1f - 0.20f * below * (1f - 0.55f * front);                      // jaw narrows
            k += 0.05f * below * below * front * front;                              // chin
            k += 0.02f * MathF.Exp(-(d.Y - 0.3f) * (d.Y - 0.3f) * 30f) * front;    // brow
            k *= 1f - 0.03f * MathF.Max(0, -d.Z);                                  // flatter back
            return k;
        });

        // Eyes
        for (int side = -1; side <= 1; side += 2)
        {
            float ex = side * 0.039f;
            mb.Ellipsoid(HP(ex, 0.012f, 0.094f), HR(0.02f, 0.016f, 0.013f), 14, 8, Color.White, Mat.Eye, headW);
            mb.Ellipsoid(HP(ex, 0.012f, 0.102f), HR(0.0105f, 0.0105f, 0.006f), 12, 6, spec.Eye, Mat.Eye, headW);
            mb.Ellipsoid(HP(ex, 0.012f, 0.1065f), HR(0.005f, 0.005f, 0.003f), 8, 4, new Color(15, 12, 12), Mat.Eye, headW);
            // Brow
            mb.Box(HP(ex, 0.042f, 0.102f), HR(0.042f, 0.007f, 0.008f), spec.Hair, Mat.Hair, headW,
                Quaternion.CreateFromAxisAngle(Vector3.Backward, -side * 0.12f) * Quaternion.CreateFromAxisAngle(Vector3.Right, -0.35f));
            // Ear
            mb.Ellipsoid(HP(side * 0.104f, 0f, 0f), HR(0.012f, 0.028f, 0.02f), 10, 6, spec.Skin, Mat.Skin, headW);
        }
        // Nose & mouth
        mb.Ellipsoid(HP(0, -0.012f, 0.108f), HR(0.016f, 0.026f, 0.022f), 12, 8, spec.Skin, Mat.Skin, headW);
        mb.Box(HP(0, -0.048f, 0.103f), HR(0.036f, 0.006f, 0.008f), new Color(120, 60, 60), Mat.Skin, headW,
            Quaternion.CreateFromAxisAngle(Vector3.Right, -0.4f));

        // Beard
        if (spec.Beard)
        {
            mb.Ellipsoid(HP(0, -0.05f, 0.025f), HR(0.105f, 0.11f, 0.105f), 22, 14, spec.Hair, Mat.Hair, headW, d =>
            {
                float below = Sat((-d.Y - 0.05f) / 0.12f), front = Sat((d.Z + 0.05f) / 0.25f);
                float k = MathHelper.Lerp(0.75f, 1.03f, below * front);
                k *= 1f + 0.35f * MathF.Pow(MathF.Max(0, -d.Y), 2f) * front;
                k += 0.02f * MathF.Sin(d.X * 40f) * below;
                return k;
            });
        }

        switch (spec.HeadGear)
        {
            case HeadGear.ShortHair:
                mb.Ellipsoid(HP(0, 0.012f, -0.004f), HR(0.113f, 0.13f, 0.122f), 28, 18, spec.Hair, Mat.Hair, headW, d =>
                {
                    float thresh = d.Z > 0 ? -0.1f + 0.55f * d.Z : -0.1f + 0.45f * d.Z;
                    float edge = Sat((d.Y - thresh) / 0.1f);
                    float tuft = 0.025f * MathF.Sin(d.X * 23f) * MathF.Sin(d.Z * 17f + d.Y * 9f);
                    return MathHelper.Lerp(0.8f, 1f + tuft, edge);
                });
                break;
            case HeadGear.Helmet:
                mb.Ellipsoid(HP(0, 0.012f, 0), HR(0.122f, 0.138f, 0.13f), 28, 18, spec.Metal, Mat.Metal, headW, d =>
                {
                    float opening = Sat((d.Z - 0.35f) / 0.2f) * Sat((0.32f - MathF.Abs(d.Y + 0.05f)) / 0.1f);
                    float bottom = Sat((-d.Y - 0.55f) / 0.2f) * Sat((d.Z + 0.1f) / 0.2f);
                    return MathHelper.Lerp(1f, 0.8f, MathF.Max(opening, bottom));
                });
                mb.Box(HP(0, -0.01f, 0.125f), HR(0.018f, 0.085f, 0.012f), spec.Metal, Mat.Metal, headW);
                mb.Loft(new[]
                {
                    new Ring(HP(0, 0.13f, 0.03f), 0.018f * hs, spec.Accent, Mat.Cloth),
                    new Ring(HP(0, 0.19f, -0.02f), 0.024f * hs, spec.Accent, Mat.Cloth),
                    new Ring(HP(0, 0.19f, -0.10f), 0.024f * hs, spec.Accent, Mat.Cloth),
                    new Ring(HP(0, 0.12f, -0.17f), 0.018f * hs, spec.Accent, Mat.Cloth),
                    new Ring(HP(0, 0.02f, -0.21f), 0.01f * hs, spec.Accent, Mat.Cloth)
                }, 10, headW, Vector3.Backward, capStart: true, capEnd: true, capSteps: 3);
                break;
            case HeadGear.Hood:
                mb.Ellipsoid(HP(0, 0.03f, -0.012f), HR(0.135f, 0.15f, 0.142f), 28, 18, spec.Accent, Mat.Cloth,
                    new Weighter(sk, 3, "chest", "neck", "head"), d =>
                    {
                        float opening = Sat((d.Z - 0.3f) / 0.25f) * Sat((0.45f - MathF.Abs(d.Y)) / 0.15f);
                        float k = MathHelper.Lerp(1f, 0.76f, opening);
                        k *= 1f + 0.9f * MathF.Pow(MathF.Max(0, -d.Y), 1.6f) * (1f - 0.6f * MathF.Max(0, d.Z));
                        k += 0.015f * MathF.Sin(d.Y * 30f) * MathF.Max(0, -d.Y);
                        return k;
                    });
                break;
            case HeadGear.WizardHat:
                {
                    var hatW = headW;
                    var col = spec.Accent;
                    Ring R(float y, float r, float z = 0) => new Ring(HP(0, y, z), r * hs, col, Mat.Cloth) { Tangent = Vector3.Up };
                    mb.Loft(new[]
                    {
                        R(0.085f, 0.105f), R(0.08f, 0.26f), R(0.10f, 0.26f), R(0.105f, 0.118f),
                        R(0.20f, 0.095f), R(0.30f, 0.068f), R(0.40f, 0.040f, -0.02f), R(0.47f, 0.018f, -0.07f), R(0.50f, 0.004f, -0.12f)
                    }, 24, hatW, Vector3.Backward);
                    mb.Loft(new[]
                    {
                        new Ring(HP(0, 0.10f, 0), 0.125f * hs, 0.125f * hs, spec.Leather, Mat.Leather),
                        new Ring(HP(0, 0.135f, 0), 0.112f * hs, 0.112f * hs, spec.Leather, Mat.Leather)
                    }, 24, hatW, Vector3.Backward);
                    break;
                }
            case HeadGear.Bandana:
                mb.Ellipsoid(HP(0, 0.01f, -0.004f), HR(0.111f, 0.128f, 0.12f), 28, 18, spec.Hair, Mat.Hair, headW, d =>
                {
                    float thresh = d.Z > 0 ? -0.05f + 0.6f * d.Z : -0.15f + 0.45f * d.Z;
                    return MathHelper.Lerp(0.8f, 1f, Sat((d.Y - thresh) / 0.1f));
                });
                mb.Loft(new[]
                {
                    new Ring(HP(0, 0.045f, 0.005f), 0.114f * hs, 0.123f * hs, spec.Accent, Mat.Cloth),
                    new Ring(HP(0, 0.085f, 0.0f), 0.112f * hs, 0.121f * hs, spec.Accent, Mat.Cloth)
                }, 28, headW, Vector3.Backward, capStart: true, capEnd: true, capSteps: 2);
                mb.Loft(new[]
                {
                    new Ring(HP(0.02f, 0.06f, -0.115f), 0.02f * hs, 0.012f * hs, spec.Accent, Mat.Cloth),
                    new Ring(HP(0.04f, -0.03f, -0.16f), 0.02f * hs, 0.008f * hs, spec.Accent, Mat.Cloth),
                    new Ring(HP(0.05f, -0.13f, -0.15f), 0.016f * hs, 0.005f * hs, spec.Accent, Mat.Cloth)
                }, 8, headNeckW, Vector3.Backward, capEnd: true, capSteps: 2);
                break;
        }

        if (spec.Ponytail)
        {
            var pw = new Weighter(sk, 2.5f, "head", "neck", "chest");
            mb.Loft(new[]
            {
                new Ring(HP(0, 0.04f, -0.09f), 0.04f * hs, spec.Hair, Mat.Hair),
                new Ring(HP(0, -0.03f, -0.16f), 0.034f * hs, spec.Hair, Mat.Hair),
                new Ring(HP(0, -0.16f, -0.20f), 0.028f * hs, spec.Hair, Mat.Hair),
                new Ring(HP(0, -0.30f, -0.18f), 0.02f * hs, spec.Hair, Mat.Hair),
                new Ring(HP(0, -0.40f, -0.15f), 0.008f * hs, spec.Hair, Mat.Hair)
            }, 12, pw, Vector3.Backward, capEnd: true, capSteps: 3);
        }
    }

    // ---------------------------------------------------------------- weapons

    private static void BuildWeapon(MeshBuilder mb, Skeleton sk, CharacterSpec spec, int side)
    {
        float s = spec.Height / 1.8f;
        float sw = 0.21f * s * spec.Shoulders;
        string L = side > 0 ? "L" : "R";
        float x = side * sw;
        var handW = Weighter.Fixed(sk, BoneNames.Of("weapon", L));
        var foreW = Weighter.Fixed(sk, BoneNames.Of("fore", L));
        Vector3 V(float dx, float y, float z) => new Vector3(x + dx * s, y * s, z * s);
        var steel = spec.Metal; var wood = new Color(110, 80, 50); var dark = new Color(40, 35, 35);

        void Blade(float gripY, float bladeLen, float bladeW, float guardW)
        {
            mb.Loft(new[] { new Ring(V(0, gripY + 0.06f, 0.01f), 0.011f * s, dark, Mat.Leather), new Ring(V(0, gripY - 0.06f, 0.01f), 0.011f * s, dark, Mat.Leather) },
                10, handW, Vector3.Backward);
            mb.Ellipsoid(V(0, gripY + 0.075f, 0.01f), new Vector3(0.018f) * s, 10, 6, steel, Mat.Metal, handW);
            mb.Box(V(0, gripY - 0.07f, 0.01f), new Vector3(guardW, 0.016f, 0.026f) * s, steel, Mat.Metal, handW);
            mb.Box(V(0, gripY - 0.08f - bladeLen * 0.5f, 0.01f), new Vector3(0.012f, bladeLen, bladeW) * s, steel, Mat.Metal, handW);
            // Tip: thin wedge via a tiny loft with explicit tangent.
            mb.Loft(new[]
            {
                new Ring(V(0, gripY - 0.08f - bladeLen, 0.01f), 0.006f * s, bladeW * 0.5f * s, steel, Mat.Metal) { Tangent = Vector3.Down },
                new Ring(V(0, gripY - 0.08f - bladeLen - bladeW * 0.9f, 0.01f), 0.002f * s, 0.004f * s, steel, Mat.Metal) { Tangent = Vector3.Down }
            }, 8, handW, Vector3.Backward, capEnd: true, capSteps: 2);
        }

        if (side < 0 && spec.Weapon == Weapon.Sword) Blade(0.76f, 0.55f, 0.05f, 0.12f);
        if (spec.Weapon == Weapon.Daggers) Blade(0.76f, 0.22f, 0.032f, 0.07f);

        if (side < 0 && spec.Weapon == Weapon.Axe)
        {
            mb.Loft(new[] { new Ring(V(0, 1.05f, 0.01f), 0.016f * s, wood, Mat.Wood), new Ring(V(0, 0.25f, 0.01f), 0.018f * s, wood, Mat.Wood) },
                10, handW, Vector3.Backward, capStart: true, capEnd: true, capSteps: 2);
            // Axe head: a wedge lofted outward from the haft (thick at the haft, thin at the edge) plus a rear spike.
            mb.Loft(new[]
            {
                new Ring(V(0, 0.36f, 0.0f), 0.03f * s, 0.07f * s, steel, Mat.Metal) { Tangent = Vector3.Backward },
                new Ring(V(0, 0.36f, 0.06f), 0.026f * s, 0.10f * s, steel, Mat.Metal) { Tangent = Vector3.Backward },
                new Ring(V(0, 0.36f, 0.14f), 0.012f * s, 0.15f * s, steel, Mat.Metal) { Tangent = Vector3.Backward },
                new Ring(V(0, 0.36f, 0.19f), 0.002f * s, 0.17f * s, steel, Mat.Metal) { Tangent = Vector3.Backward }
            }, 16, handW, Vector3.Up, capStart: true, capSteps: 2);
            mb.Loft(new[]
            {
                new Ring(V(0, 0.36f, -0.0f), 0.028f * s, 0.045f * s, steel, Mat.Metal) { Tangent = Vector3.Forward },
                new Ring(V(0, 0.36f, -0.07f), 0.012f * s, 0.02f * s, steel, Mat.Metal) { Tangent = Vector3.Forward },
                new Ring(V(0, 0.36f, -0.11f), 0.003f * s, 0.004f * s, steel, Mat.Metal) { Tangent = Vector3.Forward }
            }, 10, handW, Vector3.Up, capStart: true, capSteps: 2);
        }

        if (side < 0 && spec.Weapon == Weapon.Staff)
        {
            mb.Loft(new[]
            {
                new Ring(V(0, 0.08f, 0.05f), 0.018f * s, wood, Mat.Wood),
                new Ring(V(0, 0.8f, 0.05f), 0.016f * s, wood, Mat.Wood),
                new Ring(V(0, 1.7f, 0.05f), 0.014f * s, wood, Mat.Wood),
                new Ring(V(0, 1.92f, 0.05f), 0.02f * s, wood, Mat.Wood)
            }, 10, handW, Vector3.Backward, capStart: true, capEnd: true, capSteps: 2);
            mb.Ellipsoid(V(0, 1.99f, 0.05f), new Vector3(0.05f) * s, 16, 12, new Color(120, 210, 255), Mat.Glow, handW);
        }

        if (side > 0 && spec.Weapon == Weapon.Bow)
        {
            var rings = new List<Ring>();
            const int n = 14;
            for (int i = 0; i <= n; i++)
            {
                float t = i / (float)n;
                float r = MathHelper.Lerp(0.007f, 0.014f, MathF.Sin(t * MathHelper.Pi));
                rings.Add(new Ring(V(0.03f, 0.18f + t * 1.2f, 0.04f + 0.16f * MathF.Sin(t * MathHelper.Pi)), r * s, wood, Mat.Wood));
            }
            mb.Loft(rings, 10, handW, Vector3.Right, capStart: true, capEnd: true, capSteps: 2);
            mb.Box(V(0.03f, 0.78f, 0.04f), new Vector3(0.004f, 1.2f, 0.004f) * s, new Color(230, 225, 200), Mat.Cloth, handW);
        }

        if (side > 0 && spec.Shield)
        {
            mb.Ellipsoid(V(0.075f, 0.98f, 0.02f), new Vector3(0.03f, 0.22f, 0.17f) * s, 20, 14, spec.Accent, Mat.Metal, foreW,
                d => 1f + 0.18f * MathF.Max(0, d.X));
            mb.Loft(new[]
            {
                new Ring(V(0.09f, 0.98f, 0.02f), 0.2f * s, 0.15f * s, steel, Mat.Metal) { Tangent = Vector3.Right },
                new Ring(V(0.10f, 0.98f, 0.02f), 0.22f * s, 0.17f * s, steel, Mat.Metal) { Tangent = Vector3.Right },
                new Ring(V(0.105f, 0.98f, 0.02f), 0.2f * s, 0.15f * s, steel, Mat.Metal) { Tangent = Vector3.Right }
            }, 24, foreW, Vector3.Up);
            mb.Ellipsoid(V(0.11f, 0.98f, 0.02f), new Vector3(0.035f, 0.045f, 0.045f) * s, 12, 8, steel, Mat.Metal, foreW);
        }
    }
}

/// <summary>The showcase line-up.</summary>
public static class Roster
{
    public static List<CharacterSpec> Create() => new()
    {
        new CharacterSpec
        {
            Name = "Knight", Height = 1.85f, Bulk = 1.08f, Shoulders = 1.1f,
            Skin = new Color(225, 182, 150), Hair = new Color(90, 60, 35), Eye = new Color(80, 100, 140),
            Shirt = new Color(40, 55, 110), Pants = new Color(55, 55, 65), Boots = new Color(45, 38, 34),
            Metal = new Color(200, 205, 215), Accent = new Color(190, 40, 45), Leather = new Color(80, 55, 35),
            HeadGear = HeadGear.Helmet, Weapon = Weapon.Sword, Sleeves = Sleeves.Long,
            Pauldrons = true, ChestPlate = true, Shield = true, Gloves = true
        },
        new CharacterSpec
        {
            Name = "Ranger", Height = 1.76f, Bulk = 0.92f, Shoulders = 0.95f,
            Skin = new Color(205, 160, 128), Hair = new Color(50, 35, 25), Eye = new Color(90, 130, 80),
            Shirt = new Color(85, 105, 60), Pants = new Color(75, 60, 45), Boots = new Color(60, 45, 32),
            Accent = new Color(55, 80, 50), Leather = new Color(110, 75, 45),
            HeadGear = HeadGear.Hood, Weapon = Weapon.Bow, Sleeves = Sleeves.Bracers, Quiver = true, Ponytail = true
        },
        new CharacterSpec
        {
            Name = "Mage", Height = 1.78f, Bulk = 0.95f, Shoulders = 0.92f,
            Skin = new Color(232, 205, 185), Hair = new Color(225, 222, 215), Eye = new Color(120, 160, 200),
            Shirt = new Color(85, 45, 130), Pants = new Color(60, 40, 80), Boots = new Color(50, 40, 45),
            Accent = new Color(70, 35, 110), Leather = new Color(200, 165, 70), Metal = new Color(230, 195, 90),
            ShirtMaterial = Mat.Cloth,
            HeadGear = HeadGear.WizardHat, Weapon = Weapon.Staff, Sleeves = Sleeves.Long, Robe = true, Beard = true, Belt = true
        },
        new CharacterSpec
        {
            Name = "Rogue", Height = 1.72f, Bulk = 0.9f, Shoulders = 0.95f, Hips = 1.05f,
            Skin = new Color(195, 150, 120), Hair = new Color(25, 22, 25), Eye = new Color(60, 70, 60),
            Shirt = new Color(48, 50, 58), Pants = new Color(38, 38, 45), Boots = new Color(35, 30, 30),
            Accent = new Color(160, 35, 40), Leather = new Color(70, 50, 38),
            HeadGear = HeadGear.Bandana, Weapon = Weapon.Daggers, Sleeves = Sleeves.Bracers, Ponytail = true, Gloves = true
        },
        new CharacterSpec
        {
            Name = "Barbarian", Height = 1.98f, Bulk = 1.28f, Shoulders = 1.18f, Hips = 1.05f, HeadSize = 1.05f,
            Skin = new Color(200, 150, 115), Hair = new Color(160, 70, 35), Eye = new Color(90, 120, 150),
            Shirt = new Color(95, 60, 38), Pants = new Color(95, 75, 55), Boots = new Color(80, 62, 45),
            Accent = new Color(120, 90, 60), Leather = new Color(60, 42, 30), Metal = new Color(170, 170, 165),
            ShirtMaterial = Mat.Leather,
            HeadGear = HeadGear.Bald, Weapon = Weapon.Axe, Sleeves = Sleeves.Bracers, Beard = true
        }
    };
}
