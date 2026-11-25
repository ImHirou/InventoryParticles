namespace InventoryParticles.Api;

/// <summary> Identifies something started with <see cref="IInventoryParticlesApi.Play"/> or <see cref="IInventoryParticlesApi.PlayItem"/>. </summary>
public readonly record struct ParticleHandle(long Id)
{
    /// <summary> Returned when nothing could be started. </summary>
    public static readonly ParticleHandle Invalid = default;

    public bool IsValid => Id != 0;
}
