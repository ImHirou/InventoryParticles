using System;
using System.Collections.Generic;
using System.Linq;
using InventoryParticles.Api;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.API.Util;

namespace InventoryParticles.Definitions;

/// <summary> Item rules from JSON assets of every domain plus rules registered in code. </summary>
internal sealed class RuleRegistry
{
    /// <summary> Path inside the <c>config</c> asset category. </summary>
    public const string AssetFolder = "inventoryparticles/rules/";

    /// <summary> Temperature of stacks without a temperature attribute. </summary>
    public const float RoomTemperature = 20f;

    private readonly List<ItemParticleRule> fromCode = new();
    private readonly List<ItemParticleRule> fromAssets = new();
    private readonly Dictionary<CollectibleObject, CompiledRule[]> candidates = new(ReferenceEqualityComparer.Instance);
    private readonly HashSet<string> disabled = new(StringComparer.OrdinalIgnoreCase);
    private CompiledRule[] compiled = Array.Empty<CompiledRule>();
    private System.Func<string, Type?>? classResolver;
    private IStackInspector? inspector;
    private ILogger? logger;
    private bool dirty = true;
    private bool inspectorErrorLogged;

    public int AssetCount => fromAssets.Count;

    public int CodeCount => fromCode.Count;

    /// <summary> The active rules in the order they are checked. </summary>
    public IReadOnlyList<CompiledRule> Rules
    {
        get
        {
            CompileIfNeeded();
            return compiled;
        }
    }

    /// <summary>
    /// <paramref name="resolveClass"/> turns a registered class name into its type (null if unknown).
    /// <paramref name="stackInspector"/> looks into stacks; without it, rules with liquid or spoilage conditions never apply.
    /// </summary>
    public void Attach(System.Func<string, Type?> resolveClass, ILogger log, IStackInspector? stackInspector = null)
    {
        classResolver = resolveClass;
        inspector = stackInspector;
        logger = log;
        dirty = true;
    }

    public void Register(ItemParticleRule rule)
    {
        ArgumentNullException.ThrowIfNull(rule);
        fromCode.Add(rule);
        dirty = true;
    }

    public void SetDisabled(IEnumerable<string> codes)
    {
        disabled.Clear();
        disabled.UnionWith(codes);
        dirty = true;
    }

    /// <summary> Replaces the rules loaded from assets. Broken files are logged and skipped. </summary>
    public void LoadAssets(IAssetManager assets, ILogger log)
    {
        fromAssets.Clear();
        dirty = true;

        foreach (var asset in assets.GetManyInCategory("config", AssetFolder))
        {
            if (!asset.Location.Path.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            try
            {
                var rules = asset.ToObject<ItemParticleRule[]>();

                foreach (var rule in rules ?? Array.Empty<ItemParticleRule>())
                {
                    if (rule == null)
                    {
                        continue;
                    }

                    rule.Domain = asset.Location.Domain;
                    fromAssets.Add(rule);
                }
            }
            catch (Exception e)
            {
                log.Error("Failed to load particle rules {0}, skipping the file: {1}", asset.Location, e.Message);
            }
        }
    }

    /// <summary>
    /// Adds the effects of the first rule that accepts the stack to <paramref name="matches"/>.
    /// Returns false if no rule matched. <paramref name="slot"/> is where the stack is, if known; spoilage depends on it.
    /// </summary>
    public bool Collect(ItemStack stack, ItemSlot? slot, EnumParticleTrigger trigger, float? temperature, List<ItemParticleMatch> matches)
    {
        var collectible = stack.Collectible;

        if (collectible?.Code == null)
        {
            return false;
        }

        CompileIfNeeded();

        if (!candidates.TryGetValue(collectible, out var rules))
        {
            rules = compiled.Where(rule => rule.MatchesCollectible(collectible)).ToArray();
            candidates[collectible] = rules;
        }

        foreach (var rule in rules)
        {
            if (!rule.Accepts(trigger, temperature) || !TryInspect(rule.Source, stack, slot, out var liquidColor))
            {
                continue;
            }

            rule.AddMatches(temperature, liquidColor, matches);
            return true;
        }

        return false;
    }

    /// <summary> Checks the rule's per-stack conditions. <paramref name="liquidColor"/> is set for <c>liquidTint</c>. </summary>
    private bool TryInspect(ItemParticleRule rule, ItemStack stack, ItemSlot? slot, out Vec4f? liquidColor)
    {
        liquidColor = null;

        if (rule.MinSpoilage == null && !rule.RequireLiquid && !rule.LiquidTint)
        {
            return true;
        }

        if (inspector == null)
        {
            return false;
        }

        try
        {
            if (rule.MinSpoilage is float minSpoilage && !(inspector.GetSpoilage(stack, slot) >= minSpoilage))
            {
                return false;
            }

            if (rule.RequireLiquid || rule.LiquidTint)
            {
                bool hasLiquid = inspector.TryGetLiquid(stack, out var color);

                if (rule.RequireLiquid && !hasLiquid)
                {
                    return false;
                }

                liquidColor = rule.LiquidTint ? color : null;
            }

            return true;
        }
        catch (Exception e)
        {
            if (!inspectorErrorLogged)
            {
                inspectorErrorLogged = true;
                logger?.Error("Could not inspect {0} for particle rule {1}, the rule doesn't apply (logged once): {2}", stack.Collectible?.Code, rule.Code, e);
            }

            return false;
        }
    }

    private void CompileIfNeeded()
    {
        if (!dirty)
        {
            return;
        }

        dirty = false;
        candidates.Clear();
        compiled = fromAssets.Concat(fromCode)
            .Where(rule => !disabled.Contains(rule.Code ?? ""))
            .OrderByDescending(rule => rule.Priority)
            .Select(rule => new CompiledRule(rule, classResolver, logger))
            .Where(rule => rule.CanMatch)
            .ToArray();
    }
}

/// <summary> A rule with its codes, classes and effects parsed. </summary>
internal sealed class CompiledRule
{
    private readonly AssetLocation[] codes;
    private readonly Type[] classes;
    private readonly AssetLocation[] effects;
    private readonly Vec4f? tint;
    private readonly float originX;
    private readonly float originY;

    public CompiledRule(ItemParticleRule rule, System.Func<string, Type?>? resolveClass, ILogger? logger)
    {
        Source = rule;
        string name = string.IsNullOrEmpty(rule.Code) ? "(unnamed)" : rule.Code;

        codes = (rule.Match?.Codes ?? Array.Empty<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => AssetLocation.Create(code.Trim()))
            .ToArray();

        var classNames = (rule.Match?.Classes ?? Array.Empty<string>()).Where(className => !string.IsNullOrWhiteSpace(className)).ToArray();
        var resolved = new List<Type>();

        foreach (string className in classNames)
        {
            var type = resolveClass?.Invoke(className.Trim());

            if (type == null)
            {
                logger?.Warning("Particle rule {0}: unknown block or item class {1}.", name, className);
            }
            else
            {
                resolved.Add(type);
            }
        }

        classes = resolved.ToArray();

        effects = (rule.Effects ?? Array.Empty<string>())
            .Where(code => !string.IsNullOrWhiteSpace(code))
            .Select(code => AssetLocation.Create(code.Trim(), string.IsNullOrEmpty(rule.Domain) ? "game" : rule.Domain))
            .ToArray();

        if (rule.Tint is { Length: >= 3 } t)
        {
            tint = new Vec4f(t[0], t[1], t[2], t.Length >= 4 ? t[3] : 1f);
        }

        if (rule.Origin is { Length: >= 2 } origin)
        {
            originX = origin[0];
            originY = origin[1];
        }

        if (effects.Length == 0)
        {
            logger?.Warning("Particle rule {0} has no effects and is ignored.", name);
        }

        // A rule whose classes all failed to resolve must not fall back to matching everything.
        CanMatch = effects.Length > 0 && (classNames.Length == 0 || classes.Length > 0);
    }

    public ItemParticleRule Source { get; }

    public bool CanMatch { get; }

    public IReadOnlyList<AssetLocation> Effects => effects;

    public bool MatchesCollectible(CollectibleObject collectible)
    {
        var code = collectible.Code;

        if (code == null)
        {
            return false;
        }

        if (codes.Length > 0 && !codes.Any(pattern => WildcardUtil.Match(pattern, code)))
        {
            return false;
        }

        return classes.Length == 0 || classes.Any(type => type.IsInstanceOfType(collectible));
    }

    /// <summary> Pickup and toohot happen once instead of lasting; only rules that ask for them get them. </summary>
    public static bool IsOneShot(EnumParticleTrigger trigger) => trigger is EnumParticleTrigger.Pickup or EnumParticleTrigger.TooHot;

    public bool Accepts(EnumParticleTrigger trigger, float? temperature)
    {
        if (Source.Triggers is { Length: > 0 } triggers ? Array.IndexOf(triggers, trigger) < 0 : IsOneShot(trigger))
        {
            return false;
        }

        float value = temperature ?? RuleRegistry.RoomTemperature;
        return (Source.MinTemperature is not float min || value >= min) && (Source.MaxTemperature is not float max || value <= max);
    }

    /// <param name="liquidColor"> Multiplied with the rule's tint, for <c>liquidTint</c>. </param>
    public void AddMatches(float? temperature, Vec4f? liquidColor, List<ItemParticleMatch> matches)
    {
        float intensity = Source.Intensity;

        if (Source.IntensityByTemperature is { } curve)
        {
            intensity *= curve.Evaluate(temperature ?? RuleRegistry.RoomTemperature);
        }

        var color = liquidColor == null ? tint : tint == null ? liquidColor : new Vec4f(tint.X * liquidColor.X, tint.Y * liquidColor.Y, tint.Z * liquidColor.Z, tint.W * liquidColor.W);

        foreach (var effect in effects)
        {
            matches.Add(new ItemParticleMatch(effect)
            {
                Intensity = intensity,
                Scale = Source.Scale,
                OriginX = originX,
                OriginY = originY,
                Tint = color,
                Source = Source.Code
            });
        }
    }
}
