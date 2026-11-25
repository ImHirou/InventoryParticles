using System;
using System.IO;
using System.Linq;
using InventoryParticles.Api;
using InventoryParticles.Config;
using InventoryParticles.Gui;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.Server;

namespace InventoryParticles;

/// <summary>
/// Client-side entry point. Draws particles around the hovered stack, the stack held on the cursor and, in the hotbar,
/// the stacks in the player's hands, and exposes <see cref="Api"/> to other mods.
/// </summary>
public sealed class InventoryParticlesModSystem : ModSystem
{
    public const string Domain = "inventoryparticles";
    public const string BrokenConfigFileName = InventoryParticlesConfig.FileName + ".broken";

    private const float DefaultPreviewSeconds = 3f;

    private readonly InventoryParticlesApi particles = new();
    private ICoreClientAPI? capi;
    private ParticleHudLayer? layer;
    private TopLayerRenderer? topRenderer;
    private long reopenListenerId;

    /// <summary> The particle API. Registration works from StartClientSide on, playing once the world is loaded. </summary>
    public IInventoryParticlesApi Api => particles;

    internal InventoryParticlesConfig Config { get; private set; } = new();

    // Before dependent mods, so they find the API ready in their own StartClientSide.
    public override double ExecuteOrder() => 0.1;

    public override bool ShouldLoad(EnumAppSide forSide) => forSide == EnumAppSide.Client;

    public override void StartClientSide(ICoreClientAPI api)
    {
        capi = api;

        if (!TryLoadConfig(out var config))
        {
            api.Event.LevelFinalize += () => api.ShowChatMessage(Lang.Get($"{Domain}:config-load-failed", InventoryParticlesConfig.FileName, BrokenConfigFileName));
        }

        Config = config;
        particles.Attach(api, Mod.Logger, config);
        api.Event.LevelFinalize += OnLevelFinalize;
        api.Event.InGameError += particles.OnInGameError;
        RegisterCommands(api);
    }

    private void OnLevelFinalize()
    {
        if (capi == null)
        {
            return;
        }

        layer = new ParticleHudLayer(capi, particles.OnHudFrame);
        particles.OnWorldReady(layer);
        layer.TryOpen(false);

        topRenderer = new TopLayerRenderer(particles.OnTopFrame);
        capi.Event.RegisterRenderer(topRenderer, EnumRenderStage.Ortho, Domain + "-top");

        // Nothing in vanilla closes HUDs, but other mods might.
        reopenListenerId = capi.Event.RegisterGameTickListener(_ =>
        {
            if (layer != null && !layer.IsOpened())
            {
                layer.TryOpen(false);
            }
        }, 1000);
    }

    public override void Dispose()
    {
        if (capi != null)
        {
            capi.Event.LevelFinalize -= OnLevelFinalize;
            capi.Event.InGameError -= particles.OnInGameError;

            if (topRenderer != null)
            {
                capi.Event.UnregisterRenderer(topRenderer, EnumRenderStage.Ortho);
            }

            if (reopenListenerId != 0)
            {
                capi.Event.UnregisterGameTickListener(reopenListenerId);
            }
        }

        layer?.TryClose();
        layer?.Dispose();
        layer = null;
        topRenderer = null;
        reopenListenerId = 0;
        particles.Detach();
        capi = null;
        base.Dispose();
    }

    #region Config

    /// <summary>
    /// Reads the config file. If it cannot be read, a copy is kept as <see cref="BrokenConfigFileName"/>
    /// and false is returned along with the default settings.
    /// </summary>
    private bool TryLoadConfig(out InventoryParticlesConfig config)
    {
        InventoryParticlesConfig? loaded;

        try
        {
            loaded = capi!.LoadModConfig<InventoryParticlesConfig>(InventoryParticlesConfig.FileName);
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Failed to read ModConfig/{0}, a copy is kept as {1}: {2}", InventoryParticlesConfig.FileName, BrokenConfigFileName, e.Message);
            // Saving the settings later overwrites the file, so keep the user's edits around.
            BackupConfigFile(BrokenConfigFileName);
            config = new InventoryParticlesConfig();
            return false;
        }

        loaded ??= new InventoryParticlesConfig();
        loaded.Sanitize();
        loaded.ConfigVersion = InventoryParticlesConfig.CurrentVersion;
        // Writes back newly added settings.
        SaveConfig(loaded);
        config = loaded;
        return true;
    }

    private void BackupConfigFile(string backupFileName)
    {
        try
        {
            string path = Path.Combine(GamePaths.ModConfig, InventoryParticlesConfig.FileName);

            if (File.Exists(path))
            {
                File.Copy(path, Path.Combine(GamePaths.ModConfig, backupFileName), overwrite: true);
            }
        }
        catch (Exception e)
        {
            Mod.Logger.Warning("Failed to copy ModConfig/{0} to {1}: {2}", InventoryParticlesConfig.FileName, backupFileName, e.Message);
        }
    }

    private void SaveConfig(InventoryParticlesConfig config)
    {
        try
        {
            capi!.StoreModConfig(config, InventoryParticlesConfig.FileName);
        }
        catch (Exception e)
        {
            Mod.Logger.Error("Failed to save ModConfig/{0}: {1}", InventoryParticlesConfig.FileName, e);
        }
    }

    /// <summary> Reads the config file and the JSON effects and rules again. False if the config file is broken. </summary>
    private bool Reload()
    {
        bool configLoaded = TryLoadConfig(out var config);

        if (configLoaded)
        {
            Config = config;
            particles.ApplyConfig(config);
        }

        particles.ReloadAssets();
        return configLoaded;
    }

    #endregion

    #region Commands

    private void RegisterCommands(ICoreClientAPI api)
    {
        var parsers = api.ChatCommands.Parsers;

        api.ChatCommands.Create(Domain)
            .WithDescription(Lang.Get($"{Domain}:command-description"))
            .RequiresPrivilege(Privilege.chat)
            .BeginSubCommand("reload")
                .WithDescription(Lang.Get($"{Domain}:command-reload-description"))
                .HandleWith(_ => Reload()
                    ? TextCommandResult.Success(Lang.Get($"{Domain}:command-reload-done", particles.Effects.Codes.Count, particles.Rules.Rules.Count))
                    : TextCommandResult.Error(Lang.Get($"{Domain}:command-reload-failed", InventoryParticlesConfig.FileName, BrokenConfigFileName)))
            .EndSubCommand()
            .BeginSubCommand("list")
                .WithDescription(Lang.Get($"{Domain}:command-list-description"))
                .HandleWith(_ => TextCommandResult.Success(Lang.Get($"{Domain}:command-list-done",
                    string.Join(", ", particles.EffectCodes.Select(code => code.ToString())),
                    string.Join(", ", particles.Rules.Rules.Select(rule => rule.Source.Code)))))
            .EndSubCommand()
            .BeginSubCommand("play")
                .WithDescription(Lang.Get($"{Domain}:command-play-description"))
                .WithArgs(parsers.Word("effect"), parsers.OptionalFloat("seconds", DefaultPreviewSeconds))
                .HandleWith(args => PlayPreview((string)args[0], (float)args[1]))
            .EndSubCommand()
            .BeginSubCommand("debug")
                .WithDescription(Lang.Get($"{Domain}:command-debug-description"))
                .HandleWith(_ =>
                {
                    particles.Debug = !particles.Debug;
                    return TextCommandResult.Success(Lang.Get(particles.Debug ? $"{Domain}:command-debug-on" : $"{Domain}:command-debug-off"));
                })
            .EndSubCommand()
            .BeginSubCommand("heat")
                .WithDescription(Lang.Get($"{Domain}:command-heat-description"))
                .WithArgs(parsers.Float("temperature"))
                .HandleWith(args => Heat((float)args[0]))
            .EndSubCommand();
    }

    /// <summary> Plays an effect at the mouse cursor, for tuning effect files. </summary>
    private TextCommandResult PlayPreview(string code, float seconds)
    {
        if (capi == null || !particles.IsReady)
        {
            return TextCommandResult.Error(Lang.Get($"{Domain}:command-not-ready"));
        }

        var effect = AssetLocation.Create(code, Domain);
        var input = capi.Input;
        var handle = particles.Play(effect, ParticleAnchor.Tracked(() => new GuiItemRect(input.MouseX, input.MouseY, 0f)), new PlayOptions
        {
            Duration = seconds > 0f ? seconds : DefaultPreviewSeconds,
            Layer = EnumParticleLayer.Top,
            FadeWhenHidden = false
        });

        return handle.IsValid
            ? TextCommandResult.Success(Lang.Get($"{Domain}:command-play-done", effect))
            : TextCommandResult.Error(Lang.Get($"{Domain}:command-play-unknown", effect));
    }

    /// <summary> Sets the temperature of the stack on the cursor (or in hand) on this client only, to test hot item effects. </summary>
    private TextCommandResult Heat(float temperature)
    {
        var inventory = capi?.World?.Player?.InventoryManager;
        var slot = inventory?.MouseItemSlot?.Itemstack != null ? inventory.MouseItemSlot : inventory?.ActiveHotbarSlot;
        var stack = slot?.Itemstack;

        if (stack?.Collectible == null || capi == null)
        {
            return TextCommandResult.Error(Lang.Get($"{Domain}:command-heat-nostack"));
        }

        stack.Collectible.SetTemperature(capi.World, stack, temperature, false);
        return TextCommandResult.Success(Lang.Get($"{Domain}:command-heat-done", stack.GetName(), temperature));
    }

    #endregion
}
