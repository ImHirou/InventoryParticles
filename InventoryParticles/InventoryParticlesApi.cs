using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using InventoryParticles.Api;
using InventoryParticles.Config;
using InventoryParticles.Definitions;
using InventoryParticles.Gui;
using InventoryParticles.Particles;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;

namespace InventoryParticles;

/// <summary> Implements the public API and runs everything each frame. </summary>
internal sealed class InventoryParticlesApi : IInventoryParticlesApi
{
    /// <summary> The code of the error <c>InventoryBase.DropSlotIfHot</c> sends with "Requires tongs to hold". </summary>
    internal const string TooHotErrorCode = "requiretongs";

    private const float MaxFrameTime = 0.1f;

    /// <summary> How long an effect an item left behind waits for the tracker the item moved to. </summary>
    private const double HandOverSeconds = 0.15;

    private readonly EffectRegistry effects = new();
    private readonly RuleRegistry rules = new();
    private readonly List<IItemParticleProvider> providers = new();
    private readonly Dictionary<long, IPlayback> playbacks = new();
    private readonly List<long> deadPlaybacks = new();
    private readonly List<ItemTracker> itemTrackers = new();
    private readonly HashSet<AssetLocation> disabledEffects = new();
    private readonly HashSet<AssetLocation> reportedEffects = new();
    private readonly ParticleRenderer renderer = new();
    private readonly List<ItemParticleMatch> burstMatches = new();
    private readonly List<LeftEffect> leftEffects = new();
    private ItemParticlesEventArgs? eventArgs;
    private long nextId = 1;

    private ICoreClientAPI? capi;
    private ILogger? logger;
    private ParticleSystem? system;
    private ParticleTextures? textures;
    private SlotLocator? locator;
    private ItemParticleDriver? driver;
    private StackInspector? inspector;
    private InventoryParticlesConfig config = new();
    private bool worldReady;
    private bool frameErrorLogged;
    private bool providerErrorLogged;
    private bool eventErrorLogged;
    private bool tooManyEffectsLogged;

    public event ItemParticlesDelegate? OnItemParticles;

    public bool IsReady => worldReady && system != null;

    /// <summary> Draw anchor outlines and report picked effects in chat. </summary>
    internal bool Debug { get; set; }

    internal EffectRegistry Effects => effects;

    internal RuleRegistry Rules => rules;

    public IReadOnlyCollection<AssetLocation> EffectCodes => effects.Codes;

    public GuiItemRect? HoveredRect => driver?.HoveredRect;

    public GuiItemRect? CursorRect => driver?.CursorRect;

    public int ParticleCount => system?.Count ?? 0;

    /// <summary> Number of running effects, for tests. </summary>
    internal int EffectCount => system?.Effects.Count ?? 0;

    /// <summary> Screen positions of the live particles of an effect, for tests. </summary>
    internal List<(float X, float Y)> PositionsOf(AssetLocation effect)
    {
        var positions = new List<(float X, float Y)>();

        if (system != null)
        {
            foreach (ref readonly var particle in system.Particles)
            {
                if (particle.Owner.Code == effect)
                {
                    positions.Add(particle.Relative ? (particle.X + particle.Owner.AnchorX, particle.Y + particle.Owner.AnchorY) : (particle.X, particle.Y));
                }
            }
        }

        return positions;
    }

    /// <summary> Number of live particles of an effect, for tests. </summary>
    internal int CountParticlesOf(AssetLocation effect)
    {
        int count = 0;

        if (system != null)
        {
            foreach (ref readonly var particle in system.Particles)
            {
                if (particle.Owner.Code == effect)
                {
                    count++;
                }
            }
        }

        return count;
    }

    #region Lifecycle

    internal void Attach(ICoreClientAPI api, ILogger log, InventoryParticlesConfig settings)
    {
        capi = api;
        logger = log;
        system = new ParticleSystem(settings.MaxParticles);
        textures = new ParticleTextures(api, log);
        system.TextureKey = textures.GetKey;
        locator = new SlotLocator(api, log);
        driver = new ItemParticleDriver(this, api);
        inspector = new StackInspector(api);
        rules.Attach(name => api.ClassRegistry.GetBlockClass(name) ?? api.ClassRegistry.GetItemClass(name), log, inspector);
        ApplyConfig(settings);
    }

    /// <summary> Called once the world is loaded. </summary>
    internal void OnWorldReady(GuiDialog layer)
    {
        locator?.Ignore(layer);
        ReloadAssets();
        worldReady = true;
    }

    internal void ApplyConfig(InventoryParticlesConfig settings)
    {
        config = settings;
        rules.SetDisabled(settings.DisabledRules);
        disabledEffects.Clear();

        foreach (string code in settings.DisabledEffects)
        {
            disabledEffects.Add(AssetLocation.Create(code, InventoryParticlesModSystem.Domain));
        }

        ResetItemEffects();
    }

    /// <summary> Reads the JSON effects and rules of every domain again and restarts the item effects. </summary>
    internal void ReloadAssets()
    {
        if (capi == null || logger == null)
        {
            return;
        }

        effects.LoadAssets(capi.Assets, logger);
        rules.LoadAssets(capi.Assets, logger);
        inspector?.Clear();
        reportedEffects.Clear();
        ResetItemEffects();
        logger.Notification("Loaded {0} particle effects and {1} particle rules from assets ({2} effects and {3} rules from code).",
            effects.AssetCount, rules.AssetCount, effects.CodeCount, rules.CodeCount);
    }

    internal void Detach()
    {
        worldReady = false;
        ResetItemEffects();

        foreach (var playback in playbacks.Values)
        {
            playback.Stop(false);
        }

        playbacks.Clear();
        itemTrackers.Clear();
        system?.Clear();
        textures?.Dispose();
        textures = null;
        system = null;
        locator = null;
        driver = null;
        inspector = null;
        capi = null;
    }

    #endregion

    #region Frame

    /// <summary> Called by the HUD layer: updates everything, then draws the layer below the cursor. </summary>
    internal void OnHudFrame(float deltaTime)
    {
        if (capi?.World?.Player == null || system == null || textures == null || locator == null || driver == null)
        {
            return;
        }

        try
        {
            float step = Math.Clamp(deltaTime, 0f, MaxFrameTime);
            system.SpawningEnabled = config.Enabled;
            system.DensityMultiplier = config.Density;
            system.SizeMultiplier = config.Size;
            system.SetCapacity(config.MaxParticles);
            // Before the first frame the size may still be unknown.
            int width = capi.Render.FrameWidth;
            int height = capi.Render.FrameHeight;
            system.ScreenWidth = width > 0 ? width : float.PositiveInfinity;
            system.ScreenHeight = height > 0 ? height : float.PositiveInfinity;

            locator.BeginFrame();
            double time = system.Time + step;
            driver.Update(time, config, locator);
            UpdateItemTrackers(time);
            ExpireLeftEffects();
            system.Update(step, locator);
            RemoveDeadPlaybacks();

            renderer.Render(capi.Render, system, textures, EnumParticleLayer.BelowCursor);
        }
        catch (Exception e)
        {
            ReportFrameError(e);
        }
    }

    /// <summary> Called after the whole GUI: draws the top layer and the debug overlay. </summary>
    internal void OnTopFrame()
    {
        if (capi?.World?.Player == null || system == null || textures == null)
        {
            return;
        }

        try
        {
            renderer.Render(capi.Render, system, textures, EnumParticleLayer.Top);

            if (Debug)
            {
                DrawDebugOverlay(capi.Render, system);
            }
        }
        catch (Exception e)
        {
            ReportFrameError(e);
        }
    }

    private void UpdateItemTrackers(double time)
    {
        for (int i = itemTrackers.Count - 1; i >= 0; i--)
        {
            var tracker = itemTrackers[i];
            tracker.Update(tracker.FixedStack, time);

            if (!tracker.Alive)
            {
                itemTrackers.RemoveAt(i);
            }
        }
    }

    private void RemoveDeadPlaybacks()
    {
        foreach (var (id, playback) in playbacks)
        {
            if (!playback.Alive)
            {
                deadPlaybacks.Add(id);
            }
        }

        foreach (long id in deadPlaybacks)
        {
            playbacks.Remove(id);
        }

        deadPlaybacks.Clear();
    }

    private void ReportFrameError(Exception e)
    {
        if (frameErrorLogged)
        {
            return;
        }

        frameErrorLogged = true;
        logger?.Error("Inventory particles failed to render a frame; further errors are not logged: {0}", e);
    }

    private void DrawDebugOverlay(IRenderAPI render, ParticleSystem particles)
    {
        int anchorColor = ColorUtil.ToRgba(255, 255, 220, 60);
        int originColor = ColorUtil.ToRgba(255, 255, 60, 60);
        render.GLDisableDepthTest();

        try
        {
            foreach (var effect in particles.Effects)
            {
                if (!effect.AnchorVisible)
                {
                    continue;
                }

                float size = effect.AnchorSize;
                render.RenderRectangle(effect.AnchorX - size / 2f, effect.AnchorY - size / 2f, 60f, size, size, anchorColor);

                foreach (var emitter in effect.Effect.Emitters ?? Array.Empty<GuiEmitterProps>())
                {
                    var origin = emitter?.Origin is { Length: >= 2 } value ? value : new[] { 0f, 0f };
                    float x = effect.AnchorX + (origin[0] + effect.OriginX) * size;
                    float y = effect.AnchorY - (origin[1] + effect.OriginY) * size;
                    render.RenderRectangle(x - 1.5f, y - 1.5f, 60f, 3f, 3f, originColor);
                }
            }
        }
        finally
        {
            render.GLEnableDepthTest();
        }
    }

    #endregion

    #region Item particles

    /// <summary> Fills <paramref name="matches"/> from providers, rules and the event. Returns the stack's temperature. </summary>
    internal float? CollectItemParticles(ItemStack stack, ItemSlot? slot, EnumParticleTrigger trigger, List<ItemParticleMatch> matches)
    {
        matches.Clear();
        float? temperature = GetTemperature(stack);
        var query = new ItemParticleQuery(stack, slot, trigger, temperature);
        bool handled = false;

        foreach (var provider in providers)
        {
            try
            {
                if (provider.Collect(in query, matches))
                {
                    handled = true;
                    break;
                }
            }
            catch (Exception e)
            {
                if (!providerErrorLogged)
                {
                    providerErrorLogged = true;
                    logger?.Error("Particle provider {0} threw an exception (logged once): {1}", provider.GetType().FullName ?? "?", e);
                }
            }
        }

        if (!handled)
        {
            rules.Collect(stack, slot, trigger, temperature, matches);
        }

        RaiseItemParticles(query, matches);

        for (int i = matches.Count - 1; i >= 0; i--)
        {
            if (matches[i]?.Effect == null || disabledEffects.Contains(matches[i].Effect))
            {
                matches.RemoveAt(i);
            }
        }

        return temperature;
    }

    private void RaiseItemParticles(ItemParticleQuery query, List<ItemParticleMatch> matches)
    {
        var handlers = OnItemParticles;

        if (handlers == null)
        {
            return;
        }

        var args = eventArgs ??= new ItemParticlesEventArgs(query, matches);
        args.Query = query;
        args.Matches = matches;
        args.Cancel = false;

        foreach (var handler in handlers.GetInvocationList())
        {
            try
            {
                ((ItemParticlesDelegate)handler)(args);
            }
            catch (Exception e)
            {
                if (!eventErrorLogged)
                {
                    eventErrorLogged = true;
                    logger?.Error("An OnItemParticles handler threw an exception (logged once): {0}", e);
                }
            }
        }

        if (args.Cancel)
        {
            matches.Clear();
        }
    }

    private float? GetTemperature(ItemStack stack)
    {
        var collectible = stack.Collectible;
        var world = capi?.World;

        if (collectible == null || world == null)
        {
            return null;
        }

        try
        {
            return collectible.HasTemperature(stack) ? collectible.GetTemperature(world, stack) : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    private void ResetItemEffects()
    {
        leftEffects.Clear();
        driver?.Reset();
    }

    /// <summary>
    /// A tracker let go of a running effect because its item left (a slot emptied, the cursor emptied) or another tracker may show the
    /// same slot now. The tracker the item arrives at this frame takes the effect over in <see cref="StartItemEffect"/>.
    /// </summary>
    internal void LeaveEffect(EffectInstance instance, CollectibleObject? collectible, ItemSlot? slot, bool fromCursor)
    {
        // Only items whose place is known can be followed.
        if (system != null && collectible != null && !instance.Alive && (slot != null || fromCursor))
        {
            leftEffects.Add(new LeftEffect(instance, collectible, slot, fromCursor, system.Time));
        }
    }

    /// <summary>
    /// Starts an effect for an item's tracker. If the same item just left another tracker with this effect running (it was picked up,
    /// put down or moved into a hand), that effect continues here instead, so its particles follow the item.
    /// </summary>
    internal EffectInstance? StartItemEffect(AssetLocation code, ParticleAnchor anchor, ItemStack stack, ItemSlot? slot)
    {
        for (int i = leftEffects.Count - 1; i >= 0; i--)
        {
            var left = leftEffects[i];

            if (left.Instance.Code != code || !ReferenceEquals(left.Collectible, stack.Collectible))
            {
                continue;
            }

            // The same item, not another one of its kind: it came from the cursor, from a slot that no longer holds it, or it stayed in the same slot.
            if (!left.FromCursor && !ReferenceEquals(left.Slot, slot) && !left.LeftItsSlot)
            {
                continue;
            }

            leftEffects.RemoveAt(i);

            if (system!.Revive(left.Instance, anchor))
            {
                return left.Instance;
            }
        }

        return StartEffect(code, anchor);
    }

    /// <summary> Effects nobody took over stay stopped. If their item is gone, the particles attached to it go too. </summary>
    private void ExpireLeftEffects()
    {
        for (int i = leftEffects.Count - 1; i >= 0; i--)
        {
            var left = leftEffects[i];

            if (system!.Time - left.Time < HandOverSeconds)
            {
                continue;
            }

            if (left.FromCursor || left.LeftItsSlot)
            {
                system.FadeAttachedParticles(left.Instance);
            }

            leftEffects.RemoveAt(i);
        }
    }

    private readonly record struct LeftEffect(EffectInstance Instance, CollectibleObject Collectible, ItemSlot? Slot, bool FromCursor, double Time)
    {
        /// <summary> The slot is empty now or holds something else (the item was picked up, moved or swapped). </summary>
        public bool LeftItsSlot => Slot != null && !ReferenceEquals(Slot.Itemstack?.Collectible, Collectible);
    }

    /// <summary> Starts an effect, or returns null if it is unknown, disabled or the system is full. </summary>
    internal EffectInstance? StartEffect(AssetLocation code, ParticleAnchor anchor)
    {
        if (system == null || disabledEffects.Contains(code))
        {
            return null;
        }

        if (!effects.TryGet(code, out var effect))
        {
            if (reportedEffects.Add(code))
            {
                logger?.Warning("Unknown particle effect {0}.", code);
            }

            return null;
        }

        var instance = system.Start(code, effect, anchor);

        if (instance == null && !tooManyEffectsLogged)
        {
            tooManyEffectsLogged = true;
            logger?.Warning("More than {0} particle effects are running at once; are some never stopped? Not starting {1}.", ParticleSystem.MaxEffects, code);
        }

        return instance;
    }

    /// <summary>
    /// Plays what the rules pick for a one-shot trigger (<see cref="EnumParticleTrigger.Pickup"/>, <see cref="EnumParticleTrigger.TooHot"/>)
    /// once: every emitter spawns its <c>burst</c>, scaled by the intensity. Returns the number of effects started.
    /// </summary>
    internal int PlayItemBurst(ItemStack stack, ItemSlot? slot, EnumParticleTrigger trigger, ParticleAnchor anchor)
    {
        if (!IsReady)
        {
            return 0;
        }

        float? temperature = CollectItemParticles(stack, slot, trigger, burstMatches);
        int started = 0;

        foreach (var match in burstMatches)
        {
            if (StartEffect(match.Effect, anchor) is not { } instance)
            {
                continue;
            }

            instance.Intensity = Math.Max(0f, match.Intensity);
            instance.Scale = match.Scale;
            instance.OriginX = match.OriginX;
            instance.OriginY = match.OriginY;
            instance.Temperature = temperature;
            instance.SetTint(match.Tint);
            instance.BurstOnly = true;
            // Give up if the anchor doesn't show up soon.
            instance.EndTime = Math.Min(instance.EndTime, system!.Time + 1.0);
            started++;
        }

        if (Debug && burstMatches.Count > 0)
        {
            ShowDebug(trigger, stack, temperature, burstMatches);
        }

        return started;
    }

    /// <summary> Handler of <c>capi.Event.InGameError</c>: the server makes players drop stacks that are too hot to hold without tongs. </summary>
    internal void OnInGameError(object? sender, string? errorCode, string? text)
    {
        if (errorCode == TooHotErrorCode && system != null)
        {
            driver?.OnTooHot(system.Time);
        }
    }

    /// <summary> In debug mode, tells the player in chat which effects the hovered, cursor and hand stacks show whenever that changes. </summary>
    internal void ReportItemParticles(ItemTracker tracker, ItemStack stack, float? temperature, List<ItemParticleMatch> matches)
    {
        if (!Debug || capi == null)
        {
            return;
        }

        string summary = Summary(tracker.Trigger, stack, matches);

        if (summary == tracker.DebugSummary)
        {
            return;
        }

        tracker.DebugSummary = summary;
        ShowDebug(summary, temperature);
    }

    private void ShowDebug(EnumParticleTrigger trigger, ItemStack stack, float? temperature, List<ItemParticleMatch> matches) =>
        ShowDebug(Summary(trigger, stack, matches), temperature);

    private void ShowDebug(string summary, float? temperature)
    {
        string temperatureText = temperature is float value ? $", {value:0}°C" : "";
        capi?.ShowChatMessage($"[{InventoryParticlesModSystem.Domain}] {summary}{temperatureText}");
    }

    private static string Summary(EnumParticleTrigger trigger, ItemStack stack, List<ItemParticleMatch> matches)
    {
        string effectList = matches.Count == 0
            ? "-"
            : string.Join(", ", matches.Select(match => $"{match.Effect} ({match.Source ?? "?"})"));
        return $"{trigger.ToString().ToLowerInvariant()} {stack.Collectible?.Code}: {effectList}";
    }

    #endregion

    #region IInventoryParticlesApi

    public void RegisterEffect(AssetLocation code, GuiParticleEffect effect) => effects.Register(code, effect, logger);

    public bool TryGetEffect(AssetLocation code, [NotNullWhen(true)] out GuiParticleEffect? effect) => effects.TryGet(code, out effect);

    public void RegisterRule(ItemParticleRule rule)
    {
        rules.Register(rule);
        ResetItemEffects();
    }

    public void RegisterProvider(IItemParticleProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        providers.Add(provider);
        ResetItemEffects();
    }

    public ParticleHandle Play(AssetLocation effect, ParticleAnchor anchor, PlayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(anchor);

        if (!IsReady || StartEffect(effect, anchor) is not { } instance)
        {
            return ParticleHandle.Invalid;
        }

        ApplyOptions(instance, options);
        return Track(instance);
    }

    public ParticleHandle PlayItem(ItemStack stack, ParticleAnchor anchor, PlayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        ArgumentNullException.ThrowIfNull(anchor);

        if (!IsReady)
        {
            return ParticleHandle.Invalid;
        }

        var tracker = new ItemTracker(this, anchor, options?.Trigger ?? EnumParticleTrigger.Custom, null, stack, options);

        if (options?.Duration is float duration && duration > 0f)
        {
            tracker.EndTime = system!.Time + duration;
        }

        itemTrackers.Add(tracker);
        return Track(tracker);
    }

    public bool Burst(AssetLocation effect, ParticleAnchor anchor, int count, PlayOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(effect);
        ArgumentNullException.ThrowIfNull(anchor);

        if (!IsReady || StartEffect(effect, anchor) is not { } instance)
        {
            return false;
        }

        ApplyOptions(instance, options);
        instance.BurstOverride = Math.Max(0, count);
        instance.BurstOnly = true;
        // Give up if the anchor doesn't show up soon.
        instance.EndTime = Math.Min(instance.EndTime, system!.Time + 1.0);
        return true;
    }

    public bool Stop(ParticleHandle handle, bool clearParticles = false)
    {
        if (!playbacks.Remove(handle.Id, out var playback))
        {
            return false;
        }

        playback.Stop(clearParticles);
        return true;
    }

    public bool IsAlive(ParticleHandle handle) => playbacks.TryGetValue(handle.Id, out var playback) && playback.Alive;

    public bool SetIntensity(ParticleHandle handle, float intensity)
    {
        if (!playbacks.TryGetValue(handle.Id, out var playback) || !playback.Alive)
        {
            return false;
        }

        playback.SetIntensity(intensity);
        return true;
    }

    public bool SetAnchor(ParticleHandle handle, ParticleAnchor anchor)
    {
        ArgumentNullException.ThrowIfNull(anchor);

        if (!playbacks.TryGetValue(handle.Id, out var playback) || !playback.Alive)
        {
            return false;
        }

        playback.SetAnchor(anchor);
        return true;
    }

    public bool TryGetSlotRect(ItemSlot slot, out GuiItemRect rect)
    {
        rect = default;
        return slot != null && locator != null && locator.TryGetSlotRect(slot, out rect);
    }

    public List<ItemParticleMatch> GetItemParticles(ItemStack stack, EnumParticleTrigger trigger, ItemSlot? slot = null)
    {
        ArgumentNullException.ThrowIfNull(stack);
        var matches = new List<ItemParticleMatch>();

        if (capi != null)
        {
            CollectItemParticles(stack, slot, trigger, matches);
        }

        return matches;
    }

    private void ApplyOptions(EffectInstance instance, PlayOptions? options)
    {
        if (options == null)
        {
            return;
        }

        instance.SetIntensity(options.Intensity);
        instance.Scale = float.IsFinite(options.Scale) ? Math.Max(0f, options.Scale) : 1f;
        instance.Temperature = options.Temperature;
        instance.OriginX = options.OriginX;
        instance.OriginY = options.OriginY;
        instance.Layer = options.Layer;
        instance.FadeWhenHidden = options.FadeWhenHidden;
        instance.SetTint(options.Tint);

        if (options.Duration is float duration && duration > 0f)
        {
            instance.EndTime = system!.Time + duration;
        }
    }

    private ParticleHandle Track(IPlayback playback)
    {
        long id = nextId++;
        playbacks[id] = playback;
        return new ParticleHandle(id);
    }

    #endregion
}
