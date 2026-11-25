using System;
using Vintagestory.API.Client;

namespace InventoryParticles.Gui;

/// <summary>
/// An invisible, always open HUD that the GUI manager draws after the inventories (draw order 0.2) and before the
/// escape menu (0.89) and the mouse tools (0.9, the held stack and the item tooltips), so those cover the particles.
/// </summary>
internal sealed class ParticleHudLayer : HudElement
{
    public const double LayerDrawOrder = 0.88;

    private readonly Action<float> render;

    public ParticleHudLayer(ICoreClientAPI capi, Action<float> render) : base(capi)
    {
        this.render = render;
    }

    public override double DrawOrder => LayerDrawOrder;

    // A focusable HUD drawn this late would take the focus from the inventory whenever another dialog closes.
    public override bool Focusable => false;

    public override float ZSize => 0f;

    public override bool ShouldReceiveMouseEvents() => false;

    public override bool ShouldReceiveKeyboardEvents() => false;

    // HudElement.OnRenderGUI would render (non-existent) composers; draw the particles instead.
    public override void OnRenderGUI(float deltaTime) => render(deltaTime);
}
