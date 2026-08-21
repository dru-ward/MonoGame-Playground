using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Graphics;

namespace Game.Items;

/// <summary>Procedural 28x28 item icons (used both as floor pickups and HUD/inventory icons).</summary>
public static class ItemArt
{
    public const int IconSize = 28;

    public static Dictionary<ItemType, SpritePair> CreateAll(GraphicsDevice gd)
    {
        var d = new Dictionary<ItemType, SpritePair>();
        foreach (var def in ItemDef.All) d[def.Type] = Create(gd, def.Type);
        return d;
    }

    public static SpritePair Create(GraphicsDevice gd, ItemType type)
    {
        var s = new ShapeSprite(IconSize);
        Vector3 dark = new(0.10f, 0.10f, 0.10f), brass = new(0.85f, 0.65f, 0.25f), red = new(0.85f, 0.12f, 0.12f), white = new(0.95f, 0.95f, 0.95f);
        Vector3 gunDark = new(0.13f, 0.13f, 0.15f), gunMid = new(0.26f, 0.26f, 0.29f), wood = new(0.40f, 0.27f, 0.15f);
        switch (type)
        {
            case ItemType.RifleMag:                        // curved rifle magazine, brass round on top
                s.Box(-4f, -11f, 4f, 9f, gunDark, 0.7f, 0.3f);
                s.Box(-3f, -3f, 3f, 7f, gunMid, 0.75f, 0.2f);
                s.Capsule(0f, -12f, 0f, -9f, 1.5f, brass, 0.85f, 0.9f);
                s.Box(-5f, 8f, 5f, 11f, gunMid, 0.72f, 0.2f);                                     // base plate
                break;
            case ItemType.PistolMag:                       // straight pistol mag
                s.Box(-3f, -9f, 3f, 9f, gunMid, 0.7f, 0.3f);
                s.Capsule(0f, -10f, 0f, -8f, 1.4f, new Vector3(0.85f, 0.85f, 0.9f), 0.85f, 0.9f);
                s.Box(-4f, 8f, 4f, 11f, gunDark, 0.72f, 0.2f);
                break;
            case ItemType.SmgMag:                          // long stick mag
                s.Box(-2.5f, -12f, 2.5f, 11f, gunMid, 0.7f, 0.3f);
                for (int i = -3; i <= 3; i++) s.Box(-2f, i * 3f - 0.7f, 2f, i * 3f + 0.7f, gunDark, 0.72f, 0.2f);
                s.Capsule(0f, -12.5f, 0f, -10.5f, 1.3f, brass, 0.85f, 0.9f);
                break;
            case ItemType.Shells:                          // box of red shells
                s.Box(-9f, -7f, 9f, 7f, new Vector3(0.30f, 0.14f, 0.10f), 0.6f, 0.3f);
                for (int i = -1; i <= 1; i++) { s.Capsule(i * 5f, -4f, i * 5f, 4f, 2.0f, new Vector3(0.80f, 0.20f, 0.15f), 0.75f, 0.9f); s.Circle(i * 5f, 4.5f, 2.0f, brass, 0.8f, 0.9f); }
                break;
            case ItemType.Medkit:                          // white case, red cross
                s.Box(-10f, -8f, 10f, 8f, white, 0.6f, 0.3f);
                s.Box(-2.5f, -6f, 2.5f, 6f, red, 0.7f, 0.2f);
                s.Box(-6f, -2.5f, 6f, 2.5f, red, 0.7f, 0.2f);
                s.Box(-3f, -9.5f, 3f, -8f, dark, 0.65f, 0.2f);
                break;
            case ItemType.Bandage:
                s.Capsule(-6f, 0f, 6f, 0f, 5.5f, new Vector3(0.90f, 0.84f, 0.72f), 0.7f, 1.0f);
                s.Capsule(-6f, 0f, 6f, 0f, 2.0f, new Vector3(0.75f, 0.68f, 0.56f), 0.75f, 0.9f);
                s.Circle(7.5f, 0f, 3.2f, new Vector3(0.80f, 0.74f, 0.62f), 0.8f, 1.0f);
                break;
            case ItemType.ArmorPlate:
                s.Box(-8f, -10f, 8f, 10f, new Vector3(0.28f, 0.40f, 0.62f), 0.7f, 0.5f);
                s.Box(-6f, -8f, 6f, 8f, new Vector3(0.36f, 0.52f, 0.80f), 0.75f, 0.4f);
                s.Capsule(-4f, 4f, 0f, -2f, 1.4f, white, 0.85f, 0.9f);
                s.Capsule(0f, -2f, 4f, 4f, 1.4f, white, 0.85f, 0.9f);
                break;
            case ItemType.Coin:
                s.Circle(0f, 0f, 9.5f, new Vector3(0.95f, 0.75f, 0.20f), 0.7f, 1.0f, 0.5f);
                s.Circle(0f, 0f, 6.5f, new Vector3(1.00f, 0.88f, 0.35f), 0.75f, 0.9f, 0.3f);
                s.Box(-1.2f, -4f, 1.2f, 4f, new Vector3(0.85f, 0.65f, 0.15f), 0.8f, 0.2f);
                break;
            case ItemType.GunRifle:                        // side-view silhouettes for guns
                s.Box(-13f, -1.5f, -7f, 2.5f, wood, 0.6f, 0.3f);                                    // stock
                s.Box(-7f, -2f, 8f, 1.5f, gunMid, 0.65f, 0.3f);                                    // receiver
                s.Box(-3f, 1.5f, 1f, 6f, gunDark, 0.62f, 0.3f);                                    // magazine
                s.Box(-4f, -4f, 3f, -2f, gunDark, 0.7f, 0.3f);                                     // optic
                s.Capsule(8f, -0.5f, 13f, -0.5f, 1.2f, gunDark, 0.65f, 0.9f);                     // barrel
                s.Box(-8f, 1.5f, -6f, 4.5f, gunDark, 0.62f, 0.3f);                                 // grip
                break;
            case ItemType.GunPistol:
                s.Box(-8f, -2.5f, 8f, 1f, gunDark, 0.65f, 0.3f);                                   // slide
                s.Box(-6f, 1f, -1f, 8f, gunMid, 0.62f, 0.3f);                                      // grip
                s.Box(-1f, 1f, 2f, 3f, gunDark, 0.62f, 0.3f);                                      // trigger guard
                break;
            case ItemType.GunSmg:
                s.Box(-9f, -2f, 8f, 2f, gunMid, 0.65f, 0.3f);
                s.Box(-3f, 2f, 0f, 9f, gunDark, 0.62f, 0.3f);                                      // long stick mag
                s.Box(-13f, -1f, -9f, 1f, gunDark, 0.62f, 0.3f);                                   // folded stock
                s.Capsule(8f, 0f, 12f, 0f, 1.2f, gunDark, 0.65f, 0.9f);
                s.Box(-8f, 2f, -6f, 6f, gunDark, 0.62f, 0.3f);                                     // grip
                break;
            case ItemType.MeleeBat:                        // nail bat
                s.Capsule(-11f, 6f, 9f, -6f, 2.4f, wood, 0.65f, 1.0f); s.Capsule(4f, -3f, 10f, -7f, 3.2f, wood * 1.1f, 0.7f, 1.0f);
                s.Capsule(-11f, 6f, -8f, 4f, 2.0f, dark, 0.66f, 0.9f);
                for (int i = 0; i < 3; i++) s.Capsule(2f + i * 2.5f, -1.5f - i * 1.5f, 4f + i * 2.5f, -5f - i * 1.5f, 0.6f, new Vector3(0.7f, 0.7f, 0.72f), 0.75f, 0.5f);
                break;
            case ItemType.VestLight:                       // olive plate carrier
                s.Box(-9f, -10f, 9f, 10f, new Vector3(0.32f, 0.36f, 0.26f), 0.6f, 0.4f);
                s.Box(-7f, -8f, 7f, 2f, new Vector3(0.38f, 0.42f, 0.30f), 0.7f, 0.3f);
                s.Box(-7f, 4f, -1f, 9f, new Vector3(0.26f, 0.30f, 0.20f), 0.75f, 0.3f); s.Box(1f, 4f, 7f, 9f, new Vector3(0.26f, 0.30f, 0.20f), 0.75f, 0.3f);
                s.Capsule(-8f, -10f, -5f, -12f, 1.5f, dark, 0.7f, 0.9f); s.Capsule(8f, -10f, 5f, -12f, 1.5f, dark, 0.7f, 0.9f);
                break;
            case ItemType.VestHeavy:                       // black heavy vest with plates
                s.Box(-10f, -11f, 10f, 11f, new Vector3(0.14f, 0.14f, 0.16f), 0.65f, 0.4f);
                s.Box(-8f, -9f, 8f, 3f, new Vector3(0.22f, 0.22f, 0.25f), 0.8f, 0.3f);
                for (int i = -1; i <= 1; i++) s.Box(i * 5f - 2f, 5f, i * 5f + 2f, 10f, new Vector3(0.10f, 0.10f, 0.11f), 0.82f, 0.3f);
                s.Capsule(-9f, -11f, -6f, -13f, 1.6f, dark, 0.7f, 0.9f); s.Capsule(9f, -11f, 6f, -13f, 1.6f, dark, 0.7f, 0.9f);
                break;
            case ItemType.HelmetSteel:                     // round steel pot
                s.Circle(0f, 1f, 10f, new Vector3(0.40f, 0.42f, 0.38f), 0.8f, 1.0f, 0.5f); s.Ellipse(0f, 8f, 11f, 3f, new Vector3(0.30f, 0.32f, 0.28f), 0.6f, 0.5f);
                s.Ellipse(-2f, -2f, 4f, 5f, new Vector3(0.48f, 0.50f, 0.46f), 0.9f, 0.9f);
                break;
            case ItemType.HelmetTac:                       // tactical helmet with rails + NVG mount
                s.Circle(0f, 1f, 10f, new Vector3(0.26f, 0.29f, 0.22f), 0.8f, 1.0f, 0.5f);
                s.Box(-11f, -2f, -8f, 6f, dark, 0.85f, 0.3f); s.Box(8f, -2f, 11f, 6f, dark, 0.85f, 0.3f);
                s.Box(-3f, -11f, 3f, -7f, dark, 0.9f, 0.3f);
                break;
            case ItemType.Optic:
                s.Box(-9f, -3f, 9f, 3f, gunDark, 0.7f, 0.3f); s.Box(-6f, -6f, 6f, -3f, gunMid, 0.75f, 0.3f); s.Circle(5f, -4.5f, 1.4f, red, 0.8f, 0.9f);
                break;
            case ItemType.Suppressor:
                s.Capsule(-10f, 0f, 10f, 0f, 3.2f, gunDark, 0.7f, 0.9f); s.Capsule(-10f, 0f, -7f, 0f, 2.6f, gunMid, 0.72f, 0.9f);
                break;
            case ItemType.Compensator:
                s.Capsule(-6f, 0f, 6f, 0f, 3.5f, gunMid, 0.7f, 0.9f); for (int i = -1; i <= 1; i++) s.Box(i * 3.5f - 0.7f, -3f, i * 3.5f + 0.7f, 3f, gunDark, 0.75f, 0.2f);
                break;
            case ItemType.Torch:
                s.Capsule(-9f, 0f, 5f, 0f, 3.2f, gunDark, 0.7f, 0.9f); s.Circle(7f, 0f, 3.4f, new Vector3(1.0f, 0.95f, 0.75f), 0.8f, 0.8f);
                break;
            case ItemType.Laser:
                s.Box(-7f, -3f, 5f, 3f, gunDark, 0.7f, 0.3f); s.Circle(6f, 0f, 1.8f, new Vector3(1.0f, 0.15f, 0.15f), 0.85f, 0.9f);
                s.Box(-7f, 3f, -3f, 6f, gunMid, 0.72f, 0.3f);
                break;
            case ItemType.Grip:
                s.Box(-3f, -9f, 3f, 8f, new Vector3(0.16f, 0.14f, 0.12f), 0.75f, 0.5f); s.Box(-6f, -10f, 6f, -7f, gunMid, 0.8f, 0.3f);
                for (int i = 0; i < 3; i++) s.Box(-2.5f, -4f + i * 4f, 2.5f, -3f + i * 4f, gunDark, 0.78f, 0.2f);
                break;
            case ItemType.Grenade:
                s.Ellipse(0f, 1f, 6.5f, 8f, new Vector3(0.30f, 0.42f, 0.28f), 0.7f, 1.0f);
                s.Box(-3f, -10f, 3f, -6f, dark, 0.8f, 0.3f);
                s.Capsule(2f, -9f, 7f, -3f, 1.2f, new Vector3(0.6f, 0.6f, 0.62f), 0.85f, 0.9f);
                for (int i = -1; i <= 1; i++) s.Box(-6f, i * 4f - 0.6f, 6f, i * 4f + 0.6f, new Vector3(0.22f, 0.32f, 0.20f), 0.72f, 0.2f);
                break;
            case ItemType.GunShotgun:
                s.Box(-13f, -1.5f, -6f, 2.5f, wood, 0.6f, 0.3f);                                    // stock
                s.Box(-6f, -2f, 4f, 1.5f, gunMid, 0.65f, 0.3f);                                    // receiver
                s.Capsule(4f, -1f, 13f, -1f, 1.6f, gunDark, 0.65f, 0.9f);                         // barrel
                s.Capsule(4f, 1.5f, 11f, 1.5f, 1.4f, wood, 0.65f, 0.9f);                          // pump / tube
                break;
        }
        return new SpritePair(s.CreateAlbedo(gd), s.CreateNormal(gd, reliefPx: 4f, strength: 0.4f));
    }
}
