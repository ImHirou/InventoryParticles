using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace InventoryParticles.Definitions;

/// <summary> Effects from JSON assets of every domain plus effects registered in code. Code registrations win and survive reloads. </summary>
internal sealed class EffectRegistry
{
    /// <summary> Path inside the <c>config</c> asset category. </summary>
    public const string AssetFolder = "inventoryparticles/effects/";

    private const string AssetPathPrefix = "config/" + AssetFolder;

    private readonly Dictionary<AssetLocation, GuiParticleEffect> fromCode = new();
    private readonly Dictionary<AssetLocation, GuiParticleEffect> fromAssets = new();
    private AssetLocation[]? codes;

    public IReadOnlyCollection<AssetLocation> Codes => codes ??= fromAssets.Keys.Concat(fromCode.Keys).Distinct().OrderBy(code => code.ToString()).ToArray();

    public int AssetCount => fromAssets.Count;

    public int CodeCount => fromCode.Count;

    public void Register(AssetLocation code, GuiParticleEffect effect, ILogger? logger)
    {
        ArgumentNullException.ThrowIfNull(code);
        ArgumentNullException.ThrowIfNull(effect);
        Normalize(effect, code.ToString(), logger);
        fromCode[code] = effect;
        codes = null;
    }

    public bool TryGet(AssetLocation code, [NotNullWhen(true)] out GuiParticleEffect? effect) =>
        fromCode.TryGetValue(code, out effect) || fromAssets.TryGetValue(code, out effect);

    /// <summary> Replaces the effects loaded from assets. Broken files are logged and skipped. </summary>
    public void LoadAssets(IAssetManager assets, ILogger logger)
    {
        fromAssets.Clear();
        codes = null;

        foreach (var asset in assets.GetManyInCategory("config", AssetFolder))
        {
            if (!asset.Location.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            var code = CodeFromAsset(asset.Location);

            try
            {
                var effect = asset.ToObject<GuiParticleEffect>();

                if (effect == null)
                {
                    logger.Warning("Particle effect {0} is empty, skipping it.", asset.Location);
                    continue;
                }

                Normalize(effect, code.ToString(), logger);
                fromAssets[code] = effect;
            }
            catch (Exception e)
            {
                logger.Error("Failed to load particle effect {0}, skipping it: {1}", asset.Location, e.Message);
            }
        }
    }

    /// <summary> <c>domain:config/inventoryparticles/effects/sub/name.json</c> becomes <c>domain:sub/name</c>. </summary>
    internal static AssetLocation CodeFromAsset(AssetLocation location)
    {
        string path = location.Path;

        if (path.StartsWith(AssetPathPrefix, StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(AssetPathPrefix.Length);
        }

        if (path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
        {
            path = path.Substring(0, path.Length - ".json".Length);
        }

        return new AssetLocation(location.Domain, path);
    }

    /// <summary> Replaces missing values with defaults so the particle code never sees nulls. </summary>
    internal static void Normalize(GuiParticleEffect effect, string context, ILogger? logger)
    {
        effect.Emitters ??= Array.Empty<GuiEmitterProps>();

        if (effect.Emitters.Any(emitter => emitter == null))
        {
            logger?.Warning("Particle effect {0} has empty emitters, ignoring them.", context);
            effect.Emitters = effect.Emitters.Where(emitter => emitter != null).ToArray();
        }

        foreach (var props in effect.Emitters)
        {
            Normalize(props, context, logger);
        }
    }

    private static void Normalize(GuiEmitterProps props, string context, ILogger? logger)
    {
        string name = props.Code != null ? $"{context}/{props.Code}" : context;

        props.Rate ??= NatFloat.createUniform(0f, 0f);
        props.Size ??= NatFloat.createUniform(0.1f, 0f);
        props.LifeLength ??= NatFloat.createUniform(1f, 0f);
        props.GravityEffect ??= NatFloat.createUniform(0f, 0f);
        props.Swirl ??= NatFloat.createUniform(0f, 0f);
        props.Hang ??= NatFloat.createUniform(0f, 0f);
        props.SpawnArea ??= new SpawnAreaProps();
        props.Wobble ??= new WobbleProps();
        props.Origin = PadFloats(props.Origin, 2);
        props.Velocity = PadNatFloats(props.Velocity, 2);

        if (props.LifeLength.avg <= 0f)
        {
            logger?.Warning("Particle emitter {0} has a lifeLength of {1}, using 1 second.", name, props.LifeLength.avg);
            props.LifeLength = NatFloat.createUniform(1f, 0f);
        }

        if (props.HsvaColor != null && (props.HsvaColor.Length < 4 || props.HsvaColor.Any(value => value == null)))
        {
            logger?.Warning("Particle emitter {0} needs 4 hsvaColor values (hue, saturation, value, alpha), ignoring them.", name);
            props.HsvaColor = null;
        }

        if (props.Color != null && props.Color.Length < 3)
        {
            logger?.Warning("Particle emitter {0} needs 3 or 4 color values (red, green, blue, alpha), ignoring them.", name);
            props.Color = null;
        }

        if (props.SpawnArea.InnerRadius > props.SpawnArea.Radius)
        {
            (props.SpawnArea.InnerRadius, props.SpawnArea.Radius) = (props.SpawnArea.Radius, props.SpawnArea.InnerRadius);
        }
    }

    private static float[] PadFloats(float[]? values, int length)
    {
        if (values != null && values.Length >= length)
        {
            return values;
        }

        var padded = new float[length];
        values?.CopyTo(padded, 0);
        return padded;
    }

    private static NatFloat[] PadNatFloats(NatFloat[]? values, int length)
    {
        var padded = new NatFloat[Math.Max(length, values?.Length ?? 0)];

        for (int i = 0; i < padded.Length; i++)
        {
            padded[i] = values != null && i < values.Length && values[i] != null ? values[i] : NatFloat.createUniform(0f, 0f);
        }

        return padded;
    }
}
