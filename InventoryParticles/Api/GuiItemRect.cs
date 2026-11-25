using System;

namespace InventoryParticles.Api;

/// <summary>
/// Where an item stack is drawn on screen: the centre of the item in screen pixels (origin top left, y down),
/// the size of the slot it is drawn in and how far the drawn item is turned. Effect definitions use <see cref="Size"/>
/// as their unit of length.
/// </summary>
public readonly struct GuiItemRect
{
    public GuiItemRect(double centerX, double centerY, float size)
        : this(centerX, centerY, size, 0f)
    {
    }

    public GuiItemRect(double centerX, double centerY, float size, float rotation)
    {
        CenterX = centerX;
        CenterY = centerY;
        Size = size;
        Rotation = float.IsFinite(rotation) ? rotation : 0f;
    }

    public double CenterX { get; }

    public double CenterY { get; }

    /// <summary> Edge length of an inventory slot in screen pixels (<c>scaled(48)</c>). 1.0 in effect units. </summary>
    public float Size { get; }

    /// <summary>
    /// How far the item is turned around its centre, in radians, clockwise on screen. Effect origins, spawn areas and particles that
    /// hang on the item turn with it; velocities, gravity and particles that left the item stay in screen space, so flames still rise.
    /// </summary>
    public float Rotation { get; }

    public override string ToString() => Rotation == 0f
        ? $"({CenterX:0.#}, {CenterY:0.#}) size {Size:0.#}"
        : $"({CenterX:0.#}, {CenterY:0.#}) size {Size:0.#} turned {Rotation * 180f / MathF.PI:0.#}°";
}
