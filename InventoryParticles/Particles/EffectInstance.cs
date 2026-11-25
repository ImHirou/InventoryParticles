using System;
using InventoryParticles.Api;
using InventoryParticles.Definitions;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace InventoryParticles.Particles;

/// <summary> Something that can be controlled through a <see cref="ParticleHandle"/>. </summary>
internal interface IPlayback
{
    bool Alive { get; }

    void Stop(bool clearParticles);

    void SetIntensity(float intensity);

    void SetAnchor(ParticleAnchor anchor);
}

/// <summary> One running effect: its anchor, settings and spawn state. Particles keep a reference to it after it stopped. </summary>
internal sealed class EffectInstance : IPlayback
{
    private readonly ParticleSystem system;

    public EffectInstance(ParticleSystem system, AssetLocation code, GuiParticleEffect effect, ParticleAnchor anchor, Random rand)
    {
        this.system = system;
        Code = code;
        Effect = effect;
        Anchor = anchor;
        Accumulators = new float[effect.Emitters?.Length ?? 0];

        // Start half way to the first particle so effects show up right away, but not all in the same frame.
        for (int i = 0; i < Accumulators.Length; i++)
        {
            Accumulators[i] = 0.5f + 0.5f * (float)rand.NextDouble();
        }
    }

    public AssetLocation Code { get; }

    public GuiParticleEffect Effect { get; }

    public ParticleAnchor Anchor { get; set; }

    public float Intensity { get; set; } = 1f;

    public float Scale { get; set; } = 1f;

    public float OriginX { get; set; }

    public float OriginY { get; set; }

    public float? Temperature { get; set; }

    public EnumParticleLayer? Layer { get; set; }

    public bool FadeWhenHidden { get; set; } = true;

    /// <summary> Simulation time at which emission stops. </summary>
    public double EndTime { get; set; } = double.PositiveInfinity;

    /// <summary> At least 0: the number of particles every emitter spawns at once, replacing its <c>burst</c>. </summary>
    public int BurstOverride { get; set; } = -1;

    /// <summary> Spawn the bursts once and stop. </summary>
    public bool BurstOnly { get; set; }

    public bool Stopped { get; private set; }

    public bool Alive => !Stopped;

    internal float TintR { get; private set; } = 1f;

    internal float TintG { get; private set; } = 1f;

    internal float TintB { get; private set; } = 1f;

    internal float TintA { get; private set; } = 1f;

    internal float[] Accumulators { get; }

    internal bool BurstDone { get; set; }

    internal bool AnchorVisible { get; set; }

    internal float AnchorX { get; set; }

    internal float AnchorY { get; set; }

    internal float AnchorSize { get; set; }

    /// <summary> How far the anchor's item is turned, in radians, clockwise on screen. See <see cref="GuiItemRect.Rotation"/>. </summary>
    internal float AnchorRotation { get; private set; }

    private float anchorCos = 1f;
    private float anchorSin;

    internal float HiddenFor { get; set; }

    internal bool HideFadeTriggered { get; set; }

    /// <summary> Particles spawned at or before this simulation time fade out quickly. NaN while nothing fades. </summary>
    internal double FadeCutoff { get; set; } = double.NaN;

    /// <summary> The anchor was swapped: move the attached particles so that they stay where they are on screen. </summary>
    internal bool Rebase { get; set; }

    internal void SetAnchorRotation(float rotation)
    {
        if (!float.IsFinite(rotation))
        {
            rotation = 0f;
        }

        if (rotation == AnchorRotation)
        {
            return;
        }

        AnchorRotation = rotation;
        anchorCos = MathF.Cos(rotation);
        anchorSin = MathF.Sin(rotation);
    }

    /// <summary> Turns an offset from the item's own frame (y down) into the screen's, by <see cref="AnchorRotation"/>. </summary>
    internal void Turn(ref float x, ref float y)
    {
        if (AnchorRotation == 0f)
        {
            return;
        }

        float turnedX = anchorCos * x - anchorSin * y;
        y = anchorSin * x + anchorCos * y;
        x = turnedX;
    }

    public void SetTint(Vec4f? tint)
    {
        TintR = tint?.X ?? 1f;
        TintG = tint?.Y ?? 1f;
        TintB = tint?.Z ?? 1f;
        TintA = tint?.W ?? 1f;
    }

    public void Stop(bool clearParticles)
    {
        Stopped = true;

        if (clearParticles)
        {
            system.RemoveParticlesOf(this);
        }
    }

    /// <summary> Stops emitting and lets the particles that are already alive fade out within a fraction of a second. </summary>
    public void StopAndFade()
    {
        Stopped = true;
        FadeCutoff = system.Time;
    }

    /// <summary> Called by the simulation when the effect is stopped because its time ran out. </summary>
    internal void Expire() => Stopped = true;

    /// <summary> Runs a stopped (not fading) effect again at another anchor, for an item that moved, e.g. from a slot to the cursor. </summary>
    internal void Revive(ParticleAnchor anchor)
    {
        Stopped = false;
        Anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
        EndTime = double.PositiveInfinity;
        // Without a position yet there is nothing to keep in place.
        Rebase = AnchorSize > 0f;
    }

    public void SetIntensity(float intensity) => Intensity = float.IsFinite(intensity) ? Math.Max(0f, intensity) : 0f;

    public void SetAnchor(ParticleAnchor anchor) => Anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));
}
