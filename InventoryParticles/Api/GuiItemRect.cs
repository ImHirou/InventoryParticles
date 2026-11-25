namespace InventoryParticles.Api;

/// <summary>
/// Where an item stack is drawn on screen: the centre of the item in screen pixels (origin top left, y down)
/// and the size of the slot it is drawn in. Effect definitions use <see cref="Size"/> as their unit of length.
/// </summary>
public readonly struct GuiItemRect
{
    public GuiItemRect(double centerX, double centerY, float size)
    {
        CenterX = centerX;
        CenterY = centerY;
        Size = size;
    }

    public double CenterX { get; }

    public double CenterY { get; }

    /// <summary> Edge length of an inventory slot in screen pixels (<c>scaled(48)</c>). 1.0 in effect units. </summary>
    public float Size { get; }

    public override string ToString() => $"({CenterX:0.#}, {CenterY:0.#}) size {Size:0.#}";
}
