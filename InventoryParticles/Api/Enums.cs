namespace InventoryParticles.Api;

/// <summary> Why item particles are shown. Rules can be limited to some triggers. </summary>
public enum EnumParticleTrigger
{
    /// <summary> The stack is in the slot under the mouse cursor. </summary>
    Hover,
    /// <summary> The stack is held on the mouse cursor. </summary>
    Cursor,
    /// <summary> Another mod asked for the item's particles with <see cref="IInventoryParticlesApi.PlayItem"/>. </summary>
    Custom,
    /// <summary> The stack is in one of the player's hands (the active hotbar slot or the off-hand slot) and shown in the hotbar. </summary>
    Held,
    /// <summary> One-shot: the stack was just taken onto the mouse cursor. Its effects play once, as bursts. </summary>
    Pickup,
    /// <summary>
    /// One-shot: the game made the player drop the stack because it is too hot to hold without tongs.
    /// Its effects play once, as bursts, where the stack was last seen.
    /// </summary>
    TooHot
}

/// <summary> Where particles are drawn relative to the rest of the GUI. </summary>
public enum EnumParticleLayer
{
    /// <summary> Above inventories and HUDs, below the escape menu, item tooltips and the stack held on the cursor. </summary>
    BelowCursor,
    /// <summary> Above the whole GUI, including tooltips and the stack held on the cursor. </summary>
    Top,
    /// <summary>
    /// Right above the item the effect belongs to: <see cref="BelowCursor"/>, except for effects played at
    /// <see cref="ParticleAnchor.Cursor"/>, which go above that stack (<see cref="Top"/>). For glints and flames on the item itself.
    /// </summary>
    AboveItem
}
