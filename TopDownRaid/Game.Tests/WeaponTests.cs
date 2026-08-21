using System;
using Game.Combat;
using Game.Core;
using Game.Graphics;
using Game.Items;
using Xunit;

namespace Game.Tests;

public class WeaponTests
{
    [Fact]
    public void SemiAuto_FiresOnlyOnTriggerEdge()
    {
        var w = new Weapon(WeaponDef.Pistol);
        Assert.True(w.TryFire(true, 0f, false, true, out _));
        w.Update(1f);                                              // cooldown gone
        Assert.False(w.TryFire(true, 0f, false, true, out _));     // still held → no edge
        Assert.False(w.TryFire(false, 0f, false, true, out _));
        Assert.True(w.TryFire(true, 0f, false, true, out _));      // new press
    }

    [Fact]
    public void Automatic_FiresWhileHeld_RespectingCooldown()
    {
        var w = new Weapon(WeaponDef.Rifle);
        Assert.True(w.TryFire(true, 0f, false, true, out _));
        Assert.False(w.TryFire(true, 0f, false, true, out _));     // cooldown
        w.Update(WeaponDef.Rifle.FireInterval + 0.01f);
        Assert.True(w.TryFire(true, 0f, false, true, out _));
    }

    [Fact]
    public void EnemyWeapons_AreAllAutomatic_SoTheAiKeepsFiring()
    {
        foreach (var d in WeaponDef.EnemyPool) Assert.True(d.Automatic, d.Name);
    }

    [Fact]
    public void Magazine_Empties_And_ReloadConsumesOneMag()
    {
        var w = new Weapon(WeaponDef.Pistol);
        for (int i = 0; i < 12; i++) { w.Update(1f); Assert.True(w.TryFire(true, 0f, false, false, out _)); w.TriggerWasDown = false; }
        w.Update(1f);
        Assert.Equal(0, w.AmmoInMag);
        Assert.False(w.TryFire(true, 0f, false, false, out _));
        Assert.False(w.BeginReload(spareMags: 0));
        Assert.True(w.BeginReload(spareMags: 2));
        Assert.True(w.IsReloading);
        Assert.Equal(0, w.FinishReloadIfDue(2));                  // not yet
        w.Update(WeaponDef.Pistol.ReloadTime + 0.01f);
        Assert.Equal(1, w.FinishReloadIfDue(2));                  // one magazine consumed
        Assert.Equal(WeaponDef.Pistol.MagSize, w.AmmoInMag);
        Assert.Equal(0, w.FinishReloadIfDue(2));                  // idempotent
    }

    [Fact]
    public void Reload_DoesNotStart_WhenFull()
    {
        var w = new Weapon(WeaponDef.Rifle);
        Assert.False(w.BeginReload(5));
    }

    [Fact]
    public void Pellets_FanEvenlyInsideSpread()
    {
        Rng.Seed(1);
        var w = new Weapon(WeaponDef.Shotgun);
        float maxDev = 0f;
        for (int i = 0; i < WeaponDef.Shotgun.Pellets; i++) maxDev = MathF.Max(maxDev, MathF.Abs(w.PelletAngle(0.5f, i) - 0.5f));
        Assert.True(maxDev <= WeaponDef.Shotgun.PelletSpread * 1.3f, $"deviation {maxDev}");
        Assert.Equal(0.5f, new Weapon(WeaponDef.Rifle).PelletAngle(0.5f, 0));
    }

    [Fact]
    public void Attachments_OnlyFitAllowedSlots_AndStackMultipliers()
    {
        var rifle = new Weapon(WeaponDef.Rifle);
        Assert.True(rifle.TryAttach(ItemType.Optic, out var replaced)); Assert.Null(replaced);
        Assert.True(rifle.TryAttach(ItemType.Compensator, out _));
        Assert.True(rifle.TryAttach(ItemType.Grip, out _));
        Assert.True(rifle.TryAttach(ItemType.Suppressor, out replaced));  // same slot as compensator
        Assert.Equal(ItemType.Compensator, replaced);
        Assert.Equal(0.7f * 0.95f, rifle.SpreadMul, 4);
        Assert.Equal(0.70f, rifle.RecoilMul, 4);
        Assert.Equal(0.15f, rifle.FlashMul, 4);
        Assert.Equal(80f, rifle.RangeAdd);
        Assert.False(rifle.HasTorch);

        var pistol = new Weapon(WeaponDef.Pistol);
        Assert.False(pistol.TryAttach(ItemType.Grip, out _));        // pistols have no grip slot
        Assert.True(pistol.TryAttach(ItemType.Torch, out _));
        Assert.True(pistol.HasTorch);
        Assert.Equal(ItemType.Torch, pistol.Detach(AttachSlot.Tactical));
        Assert.Null(pistol.Detach(AttachSlot.Tactical));
    }

    [Fact]
    public void Spread_IsScaledByAttachments()
    {
        Rng.Seed(5);
        var bare = new Weapon(WeaponDef.Smg); var tuned = new Weapon(WeaponDef.Smg);
        tuned.TryAttach(ItemType.Optic, out _); tuned.TryAttach(ItemType.Laser, out _);
        float maxBare = 0f, maxTuned = 0f;
        for (int i = 0; i < 300; i++)
        {
            bare.Update(1f); tuned.Update(1f);
            bare.TryFire(true, 0f, false, true, out float a); tuned.TryFire(true, 0f, false, true, out float b);
            maxBare = MathF.Max(maxBare, MathF.Abs(a)); maxTuned = MathF.Max(maxTuned, MathF.Abs(b));
        }
        Assert.True(maxTuned < maxBare, $"{maxTuned} vs {maxBare}");
        Assert.True(maxBare <= WeaponDef.Smg.Spread + 1e-4f);
    }

    [Fact]
    public void EveryGunItem_RoundTripsToItsDef()
    {
        foreach (var def in WeaponDef.All)
        {
            Assert.NotNull(def.GunItem);
            Assert.Same(def, WeaponDef.ForGunItem(def.GunItem!.Value));
            Assert.Equal(def.IsMelee, def.Mag == null);
        }
    }

    [Fact]
    public void AttachPoints_ExistForEveryDefinedAttachmentOnAtLeastOneGun()
    {
        foreach (var ad in AttachmentDef.All)
        {
            bool any = false;
            foreach (HeldWeapon h in Enum.GetValues<HeldWeapon>()) any |= AttachPoints.Allows(h, ad.Slot);
            Assert.True(any, ad.Item.ToString());
        }
    }

    [Fact]
    public void GearDefs_AreConsistent()
    {
        foreach (var t in new[] { ItemType.VestLight, ItemType.VestHeavy })
        { var g = GearDef.For(t)!; Assert.Equal(GearSlot.Vest, g.Slot); Assert.True(g.MaxArmor > 0 && g.Absorb > 0 && g.Absorb < 1); }
        foreach (var t in new[] { ItemType.HelmetSteel, ItemType.HelmetTac })
        { var g = GearDef.For(t)!; Assert.Equal(GearSlot.Helmet, g.Slot); Assert.True(g.DamageReduction > 0 && g.DamageReduction < 1); }
        Assert.Null(GearDef.For(ItemType.Medkit));
    }
}
