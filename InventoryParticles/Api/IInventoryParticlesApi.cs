using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using InventoryParticles.Definitions;
using Vintagestory.API.Common;

namespace InventoryParticles.Api;

/// <summary>
/// GUI particles for other mods. Get it with
/// <c>capi.ModLoader.GetModSystem&lt;InventoryParticlesModSystem&gt;()?.Api</c>.
/// Everything must be called on the main (render) thread. Registration works from <c>StartClientSide</c> on;
/// playing needs a loaded world and returns <see cref="ParticleHandle.Invalid"/> before that.
/// </summary>
public interface IInventoryParticlesApi
{
    /// <summary> True once the world is loaded and particles can be played. </summary>
    bool IsReady { get; }

    /// <summary> Adds or replaces an effect. Effects registered in code win over JSON effects with the same code and survive reloads. </summary>
    void RegisterEffect(AssetLocation code, GuiParticleEffect effect);

    bool TryGetEffect(AssetLocation code, [NotNullWhen(true)] out GuiParticleEffect? effect);

    /// <summary> Codes of all known effects. </summary>
    IReadOnlyCollection<AssetLocation> EffectCodes { get; }

    /// <summary> Adds a rule binding effects to items, like the JSON rules. Survives reloads. </summary>
    void RegisterRule(ItemParticleRule rule);

    /// <summary> Adds code that picks effects for stacks. Providers are asked before the rules, in registration order. </summary>
    void RegisterProvider(IItemParticleProvider provider);

    /// <summary> Plays an effect at an anchor until <see cref="Stop"/> or <see cref="PlayOptions.Duration"/>. </summary>
    ParticleHandle Play(AssetLocation effect, ParticleAnchor anchor, PlayOptions? options = null);

    /// <summary>
    /// Plays the effects the rules and providers pick for <paramref name="stack"/> (rechecked a few times per second),
    /// e.g. for a stack drawn by your own GUI. <see cref="PlayOptions.Trigger"/> is the trigger the rules see.
    /// </summary>
    ParticleHandle PlayItem(ItemStack stack, ParticleAnchor anchor, PlayOptions? options = null);

    /// <summary> Spawns <paramref name="count"/> particles from every emitter of the effect once. False if the effect is unknown or the world isn't loaded. </summary>
    bool Burst(AssetLocation effect, ParticleAnchor anchor, int count, PlayOptions? options = null);

    /// <summary> Stops emitting. Live particles finish their life unless <paramref name="clearParticles"/> is set. </summary>
    bool Stop(ParticleHandle handle, bool clearParticles = false);

    bool IsAlive(ParticleHandle handle);

    bool SetIntensity(ParticleHandle handle, float intensity);

    bool SetAnchor(ParticleHandle handle, ParticleAnchor anchor);

    /// <summary> Where a slot was drawn in the last frame, if an open dialog shows it. </summary>
    bool TryGetSlotRect(ItemSlot slot, out GuiItemRect rect);

    /// <summary> Where the hovered stack was drawn in the last frame. </summary>
    GuiItemRect? HoveredRect { get; }

    /// <summary> Where the stack held on the cursor was drawn in the last frame. </summary>
    GuiItemRect? CursorRect { get; }

    /// <summary>
    /// For a mod that draws the stack on the cursor itself, e.g. moved or turned: <paramref name="provider"/> is asked every frame
    /// while that stack is shown and returns where its item is drawn, with <see cref="GuiItemRect.Rotation"/> if it is turned.
    /// Null from it means the game's own place next to the mouse. Everything played at <see cref="ParticleAnchor.Cursor"/> follows it,
    /// including the cursor and pickup effects of the rules. Only one provider is active: a new one replaces it, null removes it.
    /// </summary>
    void SetCursorRectProvider(Func<GuiItemRect?>? provider);

    /// <summary> The effects providers and rules pick for a stack right now, without playing them. </summary>
    List<ItemParticleMatch> GetItemParticles(ItemStack stack, EnumParticleTrigger trigger, ItemSlot? slot = null);

    /// <summary> Raised whenever the effects of the hovered stack, the stack on the cursor, a stack in hand or a <see cref="PlayItem"/> stack are picked. Handlers may change or cancel them. </summary>
    event ItemParticlesDelegate? OnItemParticles;

    /// <summary> Number of live particles. </summary>
    int ParticleCount { get; }
}
