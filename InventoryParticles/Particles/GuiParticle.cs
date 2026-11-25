using InventoryParticles.Api;
using InventoryParticles.Definitions;

namespace InventoryParticles.Particles;

/// <summary>
/// One live particle. Positions and speeds are in screen pixels (y down); while <see cref="Relative"/> is set, X/Y and
/// the swirl centre are relative to the owner's anchor. Colors are 0..255 before the evolve curves are applied.
/// </summary>
internal struct GuiParticle
{
    public EffectInstance Owner;
    public GuiEmitterProps Props;

    public float X;
    public float Y;
    public float VX;
    public float VY;
    public float Gravity;
    public float Drag;
    public float Drift;
    public float Swirl;
    /// <summary> Spring pull towards the centre, per second squared. </summary>
    public float Attract;
    public float CenterX;
    public float CenterY;
    public float WobbleAmp;
    public float WobbleFreq;
    public float WobblePhase;

    public float Age;
    public float Life;
    public float Size0;
    public float R0;
    public float G0;
    public float B0;
    public float A0;

    /// <summary> Seconds the particle clings to its spawn point before moving. </summary>
    public float Hang;
    /// <summary> Height growth per pixel per second of vertical speed. </summary>
    public float Stretch;

    public double SpawnTime;
    /// <summary> Simulation time the quick fade started, NaN if it didn't. </summary>
    public double FadeStart;

    public int Texture;
    /// <summary> <see cref="EnumParticleLayer.BelowCursor"/> or <see cref="EnumParticleLayer.Top"/>; AboveItem is resolved at spawn. </summary>
    public EnumParticleLayer Layer;
    public bool Glow;
    /// <summary> Moves with the anchor for its whole life. </summary>
    public bool AnchorSpace;
    /// <summary> A screen-space particle that still hangs on the item and follows the anchor until it lets go. </summary>
    public bool Clinging;

    public readonly bool Relative => AnchorSpace || Clinging;
}
