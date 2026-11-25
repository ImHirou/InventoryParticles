using System.Collections.Generic;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace InventoryParticles.Api;

/// <summary> One effect an item should show, produced by a rule or an <see cref="IItemParticleProvider"/>. </summary>
public sealed class ItemParticleMatch
{
    public ItemParticleMatch(AssetLocation effect)
    {
        Effect = effect;
    }

    /// <summary> Code of the effect to play. </summary>
    public AssetLocation Effect { get; set; }

    /// <summary> Multiplies the emission rate; also checked against the emitters' <c>minIntensity</c>. </summary>
    public float Intensity { get; set; } = 1f;

    /// <summary> Multiplies the particle sizes. </summary>
    public float Scale { get; set; } = 1f;

    /// <summary> Moves the effect's origin, in effect units (1 = the edge of a slot), +Y up. </summary>
    public float OriginX { get; set; }

    /// <inheritdoc cref="OriginX"/>
    public float OriginY { get; set; }

    /// <summary> Multiplies the particle colors (red, green, blue, alpha, each 0..1). </summary>
    public Vec4f? Tint { get; set; }

    /// <summary> Code of the rule (or the name of the provider) that produced this match. Informational. </summary>
    public string? Source { get; set; }
}

/// <summary> What is being asked: which stack, where it is and why. </summary>
public readonly struct ItemParticleQuery
{
    public ItemParticleQuery(ItemStack stack, ItemSlot? slot, EnumParticleTrigger trigger, float? temperature)
    {
        Stack = stack;
        Slot = slot;
        Trigger = trigger;
        Temperature = temperature;
    }

    public ItemStack Stack { get; }

    /// <summary> The slot holding the stack, if known. </summary>
    public ItemSlot? Slot { get; }

    public EnumParticleTrigger Trigger { get; }

    /// <summary> The stack's temperature in °C, or null if it has no temperature attribute (room temperature). </summary>
    public float? Temperature { get; }
}

/// <summary> Decides in code which effects a stack shows, before the JSON rules are checked. </summary>
public interface IItemParticleProvider
{
    /// <summary>
    /// Adds effects for <paramref name="query"/> to <paramref name="matches"/>. Called for the hovered stack, the stack on the cursor
    /// and the stacks in hand about four times per second and whenever the stack changes. Return true to skip the remaining providers and all rules.
    /// </summary>
    bool Collect(in ItemParticleQuery query, List<ItemParticleMatch> matches);
}

/// <summary> Raised after providers and rules picked the effects of a stack. Handlers may edit <see cref="Matches"/> or cancel. </summary>
public sealed class ItemParticlesEventArgs
{
    internal ItemParticlesEventArgs(ItemParticleQuery query, List<ItemParticleMatch> matches)
    {
        Query = query;
        Matches = matches;
    }

    public ItemParticleQuery Query { get; internal set; }

    /// <summary> The effects that will be shown. Can be changed. </summary>
    public List<ItemParticleMatch> Matches { get; internal set; }

    /// <summary> Set to true to show no particles for this stack. </summary>
    public bool Cancel { get; set; }
}

public delegate void ItemParticlesDelegate(ItemParticlesEventArgs args);
