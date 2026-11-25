using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace InventoryParticles.Definitions;

/// <summary> Looks into stacks for the rules' per-stack conditions: liquids and spoilage. </summary>
internal interface IStackInspector
{
    /// <summary>
    /// True if <paramref name="container"/> holds a liquid. <paramref name="color"/> is its color (red, green, blue, alpha, each 0..1)
    /// as the container shows it, or null if unknown.
    /// </summary>
    bool TryGetLiquid(ItemStack container, out Vec4f? color);

    /// <summary> How far the stack has spoiled, as the "% spoiled" of its tooltip: 0 while fresh, 1 rotten. Null if it can't spoil. </summary>
    float? GetSpoilage(ItemStack stack, ItemSlot? slot);
}
