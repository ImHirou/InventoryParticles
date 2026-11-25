using System;
using System.Collections.Generic;
using InventoryParticles.Api;
using InventoryParticles.Particles;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace InventoryParticles.Gui;

/// <summary>
/// Keeps the effects of one item stack running at one anchor. The rules are checked again whenever the stack
/// changes and a few times per second, so effects follow the temperature.
/// </summary>
internal sealed class ItemTracker : IPlayback
{
    private const double ReevaluateSeconds = 0.25;

    private readonly InventoryParticlesApi api;
    private readonly Dictionary<AssetLocation, EffectInstance> running = new();
    private readonly List<ItemParticleMatch> matches = new();
    private readonly HashSet<AssetLocation> seen = new();
    private readonly List<AssetLocation> finished = new();
    private ItemStack? lastStack;
    private CollectibleObject? evaluated;
    private double nextEvaluation;

    public ItemTracker(InventoryParticlesApi api, ParticleAnchor anchor, EnumParticleTrigger trigger, ItemSlot? slot, ItemStack? fixedStack = null, PlayOptions? options = null)
    {
        this.api = api;
        Anchor = anchor;
        Trigger = trigger;
        Slot = slot;
        FixedStack = fixedStack;
        Options = options;
    }

    public ParticleAnchor Anchor { get; private set; }

    public EnumParticleTrigger Trigger { get; }

    public ItemSlot? Slot { get; set; }

    /// <summary> The stack given to <see cref="IInventoryParticlesApi.PlayItem"/>. </summary>
    public ItemStack? FixedStack { get; }

    public PlayOptions? Options { get; }

    public double EndTime { get; set; } = double.PositiveInfinity;

    public bool Stopped { get; private set; }

    public bool Alive => !Stopped;

    public bool HasEffects => running.Count > 0;

    /// <summary> Last debug summary shown in chat, to only report changes. </summary>
    internal string? DebugSummary { get; set; }

    /// <summary> Temperature of the stack at the last evaluation, null without one. </summary>
    public float? Temperature { get; private set; }

    private float intensityMultiplier = 1f;

    public void Update(ItemStack? stack, double time)
    {
        if (Stopped)
        {
            return;
        }

        if (time >= EndTime)
        {
            Stop(false);
            return;
        }

        if (stack == null)
        {
            StopEffects(fade: false);
            Temperature = null;
            return;
        }

        if (ReferenceEquals(stack, lastStack) && time < nextEvaluation)
        {
            return;
        }

        lastStack = stack;
        nextEvaluation = time + ReevaluateSeconds;
        Evaluate(stack);
    }

    /// <summary>
    /// The item left this tracker (or this tracker leaves its slot). Without <paramref name="fade"/> the effects are offered to the
    /// tracker the item moves to this frame, so that their particles follow it; with it they vanish quickly.
    /// </summary>
    public void ReleaseEffects(bool fade)
    {
        var collectible = evaluated;

        foreach (var instance in running.Values)
        {
            if (fade)
            {
                instance.StopAndFade();
            }
            else
            {
                instance.Stop(false);
                api.LeaveEffect(instance, collectible, Slot, Trigger == EnumParticleTrigger.Cursor);
            }
        }

        running.Clear();
        lastStack = null;
        evaluated = null;
        nextEvaluation = 0;
    }

    /// <summary> Stops the current effects but keeps tracking; they start again when a stack shows up. </summary>
    public void StopEffects(bool fade)
    {
        foreach (var instance in running.Values)
        {
            if (fade)
            {
                instance.StopAndFade();
            }
            else
            {
                instance.Stop(false);
            }
        }

        running.Clear();
        lastStack = null;
        evaluated = null;
        nextEvaluation = 0;
    }

    public void Stop(bool clearParticles)
    {
        Stopped = true;

        foreach (var instance in running.Values)
        {
            instance.Stop(clearParticles);
        }

        running.Clear();
    }

    public void SetIntensity(float intensity)
    {
        intensityMultiplier = float.IsFinite(intensity) ? Math.Max(0f, intensity) : 0f;
        // Apply on the next update.
        nextEvaluation = 0;
    }

    public void SetAnchor(ParticleAnchor anchor)
    {
        Anchor = anchor ?? throw new ArgumentNullException(nameof(anchor));

        foreach (var instance in running.Values)
        {
            instance.SetAnchor(anchor);
        }
    }

    private void Evaluate(ItemStack stack)
    {
        float? temperature = api.CollectItemParticles(stack, Slot, Trigger, matches);
        Temperature = temperature;
        // A different item took the place of the old one (a swap with the cursor): its effects may follow it.
        var previous = evaluated;
        bool swapped = previous != null && !ReferenceEquals(previous, stack.Collectible);
        evaluated = stack.Collectible;
        seen.Clear();

        foreach (var match in matches)
        {
            if (match?.Effect == null || !seen.Add(match.Effect))
            {
                continue;
            }

            if (!running.TryGetValue(match.Effect, out var instance) || instance.Stopped)
            {
                instance = api.StartItemEffect(match.Effect, Anchor, stack, Slot);

                if (instance == null)
                {
                    running.Remove(match.Effect);
                    continue;
                }

                running[match.Effect] = instance;
            }

            Apply(instance, match, temperature);
        }

        finished.Clear();

        foreach (var (code, instance) in running)
        {
            if (!seen.Contains(code) || instance.Stopped)
            {
                finished.Add(code);
            }
        }

        foreach (var code in finished)
        {
            var instance = running[code];
            instance.Stop(false);
            running.Remove(code);

            if (swapped)
            {
                api.LeaveEffect(instance, previous, Slot, Trigger == EnumParticleTrigger.Cursor);
            }
        }

        api.ReportItemParticles(this, stack, temperature, matches);
    }

    private void Apply(EffectInstance instance, ItemParticleMatch match, float? temperature)
    {
        var options = Options;
        instance.Intensity = Math.Max(0f, match.Intensity) * intensityMultiplier * (options?.Intensity ?? 1f);
        instance.Scale = match.Scale * (options?.Scale ?? 1f);
        instance.OriginX = match.OriginX + (options?.OriginX ?? 0f);
        instance.OriginY = match.OriginY + (options?.OriginY ?? 0f);
        instance.Temperature = temperature;
        instance.Layer = options?.Layer;
        instance.FadeWhenHidden = options?.FadeWhenHidden ?? true;
        instance.SetTint(Multiply(match.Tint, options?.Tint));
    }

    private static Vec4f? Multiply(Vec4f? a, Vec4f? b)
    {
        if (a == null)
        {
            return b;
        }

        return b == null ? a : new Vec4f(a.X * b.X, a.Y * b.Y, a.Z * b.Z, a.W * b.W);
    }
}
