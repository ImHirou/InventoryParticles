using System;
using InventoryParticles.Gui;
using Vintagestory.API.Common;

namespace InventoryParticles.Api;

/// <summary>
/// Where an effect is played. The anchor is resolved every frame; while it can't be resolved
/// (closed dialog, scrolled out slot, tracker returning null) no particles are spawned.
/// </summary>
public abstract class ParticleAnchor
{
    private protected ParticleAnchor()
    {
    }

    /// <summary> A fixed point in screen pixels. <paramref name="size"/> is the effect unit in pixels; 0 uses the size of an inventory slot. </summary>
    public static ParticleAnchor Point(double x, double y, float size = 0f) => new PointAnchor(x, y, size);

    /// <summary> Follows the slot while some open dialog shows it. </summary>
    public static ParticleAnchor Slot(ItemSlot slot) => new SlotAnchor(slot ?? throw new ArgumentNullException(nameof(slot)));

    /// <summary> Follows the stack held on the mouse cursor. Hidden while no stack is held or no inventory is open. </summary>
    public static ParticleAnchor Cursor() => CursorAnchor.Instance;

    /// <summary> Asks <paramref name="tracker"/> every frame; null means hidden. Use it for things that move on their own. </summary>
    public static ParticleAnchor Tracked(Func<GuiItemRect?> tracker) => new TrackedAnchor(tracker ?? throw new ArgumentNullException(nameof(tracker)));

    /// <summary> True for the stack on the cursor, which is drawn above <see cref="EnumParticleLayer.BelowCursor"/>. </summary>
    internal virtual bool IsCursor => false;

    internal abstract bool TryResolve(IAnchorResolver resolver, out GuiItemRect rect);

    private sealed class PointAnchor(double x, double y, float size) : ParticleAnchor
    {
        internal override bool TryResolve(IAnchorResolver resolver, out GuiItemRect rect)
        {
            rect = new GuiItemRect(x, y, size > 0f ? size : resolver.SlotSize);
            return true;
        }
    }

    private sealed class SlotAnchor(ItemSlot slot) : ParticleAnchor
    {
        internal override bool TryResolve(IAnchorResolver resolver, out GuiItemRect rect) => resolver.TryGetSlotRect(slot, out rect);
    }

    private sealed class CursorAnchor : ParticleAnchor
    {
        public static readonly CursorAnchor Instance = new();

        internal override bool IsCursor => true;

        internal override bool TryResolve(IAnchorResolver resolver, out GuiItemRect rect) => resolver.TryGetCursorRect(out rect);
    }

    private sealed class TrackedAnchor(Func<GuiItemRect?> tracker) : ParticleAnchor
    {
        internal override bool TryResolve(IAnchorResolver resolver, out GuiItemRect rect)
        {
            GuiItemRect? result;

            try
            {
                result = tracker();
            }
            catch (Exception e)
            {
                resolver.ReportTrackerError(e);
                result = null;
            }

            if (result is { } value)
            {
                rect = value.Size > 0f ? value : new GuiItemRect(value.CenterX, value.CenterY, resolver.SlotSize);
                return true;
            }

            rect = default;
            return false;
        }
    }
}

/// <summary> The slot under the mouse cursor; unlike <see cref="ParticleAnchor.Slot"/> it is hidden as soon as the mouse leaves it. </summary>
internal sealed class HoverAnchor(ItemSlot slot) : ParticleAnchor
{
    internal override bool TryResolve(IAnchorResolver resolver, out GuiItemRect rect) => resolver.TryGetHoveredRect(slot, out rect);
}

/// <summary> A hand slot as the hotbar shows it. HUD slot grids are preferred over dialogs that show the same slot. </summary>
internal sealed class HeldAnchor(ItemSlot slot) : ParticleAnchor
{
    internal override bool TryResolve(IAnchorResolver resolver, out GuiItemRect rect) => resolver.TryGetHeldRect(slot, out rect);
}
