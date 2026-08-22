using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using Game.Combat;
using Game.Core;
using Game.Graphics;
using Game.Items;
using Game.Meta;
using Game.World;

namespace Game.Entities;

/// <summary>Something the player can search: a title plus an inventory. Bodies and crates.</summary>
public sealed class LootSource
{
    public required string Title;
    public required Inventory Items;
    public Vector2 Position;
    public Action? OnClosed;
    public bool IsEmpty => Items.IsEmpty;
}

/// <summary>
/// The controllable character: twin-stick movement/aim, up to 3 weapons (guns with attachments or a melee bat) with
/// magazine reloads, helmet + vest gear, grenades, inventory & hotbar, looting bodies and crates, torch/laser
/// attachments, lantern light, running dust, death.
/// </summary>
public sealed class Player : Character
{
    public const float WalkSpeed = 330f, SprintSpeed = 560f, Accel = 2600f, Friction = 9f;
    public const float LootReach = 70f;
    public const int MaxWeapons = 3;

    public Inventory Inventory { get; } = new();
    public List<Weapon> Weapons { get; } = new();
    public int WeaponIndex { get; private set; }
    public Weapon CurrentWeapon => Weapons[WeaponIndex];
    public ItemType? Helmet { get; private set; }
    public ItemType? Vest { get; private set; }
    public float Armor;                               // vest durability
    public float MaxArmor => Vest is { } v ? GearDef.For(v)!.MaxArmor : 0f;
    public bool IsSprinting { get; private set; }
    public bool InventoryOpen;
    public bool TacticalOn = true;                    // torch/laser attachments on the current weapon ([T] toggles)
    private bool _triggerLocked = true;               // LMB must be released after UI close (or spawn) before it fires
    public LootSource? OpenLoot;
    public float RespawnTimer;
    public bool RespawnEnabled = true;
    public Vector2 SpawnPoint;
    public string? Toast; public float ToastTimer;
    public Crate? NearbyLootable;
    public Enemy? NearbyBody;
    public Pickup? NearbyPickup;
    public float DamageFlash;
    public int Gold;
    public bool BotMode;
    public event Action<Vector2, string>? Interacted;
    public event Action<LootSource>? LootRequested;

    private readonly GraphicsDevice _gd;
    private readonly PointLight _lantern, _muzzle, _torch;
    private readonly Dictionary<ItemType, SpritePair> _attachArt;
    private readonly SpritePair _headCap, _headHelmet, _torsoBare, _torsoLight, _torsoHeavy;
    private float _dustAccumulator, _swapAnim, _meleeTimer, _meleeCooldown, _botGrenadeTimer = 1.5f;
    private bool _meleeHitDone;
    private const float MeleeSwingTime = 0.32f, MeleeHitAt = 0.14f;

    public Player(GraphicsDevice gd, Vector2 spawn, LightManager lights, Loadout? loadout = null, Dictionary<ItemType, SpritePair>? attachArt = null)
    {
        _gd = gd; _attachArt = attachArt ?? new();
        Faction = Faction.Player; SpawnPoint = spawn; Position = spawn;
        var style = CharacterStyle.Player with { Vest = false, Head = HeadGear.Cap };
        Rig = CharacterArt.CreateRig(gd, style, new[] { HeldWeapon.Rifle, HeldWeapon.Pistol, HeldWeapon.Smg, HeldWeapon.Shotgun, HeldWeapon.Bat });
        _headCap = Rig.Head; _torsoBare = Rig.Torso;
        _headHelmet = CharacterArt.CreateHead(gd, style with { Head = HeadGear.Helmet });
        _torsoLight = CharacterArt.CreateTorso(gd, style with { Vest = true, VestColor = new Vector3(0.30f, 0.34f, 0.26f) });
        _torsoHeavy = CharacterArt.CreateTorso(gd, style with { Vest = true, VestColor = new Vector3(0.12f, 0.12f, 0.13f) });

        if (loadout != null && loadout.HasWeapon)
        {
            foreach (var wl in loadout.Weapons)
            {
                var w = new Weapon(wl.Def);
                foreach (var kv in wl.Attachments) w.Attachments[kv.Key] = kv.Value;
                Weapons.Add(w);
            }
            Inventory.CopyFrom(loadout.Bag);
            Helmet = loadout.Helmet; Vest = loadout.Vest; Armor = MaxArmor;
        }
        else
        {
            Weapons.Add(new Weapon(WeaponDef.Rifle)); Weapons.Add(new Weapon(WeaponDef.Pistol));
            Inventory.Add(ItemType.RifleMag, 2); Inventory.Add(ItemType.PistolMag, 2); Inventory.Add(ItemType.Bandage, 2); Inventory.Add(ItemType.Grenade, 2);
        }
        HeldWeapon = CurrentWeapon.Def.Held;
        RefreshVisuals();
        Inventory.ItemAdded += s => ShowToast($"+{s.Count} {s.Def.Name}");

        _lantern = lights.Add(new PointLight { Height = 110f, Radius = 360f, Intensity = 0.9f, Color = new Vector3(1f, 0.92f, 0.76f) });
        _muzzle  = lights.Add(new PointLight { Height = 40f, Radius = 260f, Intensity = 0f, Color = new Vector3(1f, 0.72f, 0.35f) });
        _torch   = lights.Add(new PointLight { Height = 45f, Radius = 900f, Intensity = 0f, Color = new Vector3(1f, 0.97f, 0.88f), ConeOuterDeg = 24f, ConeInnerDeg = 10f });
    }

    public int SpareMags(Weapon w) => w.Def.Mag is { } t ? Inventory.CountOf(t) : 0;
    public int Grenades => Inventory.CountOf(ItemType.Grenade);
    public void ShowToast(string text) { Toast = text; ToastTimer = 2.2f; }
    public float SpeedMul => Vest is { } v ? GearDef.For(v)!.SpeedMul : 1f;

    // ================================================================================================= update
    public void Update(float dt, GameContext ctx)
    {
        TickCommon(dt);
        DamageFlash = MathF.Max(0f, DamageFlash - dt * 2.5f);
        if (ToastTimer > 0f && (ToastTimer -= dt) <= 0f) Toast = null;
        var input = ctx.Input;

        if (!IsAlive)
        {
            _lantern.Intensity = 0.4f; _muzzle.Intensity = 0f; _torch.Intensity = 0f; Velocity = Vector2.Zero;
            RespawnTimer -= dt;
            if (RespawnEnabled && RespawnTimer <= 0f) Respawn(ctx);
            return;
        }

        // ---- hotkeys (the inventory screen handles its own mouse input) --------------------------------------
        if (!InventoryOpen)
        {
            for (int i = 0; i < Inventory.HotbarSize; i++) if (input.Pressed(Keys.D1 + i)) UseSlot(i, ctx);
            if (input.Pressed(Keys.Q)) SwitchWeapon();
            if (input.Pressed(Keys.R)) TryReload();
            if (input.Pressed(Keys.G)) ThrowGrenade(ctx);
            if (input.Pressed(Keys.T) && (CurrentWeapon.HasTorch || CurrentWeapon.HasLaser))
            {
                TacticalOn = !TacticalOn;
                ShowToast((CurrentWeapon.HasTorch ? "Torch" : "Laser") + (TacticalOn ? " on" : " off"));
            }
        }

        // ---- movement -------------------------------------------------------------------------------------
        var axis = InventoryOpen ? Vector2.Zero : input.MoveAxis();
        IsSprinting = input.AnyDown(Keys.LeftShift, Keys.RightShift) && axis != Vector2.Zero;
        float maxSpeed = (IsSprinting ? SprintSpeed : WalkSpeed) * SpeedMul;
        if (axis != Vector2.Zero) Velocity = MathUtil.Approach(Velocity, axis * maxSpeed, Accel * dt);
        else { Velocity *= MathF.Exp(-Friction * dt); if (Velocity.LengthSquared() < 4f) Velocity = Vector2.Zero; }
        Position += Velocity * dt;
        ctx.World.ResolveCircle(ref Position, ref Velocity, Radius);
        foreach (var e in ctx.Enemies.Alive) { var ep = e.Position; Collision.SeparateCircles(ref Position, Radius, ref ep, e.Radius); e.Position = ep; }

        // ---- aim ------------------------------------------------------------------------------------------
        Enemy? botTarget = null;
        if (BotMode)
        {
            float best = float.MaxValue;
            foreach (var e in ctx.Enemies.Alive) { float d = (e.Position - Position).LengthSquared(); if (d < best) { best = d; botTarget = e; } }
            if (botTarget != null) Facing = MathUtil.LerpAngle(Facing, MathUtil.ToAngle(botTarget.Position - Position), MathUtil.Damp(25f, dt));
            _botGrenadeTimer -= dt;
            if (botTarget != null && _botGrenadeTimer <= 0f && (botTarget.Position - Position).Length() < 420f && Grenades > 0) { ThrowGrenade(ctx); _botGrenadeTimer = 4f; }
        }
        else if (input.MouseInWindow && !InventoryOpen)
        {
            var toAim = ctx.Camera.ScreenToWorld(input.MouseScreen) - Position;
            if (toAim.LengthSquared() > 4f) Facing = MathUtil.LerpAngle(Facing, MathUtil.ToAngle(toAim), MathUtil.Damp(25f, dt));
        }
        else if (Velocity.LengthSquared() > 100f) Facing = MathUtil.LerpAngle(Facing, MathUtil.ToAngle(Velocity), MathUtil.Damp(14f, dt));

        // ---- weapon ---------------------------------------------------------------------------------------
        var w = CurrentWeapon; w.Update(dt);
        if (InventoryOpen) _triggerLocked = true;
        else if (!input.LeftDown) _triggerLocked = false;
        bool trigger = ((input.LeftDown && !_triggerLocked) || (BotMode && botTarget != null)) && !InventoryOpen;
        if (w.Def.IsMelee) UpdateMelee(dt, trigger, ctx);
        else
        {
            int used = w.FinishReloadIfDue(SpareMags(w));
            if (used > 0 && w.Def.Mag is { } magType) Inventory.Remove(magType, used);
            if (trigger && w.AmmoInMag <= 0 && !w.IsReloading) TryReload(quiet: !input.LeftPressed);
            if (w.TryFire(trigger, Facing, IsSprinting, infiniteAmmo: false, out float ang))
            {
                var muzzle = MuzzleWorld();
                for (int i = 0; i < w.Def.Pellets; i++)
                {
                    var dir = MathUtil.FromAngle(w.PelletAngle(ang, i));
                    ctx.Projectiles.Spawn(muzzle, dir * w.Def.BulletSpeed + Velocity * 0.2f, w.Def.Damage, w.Def.MaxRicochets, Faction.Player, w.Def.Tracer, w.Def.Pellets > 1 ? 0.5f : 1.6f);
                }
                var fdir = MathUtil.FromAngle(ang);
                ctx.Particles.Sparks(muzzle, fdir, (int)(6 * w.FlashMul) + 1, new Vector3(1.4f, 0.9f, 0.4f), 350f, 0.9f, 0.18f);
                ctx.Particles.Puff(muzzle, fdir, 2, new Vector3(0.30f, 0.28f, 0.26f), 50f, 10f, 0.6f);
                RecoilKick = (w.Def.Pellets > 1 ? 9f : 5f) * w.RecoilMul;
                ctx.Enemies.AlertNear(Position, 720f * w.NoiseMul);           // gunshots draw attention (suppressor: less)
            }
            _muzzle.Position = MuzzleWorld(); _muzzle.Intensity = w.Flash * w.Def.MuzzleFlash * w.FlashMul;
        }
        // torch attachment: cone light from the muzzle along the aim
        bool torch = !w.Def.IsMelee && w.HasTorch && TacticalOn;
        _torch.Intensity = torch ? 2.4f : 0f;
        if (torch) { _torch.Position = MuzzleWorld(); _torch.Direction = MathUtil.FromAngle(Facing + ArmsAngle); }
        if (!w.Def.IsMelee && w.HasLaser && TacticalOn) DrawLaser(ctx);
        _lantern.Position = Position; _lantern.Height = 110f + 4f * MathF.Sin(StridePhase); _lantern.Intensity = 0.9f;
        AnimateArms(dt, w);

        // ---- interaction: bodies first, then the nearest of floor item / crate ------------------------------
        NearbyBody = ctx.Enemies.FindLootableBodyNear(Position, LootReach + 10f);
        NearbyPickup = NearbyBody == null ? ctx.Pickups.FindNearest(Position, LootReach) : null;
        NearbyLootable = NearbyBody == null ? ctx.World.FindLootableNear(Position, LootReach) : null;
        if (NearbyPickup != null && NearbyLootable != null)
        {
            if ((NearbyPickup.Position - Position).LengthSquared() <= (NearbyLootable.Center - Position).LengthSquared()) NearbyLootable = null;
            else NearbyPickup = null;
        }
        if (BotMode && NearbyPickup != null) ctx.Pickups.TryCollect(NearbyPickup, Collect);   // bots still hoover
        if (!InventoryOpen && input.Pressed(Keys.E))
        {
            if (NearbyBody != null) SearchBody(NearbyBody);
            else if (NearbyPickup != null) ctx.Pickups.TryCollect(NearbyPickup, Collect);
            else if (NearbyLootable != null) OpenCrate(NearbyLootable);
        }
        if (OpenLoot != null && (OpenLoot.Position - Position).Length() > LootReach + 90f) CloseLoot();

        // ---- dust -----------------------------------------------------------------------------------------
        float spd = Speed;
        if (spd > 40f)
        {
            _dustAccumulator += (spd / WalkSpeed) * 50f * dt;
            var back = -MathUtil.SafeNormalize(Velocity);
            while (_dustAccumulator >= 1f)
            {
                _dustAccumulator -= 1f;
                ctx.Particles.Puff(Position + back * Radius * 0.6f + Rng.InCircle(8f), back, 1, new Vector3(0.36f, 0.30f, 0.22f) * (IsSprinting ? 1.4f : 1f), 45f, 9f, 0.6f);
            }
        }
        else _dustAccumulator = 0f;
    }

    public Vector2 MuzzleWorld() => WeaponLocalToWorld(CharacterArt.MuzzleLocal(CurrentWeapon.Def.Held));

    /// <summary>Laser attachment: thin red line from the muzzle to the first obstacle/enemy, plus a dot.</summary>
    private void DrawLaser(GameContext ctx)
    {
        var from = MuzzleWorld(); var dir = MathUtil.FromAngle(Facing + ArmsAngle);
        float len = 900f;
        var to = from + dir * len;
        if (ctx.World.CastSegment(from, to, out float t, out _, out _)) len *= t;
        foreach (var e in ctx.Enemies.Alive)
            if (Collision.SegmentVsCircle(from, from + dir * len, e.Position, e.Radius, out float et)) len *= et;
        var mid = from + dir * (len * 0.5f);
        ctx.Particles.AddQuad(mid, Facing + ArmsAngle, 2.0f, len * 0.5f / 2.0f, new Vector3(1.0f, 0.10f, 0.08f));
        ctx.Particles.AddQuad(from + dir * len, 0f, 5f, 1f, new Vector3(1.0f, 0.25f, 0.2f));
    }

    // ================================================================================================= melee
    private void UpdateMelee(float dt, bool trigger, GameContext ctx)
    {
        var w = CurrentWeapon;
        _meleeCooldown = MathF.Max(0f, _meleeCooldown - dt);
        bool edge = trigger && !w.TriggerWasDown; w.TriggerWasDown = trigger;
        if (edge && _meleeTimer <= 0f && _meleeCooldown <= 0f) { _meleeTimer = MeleeSwingTime; _meleeHitDone = false; }
        if (_meleeTimer > 0f)
        {
            _meleeTimer -= dt;
            if (!_meleeHitDone && _meleeTimer <= MeleeSwingTime - MeleeHitAt)
            {
                _meleeHitDone = true; _meleeCooldown = w.Def.FireInterval;
                var fwd = MathUtil.FromAngle(Facing); int hits = 0;
                foreach (var e in ctx.Enemies.Alive)
                {
                    var d = e.Position - Position; float dist = d.Length();
                    if (dist > w.Def.Range + Radius + e.Radius) continue;
                    if (Vector2.Dot(MathUtil.SafeNormalize(d), fwd) < MathF.Cos(MathHelper.ToRadians(50f))) continue;   // 100 deg arc
                    e.TakeDamage(w.Def.Damage, MathUtil.SafeNormalize(d), e.Position);
                    e.Velocity += MathUtil.SafeNormalize(d) * 220f;
                    ctx.Particles.Sparks(e.Position - MathUtil.SafeNormalize(d) * e.Radius * 0.5f, MathUtil.SafeNormalize(d), 6, new Vector3(1.2f, 1.1f, 0.9f), 200f, 1.5f, 0.25f);
                    hits++;
                }
                if (hits == 0) ctx.Particles.Puff(Position + fwd * 40f, fwd, 2, new Vector3(0.3f, 0.28f, 0.25f), 40f, 8f, 0.4f);   // whiff
                RecoilKick = 4f;
            }
        }
        _muzzle.Intensity = 0f;
    }

    private void AnimateArms(float dt, Weapon w)
    {
        _swapAnim = MathF.Max(0f, _swapAnim - dt * 4f);
        Vector2 offset = Vector2.Zero; float angle = 0f;
        if (w.Def.IsMelee)
        {
            if (_meleeTimer > 0f)
            {
                float p = 1f - _meleeTimer / MeleeSwingTime;                // 0 -> 1 over the swing
                angle += p < 0.4f ? -1.0f * (p / 0.4f) : MathHelper.Lerp(-1.0f, 1.1f, (p - 0.4f) / 0.6f);
                offset += new Vector2(-2f, 0f) * (p < 0.4f ? p / 0.4f : 1f - (p - 0.4f) / 0.6f);
            }
            else angle += -0.3f;                                                 // carry
        }
        else if (w.IsReloading)
        {
            float p = 1f - w.ReloadTimer / w.Def.ReloadTime, k = MathF.Sin(p * MathF.PI);
            offset += new Vector2(-4f, 4f) * k; angle += -0.35f * k;
        }
        if (_swapAnim > 0f) { offset += new Vector2(-5f, 3f) * _swapAnim; angle += 0.5f * _swapAnim; }
        if (IsSprinting) { float k = MathHelper.Clamp(Speed / SprintSpeed, 0f, 1f); offset += new Vector2(-2f, 1f) * k; angle += 0.18f * k; }
        if (InventoryOpen) { offset += new Vector2(-3f, 2f); angle += 0.35f; }
        float damp = _meleeTimer > 0f ? 40f : 18f;
        ArmsOffset = Vector2.Lerp(ArmsOffset, offset, MathUtil.Damp(damp, dt));
        ArmsAngle = MathHelper.Lerp(ArmsAngle, angle, MathUtil.Damp(damp, dt));
        HeadTurn = MathHelper.Lerp(HeadTurn, 0f, MathUtil.Damp(10f, dt));
    }

    // ================================================================================================= visuals
    /// <summary>Swaps head/torso layers for worn gear and rebuilds the attachment overlays for the current weapon.</summary>
    public void RefreshVisuals()
    {
        Rig.Head = Helmet != null ? _headHelmet : _headCap;
        Rig.Torso = Vest switch { ItemType.VestHeavy => _torsoHeavy, ItemType.VestLight => _torsoLight, _ => _torsoBare };
        ArmOverlays.Clear();
        var w = CurrentWeapon;
        foreach (var kv in w.Attachments)
            if (AttachPoints.Get(w.Def.Held, kv.Key) is { } local && _attachArt.TryGetValue(kv.Value, out var art)) ArmOverlays.Add((art, local));
    }

    // ================================================================================================= weapons
    public void SwitchWeapon() { if (Weapons.Count >= 2) SelectWeapon((WeaponIndex + 1) % Weapons.Count); }

    public void SelectWeapon(int index)
    {
        if (index < 0 || index >= Weapons.Count || index == WeaponIndex) return;
        WeaponIndex = index; HeldWeapon = CurrentWeapon.Def.Held; _swapAnim = 1f; _meleeTimer = 0f;
        CurrentWeapon.TriggerWasDown = true;
        RefreshVisuals();
        ShowToast(CurrentWeapon.Def.Name);
    }

    private void TryReload(bool quiet = false)
    {
        var w = CurrentWeapon; if (w.Def.IsMelee) return;
        if (!w.BeginReload(SpareMags(w)) && SpareMags(w) <= 0 && w.AmmoInMag < w.Def.MagSize && !quiet)
            ShowToast($"No {(w.Def.Mag is { } m ? ItemDef.Get(m).Name : "ammo")}!");
    }

    public bool EquipFromSlot(int slot)
    {
        var stack = Inventory[slot];
        if (stack.IsEmpty || !stack.Def.IsWeapon) return false;
        var def = WeaponDef.ForGunItem(stack.Type);
        Inventory.ConsumeFromSlot(slot);
        if (Weapons.Count < MaxWeapons) { Weapons.Add(new Weapon(def, fullMag: true)); SelectWeapon(Weapons.Count - 1); }
        else
        {
            var old = CurrentWeapon;
            Weapons[WeaponIndex] = new Weapon(def, fullMag: true);
            HeldWeapon = def.Held; _swapAnim = 1f;
            if (old.Def.GunItem is { } gi && Inventory.Add(gi, 1) > 0) ShowToast($"Bag full - lost {old.Def.Name}");
            foreach (var a in old.Attachments.Values) Inventory.Add(a, 1);
            RefreshVisuals();
        }
        ShowToast($"Equipped {def.Name}");
        return true;
    }

    /// <summary>Equips a gun from a bag slot into a specific weapon slot (drag &amp; drop). Replaces what's there.</summary>
    public bool EquipFromSlotAt(int slot, int weaponIndex)
    {
        var stack = Inventory[slot];
        if (stack.IsEmpty || !stack.Def.IsWeapon) return false;
        if (weaponIndex < 0 || weaponIndex >= Weapons.Count) return EquipFromSlot(slot);   // empty slot → append
        var def = WeaponDef.ForGunItem(stack.Type);
        Inventory.ConsumeFromSlot(slot);
        var old = Weapons[weaponIndex];
        Weapons[weaponIndex] = new Weapon(def, fullMag: true);
        if (old.Def.GunItem is { } gi && Inventory.Add(gi, 1) > 0) ShowToast($"Bag full - lost {old.Def.Name}");
        foreach (var a in old.Attachments.Values) Inventory.Add(a, 1);
        if (weaponIndex == WeaponIndex) { HeldWeapon = def.Held; _swapAnim = 1f; }
        RefreshVisuals(); ShowToast($"Equipped {def.Name}");
        return true;
    }

    /// <summary>Swaps two weapon slots (drag &amp; drop), keeping the selection on the same weapon.</summary>
    public void ReorderWeapons(int a, int b)
    {
        if (a == b || a < 0 || b < 0 || a >= Weapons.Count || b >= Weapons.Count) return;
        (Weapons[a], Weapons[b]) = (Weapons[b], Weapons[a]);
        if (WeaponIndex == a) WeaponIndex = b; else if (WeaponIndex == b) WeaponIndex = a;
        RefreshVisuals();
    }

    public bool Unequip(int weaponIndex)
    {
        if (Weapons.Count <= 1 || weaponIndex < 0 || weaponIndex >= Weapons.Count) return false;
        var w = Weapons[weaponIndex];
        if (w.Def.GunItem is not { } gi) return false;
        if (Inventory.Add(gi, 1) > 0) { ShowToast("Bag full"); return false; }
        foreach (var a in w.Attachments.Values) if (Inventory.Add(a, 1) > 0) ShowToast($"Bag full - lost {ItemDef.Get(a).Name}");
        Weapons.RemoveAt(weaponIndex);
        WeaponIndex = Math.Min(WeaponIndex, Weapons.Count - 1); HeldWeapon = CurrentWeapon.Def.Held; _swapAnim = 1f;
        RefreshVisuals();
        return true;
    }

    /// <summary>Fits an attachment from a bag slot onto the current weapon (displaced one goes back to the bag).</summary>
    public bool AttachFromSlot(int slot) => AttachFromSlotTo(slot, WeaponIndex);

    /// <summary>Fits an attachment from a bag slot onto a specific weapon (drag &amp; drop).</summary>
    public bool AttachFromSlotTo(int slot, int weaponIndex)
    {
        if (weaponIndex < 0 || weaponIndex >= Weapons.Count) return false;
        var stack = Inventory[slot];
        if (stack.IsEmpty || !stack.Def.IsAttachment) return false;
        var w = Weapons[weaponIndex];
        if (w.Def.IsMelee || !w.TryAttach(stack.Type, out var replaced)) { ShowToast($"{stack.Def.Name} does not fit the {w.Def.Name}"); return false; }
        Inventory.ConsumeFromSlot(slot);
        if (replaced is { } r) Inventory.Add(r, 1);
        RefreshVisuals(); ShowToast($"Fitted {stack.Def.Name}");
        return true;
    }

    public bool DetachToBag(int weaponIndex, AttachSlot slot)
    {
        if (weaponIndex < 0 || weaponIndex >= Weapons.Count) return false;
        var w = Weapons[weaponIndex];
        if (!w.Attachments.TryGetValue(slot, out var t)) return false;
        if (Inventory.Add(t, 1) > 0) { ShowToast("Bag full"); return false; }
        w.Detach(slot); RefreshVisuals(); return true;
    }

    // ================================================================================================= gear
    public bool WearFromSlot(int slot)
    {
        var stack = Inventory[slot];
        var def = stack.IsEmpty ? null : GearDef.For(stack.Type);
        if (def == null) return false;
        Inventory.ConsumeFromSlot(slot);
        ItemType? old = def.Slot == GearSlot.Helmet ? Helmet : Vest;
        if (def.Slot == GearSlot.Helmet) Helmet = stack.Type; else { Vest = stack.Type; Armor = def.MaxArmor; }
        if (old is { } o) Inventory.Add(o, 1);
        RefreshVisuals(); ShowToast($"Wearing {stack.Def.Name}");
        return true;
    }

    public bool Unwear(GearSlot gs)
    {
        ItemType? t = gs == GearSlot.Helmet ? Helmet : Vest;
        if (t is not { } it) return false;
        if (Inventory.Add(it, 1) > 0) { ShowToast("Bag full"); return false; }
        if (gs == GearSlot.Helmet) Helmet = null; else { Vest = null; Armor = 0f; }
        RefreshVisuals(); return true;
    }

    // ================================================================================================= items
    public bool UseSlot(int slot, GameContext ctx)
    {
        var stack = Inventory[slot];
        if (stack.IsEmpty) return false;
        if (stack.Def.IsWeapon) return EquipFromSlot(slot);
        if (stack.Def.IsGear) return WearFromSlot(slot);
        if (stack.Def.IsAttachment) return AttachFromSlot(slot);
        if (stack.Type == ItemType.Grenade) { ThrowGrenade(ctx); return true; }
        if (!stack.Def.Usable) { ShowToast($"{stack.Def.Name}: {stack.Def.Description}"); return false; }
        bool used = false;
        switch (stack.Type)
        {
            case ItemType.Medkit:     if (Health < MaxHealth) { Health = MathF.Min(MaxHealth, Health + 60f); used = true; } break;
            case ItemType.Bandage:    if (Health < MaxHealth) { Health = MathF.Min(MaxHealth, Health + 20f); used = true; } break;
            case ItemType.ArmorPlate:
                if (Vest == null) { ShowToast("Armor plate needs a vest"); return false; }
                if (Armor < MaxArmor) { Armor = MathF.Min(MaxArmor, Armor + 50f); used = true; } break;
        }
        if (used)
        {
            Inventory.ConsumeFromSlot(slot);
            ctx.Particles.Sparks(Position, new Vector2(0, -1), 10, stack.Def.Tint * 1.4f, 90f, 3.1f, 0.5f);
            ShowToast($"Used {stack.Def.Name}");
        }
        else ShowToast($"{stack.Def.Name} not needed");
        return used;
    }

    public void ThrowGrenade(GameContext ctx)
    {
        if (!IsAlive || Inventory.CountOf(ItemType.Grenade) <= 0) { ShowToast("No grenades"); return; }
        Inventory.Remove(ItemType.Grenade, 1);
        var aim = BotMode || !ctx.Input.MouseInWindow ? Position + MathUtil.FromAngle(Facing) * 300f : ctx.Camera.ScreenToWorld(ctx.Input.MouseScreen);
        var d = aim - Position; float dist = d.Length(); var dir = MathUtil.SafeNormalize(d);
        float speed = MathHelper.Clamp(dist * 2.1f, 180f, 760f);            // slide friction ~2.2/s: lands near the cursor
        ctx.Grenades.Throw(Position + dir * (Radius + 6f), dir * speed + Velocity * 0.3f, Faction.Player);
        RecoilKick = 3f; ShowToast($"Grenade out! ({Inventory.CountOf(ItemType.Grenade)} left)");
    }

    public void DropSlot(int slot, GameContext ctx)
    {
        var stack = Inventory[slot];
        if (stack.IsEmpty) return;
        Inventory.Remove(stack.Type, stack.Count);
        ctx.Pickups.SpawnBurst(Position + MathUtil.FromAngle(Facing) * 30f, new[] { stack }, 90f);
        ShowToast($"Dropped {stack.Def.Name} x{stack.Count}");
    }

    public int Collect(ItemStack s)
    {
        if (s.Type == ItemType.Coin) { Gold += s.Count; ShowToast($"+{s.Count} gold"); return 0; }
        return Inventory.Add(s.Type, s.Count);
    }

    // ================================================================================================= looting
    private void SearchBody(Enemy body)
    {
        var src = new LootSource { Title = $"BODY: {body.Def.Name.ToUpperInvariant()}", Items = body.Loot, Position = body.Position, OnClosed = body.MarkLooted };
        OpenLoot = src; LootRequested?.Invoke(src);
        Interacted?.Invoke(body.Position, "Searched body");
    }

    private void OpenCrate(Crate crate)
    {
        crate.Opened = true;
        var src = new LootSource { Title = PropDefs.Name(crate.Kind), Items = crate.EnsureContents(), Position = crate.Center };
        OpenLoot = src; LootRequested?.Invoke(src);
        Interacted?.Invoke(crate.Center, "Opened container");
    }

    public void CloseLoot() { if (OpenLoot == null) return; OpenLoot.OnClosed?.Invoke(); OpenLoot = null; }

    // ================================================================================================= damage / death
    public override void TakeDamage(float amount, Vector2 hitDir, Vector2 hitPos)
    {
        if (!IsAlive) return;
        if (Helmet is { } h && GearDef.For(h) is { } hd) amount *= 1f - hd.DamageReduction;
        if (Vest is { } v && GearDef.For(v) is { } vd && Armor > 0f)
        {
            float absorbed = MathF.Min(Armor, amount * vd.Absorb); Armor -= absorbed; amount -= absorbed;
            if (Armor <= 0f) ShowToast("Vest is shredded!");
        }
        DamageFlash = MathF.Min(1f, DamageFlash + 0.6f);
        base.TakeDamage(amount, hitDir, hitPos);
    }

    protected override void OnDeath() { RespawnTimer = 3f; InventoryOpen = false; CloseLoot(); }

    private void Respawn(GameContext ctx)
    {
        Health = MaxHealth; Armor = MaxArmor; DeadTimer = 0f; HitFlash = 0f;
        Position = SpawnPoint; Velocity = Vector2.Zero;
        foreach (var w in Weapons) w.AmmoInMag = w.Def.MagSize;
        if (CurrentWeapon.Def.Mag is { } m && Inventory.CountOf(m) < 1) Inventory.Add(m, 1);
        ShowToast("Respawned");
        ctx.Enemies.ResetAggro();
    }
}
