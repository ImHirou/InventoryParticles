using System;
using Vintagestory.API.Client;

namespace InventoryParticles.Gui;

/// <summary> Draws the <see cref="Api.EnumParticleLayer.Top"/> layer after the GUI manager (render order 1). </summary>
internal sealed class TopLayerRenderer : IRenderer
{
    public const double Order = 1.01;

    private readonly Action render;

    public TopLayerRenderer(Action render)
    {
        this.render = render;
    }

    public double RenderOrder => Order;

    public int RenderRange => 0;

    public void OnRenderFrame(float deltaTime, EnumRenderStage stage) => render();

    public void Dispose()
    {
    }
}
