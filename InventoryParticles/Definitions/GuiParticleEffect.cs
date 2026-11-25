using System;
using InventoryParticles.Api;
using Newtonsoft.Json;
using Vintagestory.API.MathTools;

namespace InventoryParticles.Definitions;

public enum EnumGuiParticleShape
{
    /// <summary> A square whose outer edge fades out, like vanilla quad particles. </summary>
    Square,
    /// <summary> A round dot with a soft edge. </summary>
    Dot,
    /// <summary> A very soft round blob, for smoke and steam. </summary>
    Puff,
    /// <summary> A four-pointed twinkle. </summary>
    Star,
    /// <summary> A liquid drop: round at the bottom, pointed at the top. </summary>
    Drop
}

public enum EnumGuiParticleBlend
{
    /// <summary> Normal alpha blending. </summary>
    Standard,
    /// <summary> Additive: brightens what is behind, for fire, sparks and magic. </summary>
    Glow
}

public enum EnumParticleSpace
{
    /// <summary> Particles stay where they were spawned when the anchor moves. </summary>
    Screen,
    /// <summary> Particles move along with the anchor. </summary>
    Anchor
}

public enum EnumSpawnArea
{
    Point,
    /// <summary> A <c>width</c> × <c>height</c> rectangle centred on the origin. </summary>
    Box,
    /// <summary> A disc of <c>radius</c>. </summary>
    Circle,
    /// <summary> A ring between <c>innerRadius</c> and <c>radius</c>. </summary>
    Ring
}

/// <summary> A GUI particle effect: one or more emitters. Loaded from <c>config/inventoryparticles/effects/*.json</c> or registered in code. </summary>
public sealed class GuiParticleEffect
{
    public GuiEmitterProps[] Emitters = Array.Empty<GuiEmitterProps>();
}

/// <summary>
/// One emitter of a GUI effect. Lengths are in effect units: 1 is the edge of an inventory slot. +Y points up,
/// a positive <see cref="GravityEffect"/> pulls down. Colors and opacity use 0..255 like vanilla particles.
/// <see cref="NatFloat"/> values must be objects such as <c>{ "avg": 1, "var": 0.5 }</c>.
/// </summary>
public sealed class GuiEmitterProps
{
    /// <summary> Optional name, shown in logs. </summary>
    public string? Code;

    public EnumGuiParticleShape Shape = EnumGuiParticleShape.Square;

    /// <summary> Texture asset used instead of <see cref="Shape"/>, e.g. <c>game:textures/particle/smoke-3.png</c>. White textures tint best. </summary>
    public string? Texture;

    public EnumGuiParticleBlend Blend = EnumGuiParticleBlend.Standard;

    public EnumParticleLayer Layer = EnumParticleLayer.BelowCursor;

    public EnumParticleSpace Space = EnumParticleSpace.Screen;

    /// <summary> Particles per second at intensity 1. </summary>
    public NatFloat Rate = NatFloat.createUniform(5f, 0f);

    /// <summary> Particles spawned at once when the effect starts (scaled by intensity). </summary>
    public int Burst;

    /// <summary> Only emit while the temperature is at least this, in °C. Ignored when the effect has no temperature. </summary>
    public float? MinTemperature;

    /// <summary> Only emit while the temperature is at most this, in °C. Ignored when the effect has no temperature. </summary>
    public float? MaxTemperature;

    /// <summary> Only emit while the effect's intensity is at least this. </summary>
    public float MinIntensity;

    /// <summary> [x, y] offset of the spawn area from the item centre. </summary>
    public float[] Origin = { 0f, 0f };

    public SpawnAreaProps SpawnArea = new();

    /// <summary> Hue, saturation, value and alpha, each 0..255 (vanilla scale). Takes precedence over <see cref="Color"/>. </summary>
    public NatFloat[]? HsvaColor;

    /// <summary> [r, g, b] or [r, g, b, a], each 0..255. White when neither this nor <see cref="HsvaColor"/> is set. </summary>
    public int[]? Color;

    /// <summary> Red over the lifetime: <c>value = transform(startRed, age / life)</c>, like vanilla. </summary>
    public EvolvingNatFloat RedEvolve = EvolvingNatFloat.NoValueSet;

    public EvolvingNatFloat GreenEvolve = EvolvingNatFloat.NoValueSet;

    public EvolvingNatFloat BlueEvolve = EvolvingNatFloat.NoValueSet;

    /// <summary> Alpha over the lifetime, starting from the alpha of the color (0..255). </summary>
    public EvolvingNatFloat OpacityEvolve = EvolvingNatFloat.NoValueSet;

    /// <summary> Size multiplier over the lifetime, starting from 1. </summary>
    public EvolvingNatFloat SizeEvolve = EvolvingNatFloat.NoValueSet;

    /// <summary> Edge length of a particle. </summary>
    public NatFloat Size = NatFloat.createUniform(0.1f, 0f);

    /// <summary> Seconds. </summary>
    public NatFloat LifeLength = NatFloat.createUniform(1f, 0f);

    /// <summary> [x, y] starting velocity per second. </summary>
    public NatFloat[] Velocity = { NatFloat.createUniform(0f, 0f), NatFloat.createUniform(0f, 0f) };

    /// <summary> Downward acceleration per second². Negative values make particles rise faster and faster. </summary>
    public NatFloat GravityEffect = NatFloat.createUniform(0f, 0f);

    /// <summary> Fraction of the velocity lost per second. </summary>
    public float Drag;

    /// <summary>
    /// Seconds a particle clings to the item where it spawned before it starts moving, growing from a third of its size
    /// meanwhile, like a forming drop. It follows the item while it clings, even in <see cref="EnumParticleSpace.Screen"/>.
    /// </summary>
    public NatFloat Hang = NatFloat.createUniform(0f, 0f);

    /// <summary> Draws fast particles taller: the height grows by this factor per effect unit of vertical speed, up to 4 times. </summary>
    public float Stretch;

    /// <summary> Strength of random velocity changes, like vanilla's <c>randomVelocityChange</c>. </summary>
    public float RandomVelocityChange;

    /// <summary> Speed around the origin per second; positive turns clockwise on screen. </summary>
    public NatFloat Swirl = NatFloat.createUniform(0f, 0f);

    /// <summary> Pulls particles back to the origin like a spring: acceleration = attraction × distance, per second squared. Keeps buzzing insects around the item. </summary>
    public float Attraction;

    /// <summary> Sideways sway added to the position. </summary>
    public WobbleProps Wobble = new();

    /// <summary> Seconds to fade in after spawning. </summary>
    public float FadeIn;

    /// <summary> Seconds to fade out before dying. </summary>
    public float FadeOut;
}

public sealed class SpawnAreaProps
{
    public EnumSpawnArea Type = EnumSpawnArea.Point;
    public float Width;
    public float Height;
    public float Radius;
    public float InnerRadius;
}

public sealed class WobbleProps
{
    /// <summary> Sideways distance, in effect units. </summary>
    public float Amplitude;

    /// <summary> Swings per second. </summary>
    public float Frequency = 1f;
}

/// <summary> Binds effects to items. Loaded from <c>config/inventoryparticles/rules/*.json</c> (arrays) or registered in code. </summary>
public sealed class ItemParticleRule
{
    /// <summary> Unique name, used in logs and in the config's <c>DisabledRules</c>. </summary>
    public string Code = "";

    /// <summary> Higher priorities are checked first; the first matching rule wins. </summary>
    public double Priority;

    public RuleMatch Match = new();

    /// <summary> In °C. Stacks without a temperature attribute count as 20 °C. </summary>
    public float? MinTemperature;

    public float? MaxTemperature;

    /// <summary> Maps the temperature to the intensity of the effects. </summary>
    public TemperatureIntensity? IntensityByTemperature;

    /// <summary> Only apply while the stack is at least this spoiled: 0 is fresh, 1 rotten, as the "% spoiled" in its tooltip. </summary>
    public float? MinSpoilage;

    /// <summary> Base intensity of the effects. </summary>
    public float Intensity = 1f;

    /// <summary>
    /// When to show the effects. Null means every continuous trigger (hover, cursor, held, custom); the one-shot triggers
    /// (pickup, toohot) only apply to rules that list them.
    /// </summary>
    public EnumParticleTrigger[]? Triggers;

    /// <summary> Effect codes. Without a domain they refer to the domain of the rule file (or <c>game</c> for rules registered in code). </summary>
    public string[] Effects = Array.Empty<string>();

    /// <summary> [x, y] added to every emitter's origin, e.g. to put a flame on the tip of a torch. </summary>
    public float[]? Origin;

    /// <summary> Multiplies the particle sizes. </summary>
    public float Scale = 1f;

    /// <summary> [r, g, b, a] multipliers, each 0..1. </summary>
    public float[]? Tint;

    /// <summary> Only apply while the stack is a liquid container with a liquid in it. </summary>
    public bool RequireLiquid;

    /// <summary> Multiplies the tint with the color of the liquid in the container, as the container shows it. </summary>
    public bool LiquidTint;

    /// <summary> Domain used to resolve effect codes without one. Set from the file the rule was loaded from. </summary>
    [JsonIgnore]
    public string Domain = "game";
}

/// <summary> Which collectibles a rule applies to. Both lists must match when both are given; empty lists match everything. </summary>
public sealed class RuleMatch
{
    /// <summary> Collectible codes with wildcards, e.g. <c>game:torch-*-lit-*</c>. A missing domain means <c>game</c>. </summary>
    public string[] Codes = Array.Empty<string>();

    /// <summary> Registered block or item class names, e.g. <c>BlockCookedContainer</c>. Subclasses match too. </summary>
    public string[] Classes = Array.Empty<string>();
}

/// <summary>
/// Intensity changes linearly from <see cref="Min"/> at <see cref="From"/> °C to <see cref="Max"/> at <see cref="To"/> °C and stays
/// there outside the range. <see cref="Max"/> may be lower than <see cref="Min"/>.
/// </summary>
public sealed class TemperatureIntensity
{
    public float From;
    public float To = 1000f;
    public float Min;
    public float Max = 1f;

    public float Evaluate(float temperature)
    {
        float t = To > From ? Math.Clamp((temperature - From) / (To - From), 0f, 1f) : temperature >= From ? 1f : 0f;
        return Min + (Max - Min) * t;
    }
}
