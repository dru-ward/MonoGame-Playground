using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;

namespace CharacterModels;

public sealed class Bone
{
    public string Name = "";
    public int Index;
    public int Parent = -1;
    public Vector3 LocalOffset;       // bind translation relative to parent
    public Vector3 TailOffset;        // local direction/length of the bone (for auto-weighting)
    public Vector3 BindHead, BindTail;
    public Matrix InverseBind;

    // Animated state (local space)
    public Quaternion Rotation = Quaternion.Identity;
    public Vector3 Translation;
    public Matrix World;
}

/// <summary>Axis-aligned bind-pose hierarchy that produces a GPU skinning palette.</summary>
public sealed class Skeleton
{
    private readonly List<Bone> _bones = new();
    private readonly Dictionary<string, Bone> _byName = new();
    public Matrix[] Palette = Array.Empty<Matrix>();
    public IReadOnlyList<Bone> Bones => _bones;
    public int Count => _bones.Count;

    public Bone this[string name] => _byName[name];
    public Bone this[int index] => _bones[index];
    public bool Has(string name) => _byName.ContainsKey(name);

    public Bone Add(string name, string? parent, Vector3 offset, Vector3 tail)
    {
        var b = new Bone
        {
            Name = name, Index = _bones.Count, Parent = parent == null ? -1 : _byName[parent].Index,
            LocalOffset = offset, TailOffset = tail
        };
        var parentHead = b.Parent >= 0 ? _bones[b.Parent].BindHead : Vector3.Zero;
        b.BindHead = parentHead + offset;
        b.BindTail = b.BindHead + tail;
        b.InverseBind = Matrix.CreateTranslation(-b.BindHead);
        _bones.Add(b); _byName[name] = b;
        return b;
    }

    public void ResetPose()
    {
        foreach (var b in _bones) { b.Rotation = Quaternion.Identity; b.Translation = Vector3.Zero; }
    }

    /// <summary>Recomputes world matrices and the skinning palette (InverseBind * World).</summary>
    public void Update()
    {
        if (Palette.Length != _bones.Count) Palette = new Matrix[_bones.Count];
        for (int i = 0; i < _bones.Count; i++)
        {
            var b = _bones[i];
            var local = Matrix.CreateFromQuaternion(b.Rotation) * Matrix.CreateTranslation(b.LocalOffset + b.Translation);
            b.World = b.Parent >= 0 ? local * _bones[b.Parent].World : local;
            Palette[i] = b.InverseBind * b.World;
        }
    }
}
