using Vintagestory.API.MathTools;

namespace InventoryParticles.Api;

/// <summary> Optional settings for <see cref="IInventoryParticlesApi.Play"/>, <see cref="IInventoryParticlesApi.PlayItem"/> and <see cref="IInventoryParticlesApi.Burst"/>. </summary>
public sealed class PlayOptions
{
    /// <summary> Multiplies the emission rate and burst sizes. Emitters with a higher <c>minIntensity</c> stay silent. </summary>
    public float Intensity = 1f;

    /// <summary> Stops emitting after this many seconds. Null or 0 plays until stopped. </summary>
    public float? Duration;

    /// <summary> Temperature in °C checked against the emitters' <c>minTemperature</c>/<c>maxTemperature</c>. Null ignores those limits. </summary>
    public float? Temperature;

    /// <summary> Multiplies the particle sizes. </summary>
    public float Scale = 1f;

    /// <summary> Multiplies the particle colors (red, green, blue, alpha, each 0..1). </summary>
    public Vec4f? Tint;

    /// <summary> Moves the effect's origin, in effect units (1 = the edge of a slot), +Y up. </summary>
    public float OriginX;

    /// <inheritdoc cref="OriginX"/>
    public float OriginY;

    /// <summary> Overrides the layer of every emitter. </summary>
    public EnumParticleLayer? Layer;

    /// <summary> When the anchor stays hidden for a moment (closed dialog, scrolled out), its particles fade out quickly. </summary>
    public bool FadeWhenHidden = true;

    /// <summary> Only for <see cref="IInventoryParticlesApi.PlayItem"/>: the trigger rules are checked against. </summary>
    public EnumParticleTrigger Trigger = EnumParticleTrigger.Custom;
}
