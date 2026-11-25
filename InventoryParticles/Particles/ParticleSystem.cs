using System;
using System.Collections.Generic;
using InventoryParticles.Api;
using InventoryParticles.Definitions;
using InventoryParticles.Gui;
using Vintagestory.API.MathTools;

namespace InventoryParticles.Particles;

/// <summary> Runs the effects and simulates their particles in screen space. No allocations per frame. </summary>
internal sealed class ParticleSystem
{
    /// <summary> How long particles take to vanish when their dialog closes. </summary>
    public const float FastFadeSeconds = 0.2f;

    /// <summary> An anchor hidden for longer than this makes the particles of a <see cref="EffectInstance.FadeWhenHidden"/> effect fade. </summary>
    public const float HiddenGraceSeconds = 0.1f;

    /// <summary> Upper limit of running effects, against mods that start effects without stopping them. </summary>
    public const int MaxEffects = 256;

    /// <summary> Attached particles keep their screen place on anchor jumps up to this many slots, e.g. from a slot to the cursor. </summary>
    public const float MaxRebaseSlots = 1.5f;

    private readonly Random rand;
    private readonly List<EffectInstance> effects = new();
    private readonly int[] layerCounts = new int[2];
    private GuiParticle[] particles;
    private int count;

    public ParticleSystem(int capacity, Random? random = null)
    {
        particles = new GuiParticle[Math.Max(1, capacity)];
        rand = random ?? new Random();
    }

    /// <summary> Seconds simulated so far. </summary>
    public double Time { get; private set; }

    public int Count => count;

    public int Capacity => particles.Length;

    public IReadOnlyList<EffectInstance> Effects => effects;

    public bool SpawningEnabled { get; set; } = true;

    /// <summary> Multiplies every emission rate and burst. </summary>
    public float DensityMultiplier { get; set; } = 1f;

    /// <summary> Multiplies every particle size. </summary>
    public float SizeMultiplier { get; set; } = 1f;

    /// <summary> Picks the texture of new particles. </summary>
    public Func<GuiEmitterProps, int> TextureKey { get; set; } = _ => 0;

    /// <summary> Screen size in pixels. Particles that leave the screen die; infinite keeps them all. </summary>
    public float ScreenWidth { get; set; } = float.PositiveInfinity;

    /// <inheritdoc cref="ScreenWidth"/>
    public float ScreenHeight { get; set; } = float.PositiveInfinity;

    internal ReadOnlySpan<GuiParticle> Particles => particles.AsSpan(0, count);

    public int CountIn(EnumParticleLayer layer) => layer is EnumParticleLayer.BelowCursor or EnumParticleLayer.Top ? layerCounts[(int)layer] : 0;

    public void SetCapacity(int capacity)
    {
        capacity = Math.Max(1, capacity);

        if (capacity == particles.Length)
        {
            return;
        }

        if (count > capacity)
        {
            count = capacity;
        }

        Array.Resize(ref particles, capacity);
        RecountLayers();
    }

    /// <summary> Creates an effect instance, or returns null when too many effects are running. </summary>
    public EffectInstance? Start(Vintagestory.API.Common.AssetLocation code, GuiParticleEffect effect, ParticleAnchor anchor)
    {
        if (effects.Count >= MaxEffects)
        {
            return null;
        }

        var instance = new EffectInstance(this, code, effect, anchor, rand);
        effects.Add(instance);
        return instance;
    }

    public void Clear()
    {
        foreach (var effect in effects)
        {
            effect.Stop(false);
        }

        effects.Clear();
        Array.Clear(particles, 0, count);
        count = 0;
        RecountLayers();
    }

    /// <summary>
    /// Lets a stopped effect run again at another anchor. Its free attached particles (anchor space, not hanging) keep their place on
    /// screen and move with the new anchor from there, so insects fly over to where the item is now. False if too many effects run.
    /// </summary>
    public bool Revive(EffectInstance effect, ParticleAnchor anchor)
    {
        if (!effects.Contains(effect))
        {
            if (effects.Count >= MaxEffects)
            {
                return false;
            }

            effects.Add(effect);
        }

        effect.Revive(anchor);
        return true;
    }

    /// <summary> Quickly fades the particles that are attached to an effect's item, for an item that is gone. </summary>
    public void FadeAttachedParticles(EffectInstance owner)
    {
        for (int i = 0; i < count; i++)
        {
            ref var p = ref particles[i];

            if (ReferenceEquals(p.Owner, owner) && p.Relative && double.IsNaN(p.FadeStart))
            {
                p.FadeStart = Time;
            }
        }
    }

    public void RemoveParticlesOf(EffectInstance owner)
    {
        int write = 0;

        for (int read = 0; read < count; read++)
        {
            if (!ReferenceEquals(particles[read].Owner, owner))
            {
                particles[write++] = particles[read];
            }
        }

        Array.Clear(particles, write, count - write);
        count = write;
        RecountLayers();
    }

    public void Update(float dt, IAnchorResolver resolver)
    {
        Time += dt;

        for (int i = effects.Count - 1; i >= 0; i--)
        {
            var effect = effects[i];

            if (!effect.Stopped && Time >= effect.EndTime)
            {
                effect.Expire();
            }

            if (effect.Stopped)
            {
                effects.RemoveAt(i);
                continue;
            }

            UpdateAnchor(effect, dt, resolver);

            if (effect.AnchorVisible && SpawningEnabled)
            {
                SpawnFrom(effect, dt);

                if (effect.BurstOnly)
                {
                    effect.Expire();
                }
            }
        }

        int write = 0;
        layerCounts[0] = layerCounts[1] = 0;

        for (int read = 0; read < count; read++)
        {
            if (!Step(ref particles[read], dt))
            {
                continue;
            }

            if (write != read)
            {
                particles[write] = particles[read];
            }

            layerCounts[(int)particles[write].Layer]++;
            write++;
        }

        if (write < count)
        {
            Array.Clear(particles, write, count - write);
        }

        count = write;
    }

    private void UpdateAnchor(EffectInstance effect, float dt, IAnchorResolver resolver)
    {
        if (effect.Anchor.TryResolve(resolver, out var rect))
        {
            float x = (float)rect.CenterX;
            float y = (float)rect.CenterY;

            if (effect.Rebase)
            {
                effect.Rebase = false;
                float dx = x - effect.AnchorX;
                float dy = y - effect.AnchorY;
                float limit = MaxRebaseSlots * rect.Size;

                // Farther jumps (a shift-click into the hotbar) would send insects flying across the screen; they jump along instead.
                if (dx * dx + dy * dy <= limit * limit)
                {
                    KeepAttachedInPlace(effect, dx, dy);
                }
            }

            effect.AnchorVisible = true;
            effect.AnchorX = x;
            effect.AnchorY = y;
            effect.AnchorSize = rect.Size;
            effect.SetAnchorRotation(rect.Rotation);
            effect.HiddenFor = 0f;
            effect.HideFadeTriggered = false;
            return;
        }

        effect.AnchorVisible = false;
        effect.HiddenFor += dt;

        if (effect.FadeWhenHidden && !effect.HideFadeTriggered && effect.HiddenFor >= HiddenGraceSeconds)
        {
            effect.HideFadeTriggered = true;
            effect.FadeCutoff = Time;
        }
    }

    /// <summary>
    /// The anchor jumped by (<paramref name="dx"/>, <paramref name="dy"/>). Free anchor-space particles keep their screen position;
    /// hanging ones (a drop under a bucket) stay on the item and jump with it.
    /// </summary>
    private void KeepAttachedInPlace(EffectInstance effect, float dx, float dy)
    {
        for (int i = 0; i < count; i++)
        {
            ref var p = ref particles[i];

            if (ReferenceEquals(p.Owner, effect) && p.AnchorSpace && !p.ItemFrame)
            {
                p.X -= dx;
                p.Y -= dy;
            }
        }
    }

    private void SpawnFrom(EffectInstance effect, float dt)
    {
        var emitters = effect.Effect.Emitters ?? Array.Empty<GuiEmitterProps>();
        var accumulators = effect.Accumulators;
        float multiplier = effect.Intensity * DensityMultiplier;

        for (int k = 0; k < emitters.Length && k < accumulators.Length; k++)
        {
            var props = emitters[k];

            if (props == null || !Allows(props, effect))
            {
                accumulators[k] = 0f;
                continue;
            }

            if (!effect.BurstDone)
            {
                int burst = effect.BurstOverride >= 0 ? effect.BurstOverride : props.Burst;
                int amount = (int)MathF.Round(burst * multiplier);

                for (int j = 0; j < amount && Spawn(effect, props); j++)
                {
                }
            }

            if (effect.BurstOnly)
            {
                continue;
            }

            accumulators[k] += Math.Max(0f, Next(props.Rate)) * multiplier * dt;

            while (accumulators[k] >= 1f)
            {
                accumulators[k] -= 1f;

                if (!Spawn(effect, props))
                {
                    accumulators[k] = 0f;
                    break;
                }
            }
        }

        effect.BurstDone = true;
    }

    private static bool Allows(GuiEmitterProps props, EffectInstance effect)
    {
        if (effect.Intensity < props.MinIntensity)
        {
            return false;
        }

        if (effect.Temperature is not float temperature)
        {
            return true;
        }

        return (props.MinTemperature is not float min || temperature >= min) && (props.MaxTemperature is not float max || temperature <= max);
    }

    private bool Spawn(EffectInstance effect, GuiEmitterProps props)
    {
        if (count >= particles.Length)
        {
            return false;
        }

        float unit = effect.AnchorSize;
        // Definitions are normalized on registration, but mods may still edit them afterwards.
        var origin = props.Origin ?? Array.Empty<float>();
        float originX = ((origin.Length > 0 ? origin[0] : 0f) + effect.OriginX) * unit;
        float originY = -((origin.Length > 1 ? origin[1] : 0f) + effect.OriginY) * unit;
        SampleArea(props.SpawnArea, out float areaX, out float areaY);
        float relativeX = originX + areaX * unit;
        float relativeY = originY - areaY * unit;

        ref var p = ref particles[count++];
        p = default;
        p.Owner = effect;
        p.Props = props;
        p.AnchorSpace = props.Space == EnumParticleSpace.Anchor;
        p.Hang = Math.Max(0f, Next(props.Hang));
        // A hanging particle stays on the item until it lets go, even if it moves in screen space afterwards.
        p.Clinging = p.Hang > 0f && !p.AnchorSpace;
        // Meanwhile it sits on the turned item and turns with it; every other particle starts where the turned origin is now.
        p.ItemFrame = p.Hang > 0f;

        if (!p.ItemFrame)
        {
            effect.Turn(ref relativeX, ref relativeY);
            effect.Turn(ref originX, ref originY);
        }

        if (p.Relative)
        {
            p.X = relativeX;
            p.Y = relativeY;
            p.CenterX = originX;
            p.CenterY = originY;
        }
        else
        {
            p.X = effect.AnchorX + relativeX;
            p.Y = effect.AnchorY + relativeY;
            p.CenterX = effect.AnchorX + originX;
            p.CenterY = effect.AnchorY + originY;
        }

        var velocity = props.Velocity ?? Array.Empty<NatFloat>();
        p.VX = Next(velocity.Length > 0 ? velocity[0] : null) * unit;
        p.VY = -Next(velocity.Length > 1 ? velocity[1] : null) * unit;
        p.Gravity = Next(props.GravityEffect) * unit;
        p.Drag = Math.Max(0f, props.Drag);
        p.Drift = props.RandomVelocityChange * unit;
        p.Swirl = Next(props.Swirl) * unit;
        p.Attract = Math.Max(0f, props.Attraction);

        if (props.Wobble is { Amplitude: not 0f } wobble)
        {
            p.WobbleAmp = wobble.Amplitude * unit;
            p.WobbleFreq = wobble.Frequency;
            p.WobblePhase = (float)(rand.NextDouble() * Math.PI * 2.0);
        }

        p.Life = Math.Max(0.02f, Next(props.LifeLength));
        p.Size0 = Math.Max(0f, Next(props.Size)) * unit * effect.Scale * SizeMultiplier;
        p.Stretch = props.Stretch > 0f && unit > 0f ? props.Stretch / unit : 0f;

        SampleColor(props, out float r, out float g, out float b, out float a);
        p.R0 = r * effect.TintR;
        p.G0 = g * effect.TintG;
        p.B0 = b * effect.TintB;
        p.A0 = a * effect.TintA;

        p.Texture = TextureKey(props);
        p.Layer = ResolveLayer(effect.Layer ?? props.Layer, effect.Anchor);
        p.Glow = props.Blend == EnumGuiParticleBlend.Glow;
        p.SpawnTime = Time;
        p.FadeStart = double.NaN;
        return true;
    }

    private bool Step(ref GuiParticle p, float dt)
    {
        p.Age += dt;

        if (p.Age >= p.Life)
        {
            return false;
        }

        var owner = p.Owner;

        if (double.IsNaN(p.FadeStart) && !double.IsNaN(owner.FadeCutoff) && p.SpawnTime <= owner.FadeCutoff)
        {
            p.FadeStart = Time;
        }

        if (!double.IsNaN(p.FadeStart) && Time - p.FadeStart >= FastFadeSeconds)
        {
            return false;
        }

        if (p.Age < p.Hang)
        {
            return true;
        }

        if (p.ItemFrame)
        {
            // Leave the item's frame as it is turned now.
            p.ItemFrame = false;
            owner.Turn(ref p.X, ref p.Y);
            owner.Turn(ref p.CenterX, ref p.CenterY);
        }

        if (p.Clinging)
        {
            // Let go of the item where it is now.
            p.Clinging = false;
            p.X += owner.AnchorX;
            p.Y += owner.AnchorY;
            p.CenterX += owner.AnchorX;
            p.CenterY += owner.AnchorY;
        }

        if (p.Drift != 0f)
        {
            p.VX += ((float)rand.NextDouble() * 2f - 1f) * p.Drift * dt;
            p.VY += ((float)rand.NextDouble() * 2f - 1f) * p.Drift * dt;
        }

        p.VY += p.Gravity * dt;

        if (p.Attract > 0f)
        {
            p.VX -= (p.X - p.CenterX) * p.Attract * dt;
            p.VY -= (p.Y - p.CenterY) * p.Attract * dt;
        }

        if (p.Drag > 0f)
        {
            float keep = Math.Max(0f, 1f - p.Drag * dt);
            p.VX *= keep;
            p.VY *= keep;
        }

        p.X += p.VX * dt;
        p.Y += p.VY * dt;

        if (p.Swirl != 0f)
        {
            float dx = p.X - p.CenterX;
            float dy = p.Y - p.CenterY;
            float length = MathF.Sqrt(dx * dx + dy * dy);

            if (length > 0.5f)
            {
                p.X -= dy / length * p.Swirl * dt;
                p.Y += dx / length * p.Swirl * dt;
            }
        }

        return IsOnScreen(in p);
    }

    private bool IsOnScreen(in GuiParticle p)
    {
        if (!float.IsFinite(ScreenWidth) || !float.IsFinite(ScreenHeight))
        {
            return true;
        }

        ScreenPosition(in p, out float x, out float y);

        // Stretched particles are up to four times as tall.
        float margin = p.Size0 * 4f + 2f;
        return x > -margin && y > -margin && x < ScreenWidth + margin && y < ScreenHeight + margin;
    }

    /// <summary> Where a particle is on screen, before its wobble. </summary>
    internal static void ScreenPosition(in GuiParticle p, out float x, out float y)
    {
        x = p.X;
        y = p.Y;

        if (!p.Relative)
        {
            return;
        }

        var owner = p.Owner;

        if (p.ItemFrame)
        {
            owner.Turn(ref x, ref y);
        }

        x += owner.AnchorX;
        y += owner.AnchorY;
    }

    /// <summary> <see cref="EnumParticleLayer.AboveItem"/> is above the stack on the cursor only when the effect plays there. </summary>
    internal static EnumParticleLayer ResolveLayer(EnumParticleLayer layer, ParticleAnchor anchor) => layer switch
    {
        EnumParticleLayer.Top => EnumParticleLayer.Top,
        EnumParticleLayer.AboveItem => anchor.IsCursor ? EnumParticleLayer.Top : EnumParticleLayer.BelowCursor,
        _ => EnumParticleLayer.BelowCursor
    };

    private void SampleArea(SpawnAreaProps? area, out float x, out float y)
    {
        x = 0f;
        y = 0f;

        if (area == null)
        {
            return;
        }

        switch (area.Type)
        {
            case EnumSpawnArea.Box:
                x = ((float)rand.NextDouble() - 0.5f) * area.Width;
                y = ((float)rand.NextDouble() - 0.5f) * area.Height;
                break;
            case EnumSpawnArea.Circle:
            case EnumSpawnArea.Ring:
                float inner = area.Type == EnumSpawnArea.Ring ? area.InnerRadius : 0f;
                float outer = area.Radius;
                float radius = MathF.Sqrt(inner * inner + (outer * outer - inner * inner) * (float)rand.NextDouble());
                float angle = (float)(rand.NextDouble() * Math.PI * 2.0);
                x = radius * MathF.Cos(angle);
                y = radius * MathF.Sin(angle);
                break;
        }
    }

    private void SampleColor(GuiEmitterProps props, out float r, out float g, out float b, out float a)
    {
        if (props.HsvaColor is { Length: >= 4 } hsva)
        {
            int hue = ((int)MathF.Round(Next(hsva[0])) % 256 + 256) % 256;
            int color = ColorUtil.HsvToRgba(hue, Byte(Next(hsva[1])), Byte(Next(hsva[2])), Byte(Next(hsva[3])));
            // HsvToRgba puts red in the lowest byte.
            r = color & 0xFF;
            g = (color >> 8) & 0xFF;
            b = (color >> 16) & 0xFF;
            a = (color >> 24) & 0xFF;
            return;
        }

        if (props.Color is { Length: >= 3 } rgba)
        {
            r = Byte(rgba[0]);
            g = Byte(rgba[1]);
            b = Byte(rgba[2]);
            a = rgba.Length >= 4 ? Byte(rgba[3]) : 255f;
            return;
        }

        r = g = b = a = 255f;
    }

    private static int Byte(float value) => Math.Clamp((int)MathF.Round(value), 0, 255);

    private float Next(NatFloat? value) => value?.nextFloat(1f, rand) ?? 0f;

    private void RecountLayers()
    {
        layerCounts[0] = layerCounts[1] = 0;

        for (int i = 0; i < count; i++)
        {
            layerCounts[(int)particles[i].Layer]++;
        }
    }
}
