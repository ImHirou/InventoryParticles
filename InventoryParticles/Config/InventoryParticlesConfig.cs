using System;
using System.Collections.Generic;
using System.Linq;

namespace InventoryParticles.Config;

/// <summary> Client settings, stored in <c>ModConfig/inventoryparticles.json</c>. </summary>
public sealed class InventoryParticlesConfig
{
    public const string FileName = "inventoryparticles.json";
    public const int CurrentVersion = 1;

    public int ConfigVersion = CurrentVersion;

    /// <summary> Turns all particles of this mod on or off, including those played by other mods. </summary>
    public bool Enabled = true;

    /// <summary> Show item particles for the stack under the mouse. </summary>
    public bool ShowOnHover = true;

    /// <summary> Show item particles for the stack held on the cursor. </summary>
    public bool ShowOnCursor = true;

    /// <summary> Show item particles in the hotbar for the stacks in the player's hands. </summary>
    public bool ShowInHotbar = true;

    /// <summary> Multiplies how many particles are spawned. </summary>
    public float Density = 1f;

    /// <summary> Multiplies the particle sizes. </summary>
    public float Size = 1f;

    /// <summary> Upper limit of live particles. </summary>
    public int MaxParticles = 512;

    /// <summary> Codes of rules to ignore, e.g. <c>hotmetal</c>. </summary>
    public List<string> DisabledRules = new();

    /// <summary> Codes of effects to never play, e.g. <c>inventoryparticles:steam</c> (the domain defaults to inventoryparticles). </summary>
    public List<string> DisabledEffects = new();

    internal void Sanitize()
    {
        var defaults = new InventoryParticlesConfig();
        Density = Sanitize(Density, defaults.Density, 0f, 10f);
        Size = Sanitize(Size, defaults.Size, 0.1f, 10f);
        MaxParticles = Math.Clamp(MaxParticles, 16, 8192);
        DisabledRules = Clean(DisabledRules);
        DisabledEffects = Clean(DisabledEffects);
    }

    private static float Sanitize(float value, float fallback, float min, float max) =>
        float.IsFinite(value) ? Math.Clamp(value, min, max) : fallback;

    private static List<string> Clean(List<string>? values) =>
        (values ?? new List<string>())
            .Where(value => !string.IsNullOrWhiteSpace(value))
            .Select(value => value.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
}
