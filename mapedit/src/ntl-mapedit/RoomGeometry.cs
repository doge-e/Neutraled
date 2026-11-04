using System.Drawing;
using UndertaleModLib.Models;

namespace NtlMapEdit;

/// <summary>Geometry helpers shared by the canvas, the inspector and future export code.</summary>
public static class RoomGeometry
{
    /// <summary>Approximate on-screen bounding box of an instance (rotation expanded to a square).</summary>
    public static RectangleF Bounds(UndertaleRoom.GameObject inst)
    {
        var spr = inst.ObjectDefinition?.Sprite;
        float w = spr != null ? spr.Width : 16;
        float h = spr != null ? spr.Height : 16;
        float ox = spr != null ? spr.OriginX : w / 2f;
        float oy = spr != null ? spr.OriginY : h / 2f;
        float sx = inst.ScaleX == 0 ? 0.0001f : inst.ScaleX;
        float sy = inst.ScaleY == 0 ? 0.0001f : inst.ScaleY;

        float left = inst.X - ox * sx;
        float top = inst.Y - oy * sy;
        float ww = w * sx;
        float hh = h * sy;
        if (ww < 0) { left += ww; ww = -ww; }
        if (hh < 0) { top += hh; hh = -hh; }

        var rect = new RectangleF(left, top, ww, hh);
        if (Math.Abs(inst.Rotation) > 0.01f)
        {
            float cx = rect.X + rect.Width / 2f;
            float cy = rect.Y + rect.Height / 2f;
            float r = MathF.Sqrt(rect.Width * rect.Width + rect.Height * rect.Height) / 2f;
            rect = new RectangleF(cx - r, cy - r, r * 2f, r * 2f);
        }
        return rect;
    }

    public static bool HasSprite(UndertaleRoom.GameObject inst) => inst.ObjectDefinition?.Sprite != null;

    /// <summary>An object with no sprite, or a non-visible object, is only shown as a marker.</summary>
    public static bool IsHelper(UndertaleRoom.GameObject inst)
        => inst.ObjectDefinition == null || inst.ObjectDefinition.Sprite == null || !inst.ObjectDefinition.Visible;
}
