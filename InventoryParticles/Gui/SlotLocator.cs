using System;
using System.Collections.Generic;
using InventoryParticles.Api;
using Vintagestory.API.Client;
using Vintagestory.API.Common;

namespace InventoryParticles.Gui;

/// <summary> Turns anchors into screen positions. </summary>
internal interface IAnchorResolver
{
    /// <summary> Edge length of an inventory slot at the current GUI scale: the effect unit. </summary>
    float SlotSize { get; }

    bool TryGetHoveredRect(ItemSlot slot, out GuiItemRect rect);

    bool TryGetSlotRect(ItemSlot slot, out GuiItemRect rect);

    /// <summary> Like <see cref="TryGetSlotRect"/>, but looks in HUDs (the hotbar) first. </summary>
    bool TryGetHeldRect(ItemSlot slot, out GuiItemRect rect);

    bool TryGetCursorRect(out GuiItemRect rect);

    void ReportTrackerError(Exception e);
}

/// <summary>
/// Finds where slots are drawn by walking the open dialogs' slot grids, the same data the grids render from.
/// The grid list is refreshed once per frame by <see cref="BeginFrame"/>.
/// </summary>
internal sealed class SlotLocator : IAnchorResolver
{
    // HudMouseTools draws the held stack in a slot whose top left corner is 5 - 48/4 GUI pixels from the mouse.
    private const double CursorSlotCenterOffset = 5 + 48 / 4.0;
    // InventoryItemRenderer draws items (not blocks) a little up and left of the slot centre.
    private const double ItemShiftX = -3;
    private const double ItemShiftY = -1;

    private readonly ICoreClientAPI capi;
    private readonly ILogger logger;
    private readonly List<GuiElementItemSlotGridBase> grids = new();
    private readonly List<GuiElementItemSlotGridBase> hudGrids = new();
    private GuiDialog? ignoredDialog;
    private bool trackerErrorLogged;
    private bool cursorProviderErrorLogged;

    public SlotLocator(ICoreClientAPI capi, ILogger logger)
    {
        this.capi = capi;
        this.logger = logger;
    }

    public float SlotSize => (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize);

    /// <summary> Another mod's idea of where the stack on the cursor is drawn; see <see cref="IInventoryParticlesApi.SetCursorRectProvider"/>. </summary>
    public Func<GuiItemRect?>? CursorRectProvider { get; set; }

    /// <summary> The particle layer itself, which has no slots. </summary>
    public void Ignore(GuiDialog? dialog) => ignoredDialog = dialog;

    public void BeginFrame()
    {
        grids.Clear();
        hudGrids.Clear();

        foreach (var dialog in capi.Gui.OpenedGuis)
        {
            if (dialog == null || ReferenceEquals(dialog, ignoredDialog) || !dialog.IsOpened())
            {
                continue;
            }

            var into = dialog.DialogType == EnumDialogType.HUD ? hudGrids : grids;

            foreach (var composer in dialog.Composers.Values)
            {
                if (composer?.interactiveElements != null && composer.Enabled)
                {
                    Collect(composer.interactiveElements.Values, into);
                }
            }
        }
    }

    public bool TryGetHoveredRect(ItemSlot slot, out GuiItemRect rect)
    {
        rect = default;

        if (capi.Input.MouseGrabbed)
        {
            return false;
        }

        return TryGetHoveredRect(grids, slot, out rect) || TryGetHoveredRect(hudGrids, slot, out rect);
    }

    public bool TryGetSlotRect(ItemSlot slot, out GuiItemRect rect) => TryFindSlot(grids, slot, out rect) || TryFindSlot(hudGrids, slot, out rect);

    public bool TryGetHeldRect(ItemSlot slot, out GuiItemRect rect) => TryFindSlot(hudGrids, slot, out rect) || TryFindSlot(grids, slot, out rect);

    public bool TryGetCursorRect(out GuiItemRect rect)
    {
        rect = default;
        var inventory = capi.World?.Player?.InventoryManager;
        var slot = inventory?.MouseItemSlot;

        // The same condition under which HudMouseTools draws the held stack.
        if (slot?.Itemstack == null || capi.Input.MouseGrabbed || inventory!.OpenedInventories.Count == 0)
        {
            return false;
        }

        if (TryGetProvidedCursorRect(out rect))
        {
            return true;
        }

        double offset = GuiElement.scaled(CursorSlotCenterOffset);
        rect = WithItemShift(capi.Input.MouseX + offset, capi.Input.MouseY + offset, slot.Itemstack);
        return true;
    }

    public void ReportTrackerError(Exception e)
    {
        if (!trackerErrorLogged)
        {
            trackerErrorLogged = true;
            logger.Error("A tracked particle anchor of another mod threw an exception, treating it as hidden (logged once): {0}", e);
        }
    }

    /// <summary> Asks the provider of another mod that draws the stack on the cursor itself. False means the game's own place. </summary>
    private bool TryGetProvidedCursorRect(out GuiItemRect rect)
    {
        rect = default;

        if (CursorRectProvider is not { } provider)
        {
            return false;
        }

        GuiItemRect? provided;

        try
        {
            provided = provider();
        }
        catch (Exception e)
        {
            if (!cursorProviderErrorLogged)
            {
                cursorProviderErrorLogged = true;
                logger.Error("The cursor rect provider of another mod threw an exception, using the default cursor position (logged once): {0}", e);
            }

            return false;
        }

        if (provided is not { } value || !double.IsFinite(value.CenterX) || !double.IsFinite(value.CenterY))
        {
            return false;
        }

        rect = value.Size > 0f ? value : new GuiItemRect(value.CenterX, value.CenterY, SlotSize, value.Rotation);
        return true;
    }

    private bool TryGetHoveredRect(List<GuiElementItemSlotGridBase> list, ItemSlot slot, out GuiItemRect rect)
    {
        rect = default;
        int mouseX = capi.Input.MouseX;
        int mouseY = capi.Input.MouseY;

        foreach (var grid in list)
        {
            // hoverSlotId is the slot id (the key of renderedSlots), not an index into SlotBounds.
            if (grid.hoverSlotId < 0 || grid.SlotBounds == null || grid.renderedSlots == null)
            {
                continue;
            }

            if (!grid.renderedSlots.TryGetValue(grid.hoverSlotId, out var hovered) || !ReferenceEquals(hovered, slot))
            {
                continue;
            }

            int index = grid.renderedSlots.IndexOfKey(grid.hoverSlotId);

            if (index < 0 || index >= grid.SlotBounds.Length)
            {
                continue;
            }

            var bounds = grid.SlotBounds[index];

            // hoverSlotId is only updated when the mouse moves, not when a grid scrolls under a still mouse.
            if (bounds == null || !IsVisible(grid, bounds) || !bounds.PointInside(mouseX, mouseY))
            {
                continue;
            }

            rect = RectFor(bounds, slot);
            return true;
        }

        return false;
    }

    private static bool TryFindSlot(List<GuiElementItemSlotGridBase> list, ItemSlot slot, out GuiItemRect rect)
    {
        rect = default;

        foreach (var grid in list)
        {
            var slotBounds = grid.SlotBounds;
            var rendered = grid.renderedSlots;

            if (slotBounds == null || rendered == null)
            {
                continue;
            }

            int count = Math.Min(rendered.Count, slotBounds.Length);

            for (int i = 0; i < count; i++)
            {
                if (!ReferenceEquals(rendered.GetValueAtIndex(i), slot))
                {
                    continue;
                }

                var bounds = slotBounds[i];

                if (bounds != null && IsVisible(grid, bounds))
                {
                    rect = RectFor(bounds, slot);
                    return true;
                }
            }
        }

        return false;
    }

    private static void Collect(IEnumerable<GuiElement> elements, List<GuiElementItemSlotGridBase> into)
    {
        foreach (var element in elements)
        {
            switch (element)
            {
                case GuiElementItemSlotGridBase grid:
                    into.Add(grid);
                    break;
                case GuiElementContainer { Elements: not null } container:
                    Collect(container.Elements, into);
                    break;
            }
        }
    }

    private static bool IsVisible(GuiElementItemSlotGridBase grid, ElementBounds bounds)
    {
        var parent = grid.Bounds?.ParentBounds;
        return parent == null || bounds.PartiallyInside(parent);
    }

    private static GuiItemRect RectFor(ElementBounds bounds, ItemSlot slot)
    {
        double half = GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize) / 2.0;
        return WithItemShift(bounds.renderX + half, bounds.renderY + half, slot.Itemstack);
    }

    private static GuiItemRect WithItemShift(double centerX, double centerY, ItemStack? stack)
    {
        if (stack?.Class == EnumItemClass.Item)
        {
            centerX += GuiElement.scaled(ItemShiftX);
            centerY += GuiElement.scaled(ItemShiftY);
        }

        return new GuiItemRect(centerX, centerY, (float)GuiElement.scaled(GuiElementPassiveItemSlot.unscaledSlotSize));
    }
}
