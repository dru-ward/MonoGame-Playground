using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace Game.Graphics;

public enum HeldWeapon { None, Rifle, Pistol, Bat, Smg, Shotgun }
public enum HeadGear { Hair, Cap, Helmet, Beanie, Hood }

/// <summary>Visual style knobs for a top-down human. Every character (player, enemies) is built from the same rig.</summary>
public sealed record CharacterStyle(
    Vector3 Jacket, Vector3 Sleeve, Vector3 Skin, Vector3 Hair,
    HeldWeapon Weapon,
    HeadGear Head = HeadGear.Hair, bool Backpack = false, bool Vest = false,
    Vector3? VestColor = null, Vector3? Pants = null, Vector3? Gloves = null, Vector3? GearColor = null,
    bool Radio = false, bool Holster = false, bool RolledSleeves = false)
{
    // Player: PMC-style — olive/tan kit, black plate carrier, cap, gloves, backpack, radio, holster
    public static readonly CharacterStyle Player = new(
        Jacket: new(0.40f, 0.38f, 0.27f), Sleeve: new(0.35f, 0.33f, 0.23f), Skin: new(0.88f, 0.70f, 0.55f), Hair: new(0.22f, 0.15f, 0.09f),
        Weapon: HeldWeapon.Rifle, Head: HeadGear.Cap, Backpack: true, Vest: true, VestColor: new Vector3(0.19f, 0.19f, 0.17f),
        Pants: new Vector3(0.26f, 0.25f, 0.21f), Gloves: new Vector3(0.16f, 0.14f, 0.12f), GearColor: new Vector3(0.46f, 0.40f, 0.27f),
        Radio: true, Holster: true);

    // Scav brawler: grey hoodie with the hood up, bare hands, tracksuit pants
    public static readonly CharacterStyle Brawler = new(
        Jacket: new(0.42f, 0.42f, 0.45f), Sleeve: new(0.36f, 0.36f, 0.39f), Skin: new(0.80f, 0.62f, 0.50f), Hair: new(0.09f, 0.08f, 0.08f),
        Weapon: HeldWeapon.Bat, Head: HeadGear.Hood, Vest: false, Pants: new Vector3(0.14f, 0.16f, 0.24f), RolledSleeves: true);

    // Raider gunner: black kit, helmet with NVG mount, plate carrier, gloves, radio
    public static readonly CharacterStyle Gunner = new(
        Jacket: new(0.22f, 0.23f, 0.25f), Sleeve: new(0.18f, 0.19f, 0.21f), Skin: new(0.76f, 0.58f, 0.46f), Hair: new(0.35f, 0.28f, 0.20f),
        Weapon: HeldWeapon.Pistol, Head: HeadGear.Helmet, Vest: true, VestColor: new Vector3(0.13f, 0.13f, 0.14f),
        Pants: new Vector3(0.15f, 0.15f, 0.16f), Gloves: new Vector3(0.11f, 0.11f, 0.11f), GearColor: new Vector3(0.24f, 0.25f, 0.21f), Radio: true);
}

/// <summary>
/// The layered character rig. Every layer is a 96x96 sprite whose centre is the character's position, so each can be
/// rotated independently:  Shadow (under everything) → Boots ×2 (movement direction, striding) →
/// Torso (lags the aim, sways with the stride) → Arms+Weapon (exact aim; recoil / reload / swing animate this layer) →
/// Head (aim, bobs). Weapons are modular: one Arms layer per HeldWeapon.
/// </summary>
public sealed class CharacterRig
{
    public required SpritePair Torso;
    public required SpritePair Head;
    public required SpritePair Boot;
    public required SpritePair Shadow;
    public required Dictionary<HeldWeapon, SpritePair> Arms;
    public SpritePair ArmsFor(HeldWeapon w) => Arms.TryGetValue(w, out var p) ? p : Arms[HeldWeapon.None];
}

/// <summary>Builds the character rig layers from <see cref="ShapeSprite"/> shape lists.</summary>
public static class CharacterArt
{
    public const int LayerSize = 96;     // texels; drawn at Character.SpriteScale
    public const int BootSize = 24;

    // shared palette
    static readonly Vector3 Strap = new(0.10f, 0.10f, 0.10f), GunDark = new(0.12f, 0.12f, 0.14f), GunMid = new(0.24f, 0.24f, 0.27f);
    static readonly Vector3 Stock = new(0.34f, 0.23f, 0.13f), Pack = new(0.24f, 0.20f, 0.14f), Wood = new(0.55f, 0.38f, 0.20f);
    static readonly Vector3 HelmetCol = new(0.30f, 0.33f, 0.24f);

    /// <summary>Sprite-local (+X forward, +Y right) muzzle position for a weapon, in texels from the sprite centre.</summary>
    public static Vector2 MuzzleLocal(HeldWeapon w) => w switch
    {
        HeldWeapon.Rifle   => new Vector2(46f, 0.5f),
        HeldWeapon.Pistol  => new Vector2(30f, 3.0f),
        HeldWeapon.Bat     => new Vector2(30f, 6.0f),
        HeldWeapon.Smg     => new Vector2(31f, 1.5f),
        HeldWeapon.Shotgun => new Vector2(42f, 0.5f),
        _                 => new Vector2(20f, 4.0f),
    };

    /// <summary>Elbow positions the torso's upper arms end at and the arm layer's forearms start from.</summary>
    static readonly Vector2 ElbowR = new(7f, 12.5f), ElbowL = new(7f, -12.5f);

    public static CharacterRig CreateRig(GraphicsDevice gd, CharacterStyle st, IEnumerable<HeldWeapon>? weapons = null)
    {
        var arms = new Dictionary<HeldWeapon, SpritePair>();
        foreach (var w in weapons ?? new[] { st.Weapon }) arms[w] = CreateArms(gd, st, w);
        if (!arms.ContainsKey(HeldWeapon.None)) arms[HeldWeapon.None] = CreateArms(gd, st, HeldWeapon.None);
        return new CharacterRig
        {
            Torso = CreateTorso(gd, st), Head = CreateHead(gd, st), Boot = CreateBoot(gd),
            Shadow = TextureFactory.CreateShadow(gd, 72, 56, 0.5f), Arms = arms,
        };
    }

    private static ShapeSprite NewLayer() => new(LayerSize) { Outline = true, OutlineWidth = 1.2f, MinNormalZ = 0.74f };   // gentle relief: no glare rims

    // ================================================================================================= torso
    /// <summary>Hips, backpack, torso, vest/pouches or strap, radio, holster, shoulders and UPPER arms to the elbows.</summary>
    public static SpritePair CreateTorso(GraphicsDevice gd, CharacterStyle st)
    {
        Vector3 pants = st.Pants ?? new Vector3(0.20f, 0.20f, 0.22f), vest = st.VestColor ?? new Vector3(0.2f, 0.2f, 0.2f);
        Vector3 gear = st.GearColor ?? Pack, jacketHi = st.Jacket * 1.22f;
        var s = NewLayer();

        if (st.Backpack)                                                          // behind the torso (-X)
        {
            s.Box(-21f, -13f, -8f, 13f, gear, 0.55f, 0.35f);
            s.Box(-19f, -6f, -10f, 6f, gear * 0.8f, 0.60f, 0.3f);                 // front pocket
            s.Capsule(-18f, -13f, -18f, 13f, 1.5f, Strap, 0.62f, 0.5f);           // compression strap
            s.Box(-21f, -4f, -8f, 4f, gear * 1.1f, 0.62f, 0.3f);                  // roll on top
        }
        s.Ellipse(-1f, 0f, 9f, 17.5f, pants, 0.55f, 0.8f);                        // hips / thighs peeking out
        s.Ellipse(0f, 0f, 12.5f, 19.5f, st.Jacket, 0.75f, 0.9f);                  // torso: wide across shoulders
        s.Ellipse(-3f, 0f, 7f, 13f, jacketHi, 0.80f, 0.8f, 0.5f);                 // upper back highlight
        if (st.Backpack)                                                          // shoulder straps
        {
            s.Capsule(-6f, -14f, 6f, -10f, 1.8f, Strap, 0.86f, 0.4f);
            s.Capsule(-6f, 14f, 6f, 10f, 1.8f, Strap, 0.86f, 0.4f);
        }
        if (st.Vest)
        {
            s.Ellipse(1f, 0f, 9.5f, 15f, vest, 0.85f, 0.7f);                      // plate carrier
            s.Box(3f, -11f, 8f, -5f, vest * 0.75f, 0.92f, 0.3f);                  // mag pouches
            s.Box(3f, 5f, 8f, 11f, vest * 0.75f, 0.92f, 0.3f);
            s.Box(-2f, -2.5f, 3f, 2.5f, vest * 0.6f, 0.90f, 0.3f);                // admin pouch
            s.Capsule(4f, -8f, 4f, 8f, 0.8f, vest * 1.4f, 0.93f, 0.2f);           // molle row
        }
        else
        {
            s.Box(-2.5f, -20f, 2.5f, 20f, Strap, 0.78f, 0.0f);                    // simple chest strap
        }
        if (st.Radio)
        {
            s.Box(-4f, -12f, 1f, -6f, new Vector3(0.08f, 0.08f, 0.09f), 0.98f, 0.3f);
            s.Capsule(-3f, -12f, -3f, -19f, 0.7f, new Vector3(0.05f, 0.05f, 0.05f), 1.0f, 0.5f);   // antenna
        }
        if (st.Holster) s.Box(-8f, 12f, -1f, 17f, new Vector3(0.09f, 0.08f, 0.08f), 0.72f, 0.3f);
        // shoulders + upper arms to the elbows (forearms live on the arm layer)
        s.Circle(0f, -17f, 7.5f, st.Sleeve, 0.85f, 1.0f);
        s.Circle(0f, 17f, 7.5f, st.Sleeve, 0.85f, 1.0f);
        s.Capsule(0f, -17f, ElbowL.X, ElbowL.Y, 4.4f, st.Sleeve, 0.9f, 1.0f);
        s.Capsule(0f, 17f, ElbowR.X, ElbowR.Y, 4.4f, st.Sleeve, 0.9f, 1.0f);
        if (st.Head == HeadGear.Hood)                                             // hood lies on the shoulders, behind the head
            s.Ellipse(-4f, 0f, 11f, 12.5f, st.Jacket * 0.85f, 1.05f, 0.9f, 0.4f);
        return s.Build(gd, 8f, 0.35f);
    }

    // ================================================================================================= arms
    /// <summary>Forearms from the elbows, hands and the held weapon. This is the layer that recoils/reloads/swings.</summary>
    public static SpritePair CreateArms(GraphicsDevice gd, CharacterStyle st, HeldWeapon weapon)
    {
        Vector3 hand = st.Gloves ?? st.Skin, fore = st.RolledSleeves ? st.Skin * 0.95f : st.Sleeve;
        var s = NewLayer();

        // ---- weapon first (hands go on top) ---------------------------------------------------------------
        switch (weapon)
        {
            case HeldWeapon.Rifle:
                s.Box(-6f, -1.5f, 10f, 3.5f, Stock, 0.55f, 0.3f);                                 // stock
                s.Box(-6f, 0f, -2f, 5f, Stock * 0.8f, 0.58f, 0.3f);                                // butt pad
                s.Box(10f, -2.5f, 32f, 3.0f, GunMid, 0.62f, 0.3f);                                 // receiver / handguard
                s.Box(12f, -4.8f, 22f, -2.5f, GunDark, 0.72f, 0.3f);                               // rail + optic
                s.Box(15f, -6.5f, 19f, -4.5f, GunDark * 1.4f, 0.78f, 0.5f);
                s.Box(19f, 3.0f, 24f, 9.0f, GunDark, 0.60f, 0.3f);                                 // magazine
                s.Box(26f, 3.0f, 29f, 6.5f, GunDark, 0.60f, 0.3f);                                 // fore-grip
                s.Capsule(32f, 0.5f, 46f, 0.5f, 2.0f, GunDark, 0.62f, 0.9f);                       // barrel
                s.Capsule(43f, 0.5f, 46f, 0.5f, 2.7f, GunMid, 0.66f, 0.9f);                        // muzzle brake
                s.Capsule(-2f, 4f, 24f, 8f, 0.9f, Strap, 0.5f, 0.5f);                              // sling
                break;
            case HeldWeapon.Pistol:
                s.Box(16f, 1.0f, 30f, 5.0f, GunDark, 0.62f, 0.3f);                                 // slide
                s.Box(17f, 0.2f, 29f, 1.8f, GunMid, 0.66f, 0.3f);                                  // slide serrations highlight
                s.Box(15f, 4.5f, 19f, 9.5f, GunMid, 0.60f, 0.3f);                                  // grip
                s.Capsule(28f, 3.0f, 30f, 3.0f, 1.6f, GunMid, 0.66f, 0.9f);                        // muzzle
                break;
            case HeldWeapon.Bat:
                s.Capsule(10f, 6f, 30f, 6f, 2.6f, Wood, 0.62f, 1.0f);
                s.Capsule(24f, 6f, 31f, 6f, 3.4f, Wood * 1.1f, 0.68f, 1.0f);                        // fat end
                s.Capsule(10f, 6f, 13f, 6f, 2.0f, Strap, 0.62f, 1.0f);                             // grip tape
                for (int i = 0; i < 3; i++) s.Capsule(19f + i * 3f, 3.5f, 20f + i * 3f, 8.5f, 0.6f, Strap, 0.7f, 0.5f);   // nails/wraps
                break;
            case HeldWeapon.Smg:
                s.Box(2f, -1f, 10f, 2.5f, GunDark, 0.55f, 0.3f);                                    // folded wire stock
                s.Box(10f, -2.5f, 27f, 3.0f, GunMid, 0.62f, 0.3f);                                  // receiver
                s.Box(15f, 3.0f, 19f, 12f, GunDark, 0.60f, 0.3f);                                   // long stick mag
                s.Box(12f, -4.5f, 18f, -2.5f, GunDark, 0.70f, 0.3f);                                // sight
                s.Capsule(27f, 1.5f, 31f, 1.5f, 1.8f, GunDark, 0.62f, 0.9f);                       // stubby barrel
                break;
            case HeldWeapon.Shotgun:
                s.Box(-6f, -1.5f, 10f, 3.5f, Stock, 0.55f, 0.3f);                                   // stock
                s.Box(10f, -2.5f, 24f, 3.0f, GunMid, 0.62f, 0.3f);                                  // receiver
                s.Capsule(24f, -0.5f, 42f, -0.5f, 2.2f, GunDark, 0.62f, 0.9f);                     // barrel
                s.Capsule(24f, 2.5f, 36f, 2.5f, 1.8f, GunDark * 1.3f, 0.60f, 0.9f);                // tube
                s.Box(27f, 0.5f, 33f, 5.0f, Stock * 1.1f, 0.68f, 0.5f);                             // pump
                break;
        }
        // ---- forearms + hands: pose per weapon (elbows match the torso layer) --------------------------------
        (Vector2 rHand, Vector2 lHand) = weapon switch
        {
            HeldWeapon.Rifle   => (new Vector2(15f, 4f), new Vector2(26f, -2f)),
            HeldWeapon.Pistol  => (new Vector2(17f, 6f), new Vector2(15f, 1f)),
            HeldWeapon.Bat     => (new Vector2(13f, 7f), new Vector2(9f, -11f)),
            HeldWeapon.Smg     => (new Vector2(14f, 5f), new Vector2(22f, -1f)),
            HeldWeapon.Shotgun => (new Vector2(14f, 4f), new Vector2(29f, 1f)),
            _                 => (new Vector2(11f, 12f), new Vector2(11f, -12f)),
        };
        s.Capsule(ElbowR.X, ElbowR.Y, rHand.X, rHand.Y, 4.0f, fore, 0.95f, 1.0f);
        s.Capsule(ElbowL.X, ElbowL.Y, lHand.X, lHand.Y, 4.0f, fore, 0.95f, 1.0f);
        if (st.RolledSleeves)                                                                     // sleeve cuff at the elbow
        {
            s.Circle(ElbowR.X + 1f, ElbowR.Y - 1f, 4.4f, st.Sleeve, 0.96f, 1.0f);
            s.Circle(ElbowL.X + 1f, ElbowL.Y + 1f, 4.4f, st.Sleeve, 0.96f, 1.0f);
        }
        s.Circle(rHand.X, rHand.Y, 4.6f, hand, 1.00f, 1.0f, 0.45f);
        s.Circle(lHand.X, lHand.Y, 4.6f, hand, 1.00f, 1.0f, 0.45f);
        return s.Build(gd, 8f, 0.35f);
    }

    // ================================================================================================= head
    /// <summary>Head + headgear. Rotates with the aim; slightly larger than life for readability.</summary>
    public static SpritePair CreateHead(GraphicsDevice gd, CharacterStyle st)
    {
        var s = NewLayer();
        Vector3 cap = st.GearColor ?? new Vector3(0.24f, 0.24f, 0.19f), beanie = new(0.16f, 0.14f, 0.13f);
        s.Circle(1.5f, 0f, 9.5f, st.Skin, 1.30f, 1.0f, 0.45f);                                    // head
        switch (st.Head)
        {
            case HeadGear.Helmet:
                s.Circle(0.5f, 0f, 10.4f, HelmetCol, 1.42f, 1.0f, 0.5f);                            // shell
                s.Ellipse(-1f, 0f, 6f, 7f, HelmetCol * 1.15f, 1.48f, 0.9f);                         // crown
                s.Box(6f, -8f, 8.5f, 8f, HelmetCol * 0.7f, 1.34f, 0.2f);                            // brim
                s.Box(7f, -2.5f, 11f, 2.5f, new Vector3(0.06f, 0.06f, 0.06f), 1.5f, 0.4f);          // NVG mount
                s.Capsule(-3f, -10f, -3f, 10f, 0.8f, new Vector3(0.10f, 0.10f, 0.10f), 1.44f, 0.4f); // rear strap
                break;
            case HeadGear.Cap:
                s.Circle(0.5f, 0f, 9.8f, cap, 1.36f, 1.0f, 0.5f);                                   // crown
                s.Box(7f, -7f, 13f, 7f, cap * 0.85f, 1.30f, 0.3f);                                  // brim (forward)
                s.Circle(-2f, 0f, 3f, cap * 1.2f, 1.40f, 0.9f);                                     // button
                break;
            case HeadGear.Beanie:
                s.Circle(0.5f, 0f, 9.9f, beanie, 1.36f, 1.0f, 0.5f);
                s.Capsule(4f, -8.5f, 4f, 8.5f, 1.6f, beanie * 1.3f, 1.4f, 0.6f);                    // fold
                s.Ellipse(6.5f, 0f, 3.5f, 6f, st.Skin, 1.34f, 0.8f, 0.3f);                          // forehead
                break;
            case HeadGear.Hood:
                s.Ellipse(-1.5f, 0f, 10.5f, 11f, st.Jacket * 0.9f, 1.36f, 1.0f, 0.5f);              // hood up
                s.Ellipse(4.5f, 0f, 5.5f, 6.5f, st.Skin, 1.30f, 0.8f, 0.3f);                        // face in the opening
                s.Capsule(3f, -6.5f, 3f, 6.5f, 0.9f, st.Jacket * 0.6f, 1.38f, 0.5f);                // hood rim
                break;
            default:   // hair
                s.Ellipse(-1.0f, 0f, 8.2f, 9.0f, st.Hair, 1.36f, 1.0f, 0.5f);
                s.Ellipse(-3.5f, 0f, 4.5f, 6.0f, st.Hair * 1.3f, 1.40f, 0.8f);
                s.Ellipse(4.5f, 0f, 4.0f, 6.5f, st.Skin, 1.34f, 0.8f, 0.3f);
                break;
        }
        return s.Build(gd, 8f, 0.35f);
    }

    /// <summary>A single boot facing +X. Drawn twice per character (left/right), rotated to the MOVEMENT direction.</summary>
    public static SpritePair CreateBoot(GraphicsDevice gd, Vector3? color = null)
    {
        Vector3 boot = color ?? new Vector3(0.12f, 0.09f, 0.07f), sole = boot * 0.6f, lace = boot * 1.6f;
        var s = new ShapeSprite(BootSize) { Outline = true, OutlineWidth = 1f, MinNormalZ = 0.74f };
        s.Ellipse(0f, 0f, 9f, 4.5f, sole, 0.4f, 0.6f);
        s.Ellipse(0.5f, 0f, 8f, 3.8f, boot, 0.6f, 0.9f);
        s.Box(-3f, -1f, 3f, 1f, lace, 0.65f, 0.3f);
        return s.Build(gd, 3f, 0.4f);
    }
}
