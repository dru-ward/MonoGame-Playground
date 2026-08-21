using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Game.Combat;
using Game.Core;
using Game.Graphics;

namespace Game.Entities;

/// <summary>
/// Shared body for the player and enemies: a collision circle, health, an aim facing (arms/head) that is independent
/// of the movement facing (boots) and a body facing that lags the aim, plus the layered draw:
///   shadow → boots (stride) → torso (sway) → arms+weapon (recoil / reload / swing) → head (bob).
/// </summary>
public abstract class Character
{
    public const float SpriteScale = 1.3f;                 // world px per layer texel

    public Vector2 Position;
    public Vector2 Velocity;
    public float Facing;                                   // aim (arms + head), radians, 0 = +X
    public float BodyFacing;                               // torso; lags the aim and sways with the stride
    public float MoveFacing;                               // boots (last movement direction)
    public float Radius = 24f;
    public float MaxHealth = 100f, Health = 100f;
    public Faction Faction { get; protected set; }
    public bool IsAlive => Health > 0f;
    public Vector3 BloodColor = new(0.9f, 0.1f, 0.08f);
    public float HitFlash;                                 // 1 right after being hit, decays
    public float StridePhase;                              // walk cycle
    public float RecoilKick;                               // arm layer pushed back along -Facing (px)
    public float DeadTimer;                                // seconds since death (fade-out for enemies)

    // ---- arm-layer animation (sprite-local texels / radians), set by subclasses each frame ----------------
    public Vector2 ArmsOffset;                             // e.g. reload: hand drops toward the mag pouch
    public float ArmsAngle;                                // e.g. bat wind-up / swing, reload tilt
    public float HeadTurn;                                 // extra head rotation (look-ahead / death)

    protected CharacterRig Rig = null!;
    protected HeldWeapon HeldWeapon;
    /// <summary>Small sprites drawn on top of the arm layer at arm-local positions (weapon attachments).</summary>
    public readonly List<(SpritePair sprite, Vector2 local)> ArmOverlays = new();
    public float Speed => Velocity.Length();

    /// <summary>Raised with (damage, hitDirection). Used for hit markers / sounds / score.</summary>
    public event Action<float, Vector2>? Damaged;
    public event Action? Died;

    public virtual void TakeDamage(float amount, Vector2 hitDir, Vector2 hitPos)
    {
        if (!IsAlive) return;
        Health -= amount; HitFlash = 1f;
        Damaged?.Invoke(amount, hitDir);
        if (Health <= 0f) { Health = 0f; OnDeath(); Died?.Invoke(); }
    }

    protected virtual void OnDeath() { }
    /// <summary>Corpse visibility 0..1 (base: quick fade; enemies keep bodies around for looting).</summary>
    public virtual float CorpseAlpha => MathHelper.Clamp(1f - DeadTimer / 1.5f, 0f, 1f);

    /// <summary>Common per-frame bookkeeping (call from subclasses).</summary>
    protected void TickCommon(float dt)
    {
        HitFlash = MathF.Max(0f, HitFlash - dt * 6f);
        RecoilKick = MathF.Max(0f, RecoilKick - dt * 40f);
        if (Velocity.LengthSquared() > 100f) MoveFacing = MathUtil.LerpAngle(MoveFacing, MathUtil.ToAngle(Velocity), MathUtil.Damp(12f, dt));
        BodyFacing = MathUtil.LerpAngle(BodyFacing, Facing, MathUtil.Damp(9f, dt));       // torso follows the aim with lag
        StridePhase += dt * (Speed / 26f);                 // ~one stride per 26 px of travel
        if (!IsAlive) DeadTimer += dt;
    }

    /// <summary>Layered draw. Alpha fades corpses; a hit tints everything toward red.</summary>
    public virtual void Draw(SceneBatch batch)
    {
        float alpha = IsAlive ? 1f : CorpseAlpha;
        if (alpha <= 0f) return;
        var tint = Color.Lerp(Color.White, new Color(255, 110, 100), HitFlash) * alpha;
        if (!IsAlive) tint = new Color(90, 80, 80) * alpha;

        float moving = MathHelper.Clamp(Speed / 200f, 0f, 1f);
        float stride = MathF.Sin(StridePhase) * 7f * moving;
        var fwd = MathUtil.FromAngle(MoveFacing); var side = new Vector2(-fwd.Y, fwd.X);

        // 0) shadow — slightly offset so the character reads as standing above the floor (albedo only)
        batch.DrawRotated(Rig.Shadow, Position + new Vector2(3f, 5f), BodyFacing, SpriteScale * 0.95f, Color.White * alpha, rotateNormals: false);

        // 1) boots: either side of the movement axis, sliding fore/aft in anti-phase
        if (IsAlive)
        {
            float spread = 9f * SpriteScale;
            batch.DrawRotated(Rig.Boot, Position + side * spread + fwd * stride, MoveFacing, SpriteScale, tint, rotateNormals: false);
            batch.DrawRotated(Rig.Boot, Position - side * spread - fwd * stride, MoveFacing, SpriteScale, tint, rotateNormals: false);
        }

        // 2) torso: lags the aim, sways with the stride, bobs in scale
        float sway = MathF.Sin(StridePhase) * 0.07f * moving;
        float bob = 1f + 0.025f * MathF.Sin(StridePhase * 2f) * moving;
        float torsoRot = IsAlive ? BodyFacing + sway : BodyFacing;
        batch.DrawRotated(Rig.Torso, Position, torsoRot, SpriteScale * bob, tint);

        // 3) arms + weapon: exact aim; recoil / reload / swing offsets are sprite-local (+X forward)
        var armsLocal = ArmsOffset + new Vector2(-RecoilKick / SpriteScale, 0f);
        float armsRot = Facing + ArmsAngle + (IsAlive ? 0f : 1.1f);
        var armsPos = Position + MathUtil.Rotate(armsLocal * SpriteScale, armsRot);
        batch.DrawRotated(Rig.ArmsFor(HeldWeapon), armsPos, armsRot, SpriteScale, tint);
        foreach (var (sprite, local) in ArmOverlays)
            batch.DrawRotated(sprite, Position + MathUtil.Rotate((armsLocal + local) * SpriteScale, armsRot), armsRot, SpriteScale, tint);

        // 4) head: aims (leads the torso), bobs with the gait; flops sideways when dead
        float headRot = Facing + HeadTurn + (IsAlive ? 0f : -0.9f);
        var headPos = Position + MathUtil.Rotate(new Vector2(IsAlive ? 0f : -3f, IsAlive ? 0f : 4f) * SpriteScale, Facing);
        float headBob = 1f + 0.04f * MathF.Sin(StridePhase * 2f + 0.5f) * moving;
        batch.DrawRotated(Rig.Head, headPos, headRot, SpriteScale * headBob, tint);
    }

    /// <summary>World-space point of a sprite-local offset (texels; +X forward, +Y right) after rotation and scale.</summary>
    public Vector2 LocalToWorld(Vector2 localTexels) => Position + MathUtil.Rotate(localTexels * SpriteScale, Facing);

    /// <summary>World-space point on the ARM layer (follows recoil / reload / swing animation).</summary>
    public Vector2 WeaponLocalToWorld(Vector2 localTexels)
    {
        float rot = Facing + ArmsAngle;
        return Position + MathUtil.Rotate((localTexels + ArmsOffset - new Vector2(RecoilKick / SpriteScale, 0f)) * SpriteScale, rot);
    }
}
