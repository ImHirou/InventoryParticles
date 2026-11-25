using System.Collections.Generic;
using InventoryParticles.Definitions;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

namespace InventoryParticles.Gui;

/// <summary>
/// Answers the rules' per-stack questions from the game's own data.
/// <list type="bullet">
/// <item>Liquids: vanilla liquid containers (buckets, jugs, bowls and their subclasses from other mods) and watering cans.
/// The color is the one the container draws its contents with: the average color of the liquid's container texture, tinted by
/// its climate color map like <c>BlockLiquidContainerTopOpened</c> tints liquids in the GUI. Colors are cached per liquid.</item>
/// <item>Spoilage: the perish transition, read the same way the tooltip reads it.</item>
/// </list>
/// </summary>
internal sealed class StackInspector : IStackInspector
{
    // BlockLiquidContainerTopOpened.GenMesh tints GUI meshes with the color map at this rain and temperature.
    private const int GuiRain = 196;
    private const int GuiTemperature = 128;

    private static readonly AssetLocation Water = new("game", "waterportion");

    private readonly ICoreClientAPI capi;
    private readonly Dictionary<CollectibleObject, Vec4f?> colors = new(ReferenceEqualityComparer.Instance);

    public StackInspector(ICoreClientAPI capi)
    {
        this.capi = capi;
    }

    /// <summary> Forgets the cached colors, e.g. after a texture reload. </summary>
    public void Clear() => colors.Clear();

    public bool TryGetLiquid(ItemStack container, out Vec4f? color)
    {
        color = null;
        CollectibleObject? liquid;

        switch (container.Collectible)
        {
            case BlockLiquidContainerBase block:
                var content = block.GetContent(container);
                liquid = content?.Collectible;

                // Containers also take solids such as rot or slush.
                if (liquid == null || content!.StackSize <= 0 || liquid.MatterState != EnumMatterState.Liquid)
                {
                    return false;
                }

                break;
            case BlockWateringCan can:
                if (can.GetRemainingWateringSeconds(container) <= 0f)
                {
                    return false;
                }

                // Watering cans only ever hold water.
                liquid = capi.World.GetItem(Water);
                break;
            default:
                return false;
        }

        if (liquid != null)
        {
            if (!colors.TryGetValue(liquid, out color))
            {
                color = FindColor(liquid);
                colors[liquid] = color;
            }
        }

        return true;
    }

    public float? GetSpoilage(ItemStack stack, ItemSlot? slot)
    {
        // Without freshness data the game would create some on this client; the server sends it for anything that spoils.
        if (stack.Attributes?.HasAttribute("transitionstate") != true || stack.Collectible == null)
        {
            return null;
        }

        var inSlot = slot != null && ReferenceEquals(slot.Itemstack, stack) ? slot : new DummySlot(stack);
        return stack.Collectible.UpdateAndGetTransitionState(capi.World, inSlot, EnumTransitionType.Perish)?.TransitionLevel;
    }

    private Vec4f? FindColor(CollectibleObject liquid)
    {
        var props = BlockLiquidContainerBase.GetContainableProps(liquid);

        if (props?.Texture == null)
        {
            return null;
        }

        // The same lookup as ContainerTextureSource, which textures the contents mesh.
        props.Texture.Bake(capi.Assets);
        var name = props.Texture.Baked.BakedName;
        var position = capi.BlockTextureAtlas[name];

        if (position == null && !capi.BlockTextureAtlas.GetOrInsertTexture(name, out _, out position))
        {
            return null;
        }

        if (position == null)
        {
            return null;
        }

        // AvgColor has red in the lowest byte and no alpha.
        int average = position.AvgColor;
        float red = (average & 0xFF) / 255f;
        float green = ((average >> 8) & 0xFF) / 255f;
        float blue = ((average >> 16) & 0xFF) / 255f;

        if (!string.IsNullOrEmpty(props.ClimateColorMap))
        {
            // Without flipping, red is in the lowest byte as well.
            int tint = capi.World.ApplyColorMapOnRgba(props.ClimateColorMap, null, -1, GuiRain, GuiTemperature, flipRb: false);
            red *= (tint & 0xFF) / 255f;
            green *= ((tint >> 8) & 0xFF) / 255f;
            blue *= ((tint >> 16) & 0xFF) / 255f;
        }

        return new Vec4f(red, green, blue, 1f);
    }
}
