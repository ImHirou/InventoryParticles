using InventoryParticles.Api;
using InventoryParticles.Config;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;

namespace InventoryParticles.Gui;

/// <summary>
/// Shows the particles of the hovered stack, of the stack held on the cursor and of the stacks in the player's hands,
/// and plays the one-shot effects: a stack taken onto the cursor, and a stack the game made the player drop because it was too hot.
/// </summary>
internal sealed class ItemParticleDriver
{
    /// <summary> How long a hot stack that went out of sight may still be the one the game drops. </summary>
    private const double HotMemorySeconds = 2;

    private readonly InventoryParticlesApi api;
    private readonly ICoreClientAPI capi;
    private readonly SlotTracker hover = new(EnumParticleTrigger.Hover);
    private readonly SlotTracker rightHand = new(EnumParticleTrigger.Held);
    private readonly SlotTracker leftHand = new(EnumParticleTrigger.Held);
    private ItemTracker? cursor;
    private ItemStack? lastCursorStack;
    private HotSighting? lastHot;

    public ItemParticleDriver(InventoryParticlesApi api, ICoreClientAPI capi)
    {
        this.api = api;
        this.capi = capi;
    }

    public GuiItemRect? HoveredRect { get; private set; }

    public GuiItemRect? CursorRect { get; private set; }

    public void Update(double time, InventoryParticlesConfig config, IAnchorResolver resolver)
    {
        var player = capi.World?.Player;
        var inventory = player?.InventoryManager;
        bool mouseFree = !capi.Input.MouseGrabbed;
        bool inventoryOpen = inventory != null && inventory.OpenedInventories.Count > 0;
        // Particles of a dialog that just closed should not linger over the world.
        bool guiClosed = inventory == null || !mouseFree || !inventoryOpen;

        var hovered = NonEmpty(config.Enabled && config.ShowOnHover && mouseFree ? inventory?.CurrentHoveredSlot : null);
        // The hotbar is not drawn in spectator mode. A hand slot under the mouse already shows its particles as the hovered slot.
        bool hotbar = config.Enabled && config.ShowInHotbar && player?.WorldData?.CurrentGameMode != EnumGameMode.Spectator;
        var right = hotbar ? Unless(NonEmpty(inventory?.ActiveHotbarSlot), hovered) : null;
        var left = hotbar ? Unless(Unless(NonEmpty(inventory?.OffhandHotbarSlot), hovered), right) : null;
        var mouseSlot = config.Enabled && config.ShowOnCursor && mouseFree && inventoryOpen ? inventory?.MouseItemSlot : null;
        var stack = mouseSlot?.Itemstack;
        cursor ??= new ItemTracker(api, ParticleAnchor.Cursor(), EnumParticleTrigger.Cursor, null);

        // First every tracker lets go of the item it lost, then they take the items they got: an item that moved between a slot,
        // the cursor and a hand this frame takes its running effects along.
        hover.Release(hovered, fade: guiClosed);
        rightHand.Release(right, fade: false);
        leftHand.Release(left, fade: false);

        if (stack == null && cursor.HasEffects)
        {
            cursor.ReleaseEffects(fade: guiClosed);
        }

        hover.Update(this, hovered, time);
        HoveredRect = hover.Slot != null && resolver.TryGetHoveredRect(hover.Slot, out var hoveredRect) ? hoveredRect : null;
        rightHand.Update(this, right, time);
        leftHand.Update(this, left, time);
        cursor.Slot = mouseSlot;
        cursor.Update(stack, time);
        CursorRect = stack != null && resolver.TryGetCursorRect(out var cursorRect) ? cursorRect : null;

        // The server replaces the stack on the cursor with its own copy; only an empty cursor getting a stack is a pickup.
        if (stack != null && lastCursorStack == null)
        {
            api.PlayItemBurst(stack, mouseSlot, EnumParticleTrigger.Pickup, ParticleAnchor.Cursor());
        }

        lastCursorStack = stack;
        RememberHotStack(time, resolver);
    }

    /// <summary> The game made the player drop a stack that was too hot to hold: play the toohot effects where it was last seen. </summary>
    public void OnTooHot(double time)
    {
        if (lastHot is not { } hot || time - hot.Time > HotMemorySeconds)
        {
            return;
        }

        lastHot = null;
        api.PlayItemBurst(hot.Stack, hot.Slot, EnumParticleTrigger.TooHot, ParticleAnchor.Point(hot.Rect.CenterX, hot.Rect.CenterY, hot.Rect.Size));
    }

    /// <summary>
    /// The game drops stacks too hot to touch from the hand (the active hotbar slot) and from the crafting grid, which the mouse
    /// is over when that happens. Remembers where such a stack was seen last, in that order of likelihood.
    /// </summary>
    private void RememberHotStack(double time, IAnchorResolver resolver)
    {
        float limit = GlobalConstants.TooHotToTouchTemperature;

        if (rightHand.IsHotter(limit) && resolver.TryGetHeldRect(rightHand.Slot!, out var rect))
        {
            lastHot = new HotSighting(rightHand.Slot!.Itemstack!, rightHand.Slot, rect, time);
        }
        else if (hover.IsHotter(limit) && resolver.TryGetHoveredRect(hover.Slot!, out rect))
        {
            lastHot = new HotSighting(hover.Slot!.Itemstack!, hover.Slot, rect, time);
        }
        else if (cursor?.Temperature > limit && lastCursorStack != null && CursorRect is { } cursorRect)
        {
            lastHot = new HotSighting(lastCursorStack, cursor.Slot, cursorRect, time);
        }
    }

    /// <summary> Stops everything; effects start again with the current definitions on the next frame. </summary>
    public void Reset()
    {
        hover.Reset();
        rightHand.Reset();
        leftHand.Reset();
        cursor?.Stop(false);
        cursor = null;
        lastHot = null;
        HoveredRect = null;
        CursorRect = null;
    }

    private static ItemSlot? NonEmpty(ItemSlot? slot) => slot?.Itemstack != null ? slot : null;

    private static ItemSlot? Unless(ItemSlot? slot, ItemSlot? other) => ReferenceEquals(slot, other) ? null : slot;

    private readonly record struct HotSighting(ItemStack Stack, ItemSlot? Slot, GuiItemRect Rect, double Time);

    /// <summary> Keeps one tracker on one slot and replaces it when the slot changes. </summary>
    private sealed class SlotTracker(EnumParticleTrigger trigger)
    {
        private ItemTracker? tracker;

        public ItemSlot? Slot { get; private set; }

        /// <summary> The stack in the slot was hotter than <paramref name="limit"/> °C when its effects were last picked. </summary>
        public bool IsHotter(float limit) => Slot?.Itemstack != null && tracker?.Temperature > limit;

        /// <summary> Lets go of the old slot if the tracked slot changes. </summary>
        /// <param name="fade"> Make the old slot's particles vanish quickly instead of offering its effects to other trackers. </param>
        public void Release(ItemSlot? slot, bool fade)
        {
            if (tracker == null || ReferenceEquals(slot, Slot))
            {
                return;
            }

            tracker.ReleaseEffects(fade);
            tracker.Stop(false);
            tracker = null;
            Slot = null;
        }

        public void Update(ItemParticleDriver driver, ItemSlot? slot, double time)
        {
            if (!ReferenceEquals(slot, Slot))
            {
                ParticleAnchor? anchor = slot == null ? null : trigger == EnumParticleTrigger.Hover ? new HoverAnchor(slot) : new HeldAnchor(slot);
                tracker = anchor == null ? null : new ItemTracker(driver.api, anchor, trigger, slot);
                Slot = slot;
            }

            tracker?.Update(slot?.Itemstack, time);
        }

        public void Reset()
        {
            tracker?.Stop(false);
            tracker = null;
            Slot = null;
        }
    }
}
