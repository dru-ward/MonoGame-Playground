using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Game.Items;

namespace Game.Graphics;

/// <summary>Small top-down attachment sprites drawn over the arm layer at the weapon's attach points (+X forward).</summary>
public static class AttachmentArt
{
    public static Dictionary<ItemType, SpritePair> CreateAll(GraphicsDevice gd)
    {
        var d = new Dictionary<ItemType, SpritePair>();
        Vector3 dark = new(0.10f, 0.10f, 0.12f), mid = new(0.22f, 0.22f, 0.25f), lens = new(0.35f, 0.15f, 0.15f);
        ShapeSprite S() => new(24) { Outline = true, OutlineWidth = 1f, MinNormalZ = 0.74f };

        var s = S();   // red dot: squat box with a glass
        s.Box(-4f, -2.5f, 4f, 2.5f, dark, 0.9f, 0.3f); s.Box(-2.5f, -1.5f, 2.5f, 1.5f, mid, 1.0f, 0.4f); s.Circle(2.2f, 0f, 1.0f, new Vector3(0.9f, 0.2f, 0.2f), 1.1f, 0.9f);
        d[ItemType.Optic] = s.Build(gd, 6f, 0.35f);

        s = S();       // suppressor: long tube ahead of the muzzle
        s.Capsule(-8f, 0f, 8f, 0f, 2.6f, dark, 0.8f, 0.9f); s.Capsule(-8f, 0f, -6f, 0f, 2.2f, mid, 0.82f, 0.9f);
        d[ItemType.Suppressor] = s.Build(gd, 6f, 0.35f);

        s = S();       // compensator: short slotted cylinder
        s.Capsule(-3f, 0f, 3f, 0f, 2.9f, mid, 0.8f, 0.9f); for (int i = -1; i <= 1; i++) s.Box(i * 2f - 0.5f, -2.2f, i * 2f + 0.5f, 2.2f, dark, 0.85f, 0.2f);
        d[ItemType.Compensator] = s.Build(gd, 6f, 0.35f);

        s = S();       // torch: cylinder with a bright lens at the front
        s.Capsule(-5f, 0f, 4f, 0f, 2.4f, dark, 0.85f, 0.9f); s.Circle(5f, 0f, 2.2f, new Vector3(1.0f, 0.95f, 0.8f), 0.9f, 0.8f);
        d[ItemType.Torch] = s.Build(gd, 6f, 0.35f);

        s = S();       // laser: small box with a red emitter
        s.Box(-4f, -2f, 3f, 2f, dark, 0.9f, 0.3f); s.Circle(3.5f, 0f, 1.2f, new Vector3(1.0f, 0.15f, 0.15f), 1.0f, 0.9f);
        d[ItemType.Laser] = s.Build(gd, 6f, 0.35f);

        s = S();       // fore grip: stubby vertical grip seen from above
        s.Box(-2.5f, -1.5f, 2.5f, 4.5f, new Vector3(0.16f, 0.14f, 0.12f), 0.9f, 0.5f); s.Box(-2f, -1f, 2f, 0.5f, mid, 0.95f, 0.3f);
        d[ItemType.Grip] = s.Build(gd, 6f, 0.35f);
        return d;
    }
}
