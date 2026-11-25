using System.Reflection;
using InventoryParticles;
using InventoryParticles.Api;
using InventoryParticles.Config;
using InventoryParticles.Gui;
using Newtonsoft.Json;
using Vintagestory.API.Client;
using Vintagestory.API.Common;
using Vintagestory.API.Config;
using Vintagestory.API.MathTools;

/// <summary>
/// Runs whole frames of the mod against real slot grids and composers with a faked client API, without a window:
/// asset loading, hovered and held stacks, layers, GL state, handles, providers and the event.
/// </summary>
internal static class EngineTests
{
    private const float Frame = 1f / 60f;
    private const string Domain = "inventoryparticles";

    public static void Run(string assets)
    {
        RuntimeEnv.GUIScale = 1;
        var env = new FakeEnvironment(assets);
        var particles = new InventoryParticlesApi();
        var log = new Tests.TestLogger();
        particles.Attach(env.Api, log, new InventoryParticlesConfig());
        var layer = new ParticleHudLayer(env.Api, particles.OnHudFrame);
        particles.OnWorldReady(layer);

        Check(particles.IsReady, "The API is ready once the world is loaded");
        Check(particles.EffectCodes.Count == Tests.BundledEffects && particles.Rules.Rules.Count == Tests.BundledRules, $"Assets load through IAsset.ToObject: {particles.EffectCodes.Count} effects, {particles.Rules.Rules.Count} rules");
        Check(log.Messages.Count == 0, "Asset loading logs nothing: " + string.Join(" | ", log.Messages));
        Check(layer.DrawOrder is > 0.2 and < 0.89 && !layer.Focusable && !layer.ShouldReceiveMouseEvents(), "The layer sits between inventories and the escape menu and never takes focus or mouse input");

        // The hovered temporal gear in slot 1 (x 100..148): items are drawn 3 px left and 1 px up of the slot centre.
        var gear = env.Slots[1];
        env.Hover(1);
        Frames(particles, 1f);
        Check(particles.HoveredRect is { } hovered && Near(hovered.CenterX, 121) && Near(hovered.CenterY, 23) && Near(hovered.Size, 48), $"Hovered rect is the item centre with the slot size as unit, got {particles.HoveredRect}");
        Check(env.Draws.Count > 0 && particles.ParticleCount > 0, "The hovered temporal gear draws particles");
        Check(env.Draws.Max(draw => Distance(draw, 121, 23)) is > 20 and < MaxSpread, $"Hover particles surround the hovered item and reach a little past its slot, up to {env.Draws.Max(draw => Distance(draw, 121, 23)):0} px");
        Check(env.BlendModes.Contains(EnumBlendMode.Glow) && env.BlendModes[^1] == EnumBlendMode.Standard, "Glow particles use additive blending and the standard blend is restored");
        Check(env.DepthDisabled == env.DepthEnabled && env.DepthDisabled > 0, "The depth test is restored after every layer");

        // Leaving the slot lets the particles finish their life instead of popping.
        env.Unhover();
        int afterLeaving = particles.ParticleCount;
        Frames(particles, 0.3f);
        Check(particles.ParticleCount > 0 && particles.ParticleCount <= afterLeaving, "Particles of a slot the mouse left live on for a moment");
        Frames(particles, 2.5f);
        Check(particles.ParticleCount == 0, "and then die out");

        // A stale hover id (scrolled grid, mouse elsewhere) shows nothing.
        env.Grid.hoverSlotId = 1;
        env.HoveredSlot = gear;
        env.MouseX = 600;
        Frames(particles, 0.5f);
        Check(particles.ParticleCount == 0 && particles.HoveredRect == null, "A slot the mouse is not over shows nothing even if it is still marked hovered");
        env.Grid.hoverSlotId = 99;
        Frames(particles, 0.1f);
        Check(particles.ParticleCount == 0, "An unknown hover id is ignored");
        env.Unhover();

        // The stack on the cursor: HudMouseTools draws it 17 px right of and below the mouse.
        env.MouseSlot.Itemstack = new ItemStack(Tests.MakeItem("game:gear-temporal"));
        env.MouseX = 300;
        env.MouseY = 300;
        env.Draws.Clear();
        Frames(particles, 1f);
        Check(particles.CursorRect is { } cursor && Near(cursor.CenterX, 314) && Near(cursor.CenterY, 316), $"Cursor rect follows the mouse, got {particles.CursorRect}");
        Check(env.Draws.Count > 0 && env.Draws.All(draw => Distance(draw, 314, 316) < MaxSpread), "The held temporal gear draws particles around the cursor stack");

        // Closing the inventory: the held stack goes back and its particles vanish quickly instead of floating over the world.
        env.OpenedInventories.Clear();
        env.MouseGrabbed = true;
        env.MouseSlot.Itemstack = null;
        Frames(particles, 0.25f);
        Check(particles.ParticleCount == 0, "Particles vanish within a fraction of a second when the inventory closes");
        env.MouseGrabbed = false;
        env.OpenedInventories.Add(env.Inventory);

        // Effects played by other mods.
        var sparkle = new AssetLocation(Domain, "sparkle");
        var timed = particles.Play(sparkle, ParticleAnchor.Point(700, 500), new PlayOptions { Duration = 0.5f, Layer = EnumParticleLayer.Top });
        Check(timed.IsValid && particles.IsAlive(timed), "Play returns a live handle");
        env.Draws.Clear();
        Frames(particles, 0.3f);
        particles.OnTopFrame();
        Check(env.Draws.Count > 0 && env.Draws.All(draw => Distance(draw, 700, 500) < 40), "Top layer effects are drawn by the top renderer");
        Frames(particles, 0.3f);
        Check(!particles.IsAlive(timed) && !particles.Stop(timed), "Handles die after their duration");

        var onSlot = particles.Play(sparkle, ParticleAnchor.Slot(env.Slots[3]));
        Check(particles.TryGetSlotRect(env.Slots[3], out var slotRect) && Near(slotRect.CenterX, 321), $"Slot rects are found for any visible slot, got {slotRect}");
        env.Draws.Clear();
        Frames(particles, 0.5f);
        Check(env.Draws.Count > 0 && env.Draws.All(draw => Distance(draw, 321, 23) < 40), "Slot-anchored effects are drawn at that slot");
        Check(particles.SetIntensity(onSlot, 3f) && particles.Stop(onSlot, clearParticles: true) && particles.ParticleCount == 0, "Handles can be changed and stopped");

        Check(particles.Burst(sparkle, ParticleAnchor.Point(10, 10), 6), "Bursts start");
        Frames(particles, Frame);
        Check(particles.ParticleCount == 6, $"A burst spawns its count, got {particles.ParticleCount}");

        Check(!particles.Play(new AssetLocation("mymod", "missing"), ParticleAnchor.Cursor()).IsValid && log.Messages.Count == 1, "Unknown effects return an invalid handle and are logged once");
        particles.Play(new AssetLocation("mymod", "missing"), ParticleAnchor.Cursor());
        Check(log.Messages.Count == 1, "Unknown effects are not logged again");
        log.Messages.Clear();
        Frames(particles, 1f);

        // Providers, the event, PlayItem and disabled effects.
        var rusty = Tests.MakeItem("game:gear-rusty");
        Check(particles.GetItemParticles(new ItemStack(rusty), EnumParticleTrigger.Hover).Single().Effect == new AssetLocation(Domain, "rust"), "GetItemParticles reports what the rules pick");
        particles.RegisterProvider(new RotRustyGears());
        Check(particles.GetItemParticles(new ItemStack(rusty), EnumParticleTrigger.Hover).Single().Effect == new AssetLocation(Domain, "rot"), "Providers come before the rules");
        void CancelAll(ItemParticlesEventArgs args) => args.Cancel = true;
        particles.OnItemParticles += CancelAll;
        Check(particles.GetItemParticles(new ItemStack(rusty), EnumParticleTrigger.Hover).Count == 0, "OnItemParticles can cancel");
        particles.OnItemParticles -= CancelAll;

        var item = particles.PlayItem(new ItemStack(Tests.MakeItem("game:gem-diamond-rough")), ParticleAnchor.Point(800, 100));
        Frames(particles, 1f);
        Check(particles.IsAlive(item) && particles.ParticleCount > 0, "PlayItem plays the item's own effects");
        Check(particles.Stop(item, clearParticles: true) && particles.ParticleCount == 0, "PlayItem handles stop");

        particles.ApplyConfig(new InventoryParticlesConfig { DisabledEffects = new() { "temporal" } });
        env.Hover(1);
        Frames(particles, 1f);
        Check(particles.ParticleCount == 0, "Disabled effects are never played");
        particles.ApplyConfig(new InventoryParticlesConfig { ShowOnHover = false });
        Frames(particles, 1f);
        Check(particles.ParticleCount == 0, "ShowOnHover = false hides hover particles");
        particles.ApplyConfig(new InventoryParticlesConfig());
        Frames(particles, 1f);
        Check(particles.ParticleCount > 0, "Hover particles come back with the default settings");

        // A grid scrolled so that the hovered slot is outside its clip area.
        env.SlotBounds[1].fixedY = -500;
        env.SlotBounds[1].CalcWorldBounds();
        Frames(particles, ParticleSystemFade);
        Check(particles.ParticleCount == 0, "A hovered slot scrolled out of view fades its particles");
        env.Unhover();
        env.SlotBounds[1].fixedY = 0;
        env.SlotBounds[1].CalcWorldBounds();

        // Falling drops are drawn taller than wide.
        var drip = particles.Play(new AssetLocation(Domain, "drip"), ParticleAnchor.Point(500, 100), new PlayOptions { Intensity = 10, Duration = 10 });
        env.Draws.Clear();
        Frames(particles, 2.5f);
        Check(env.Draws.Any(draw => draw.Height > draw.Size * 1.3f && draw.CenterY > 150), "Falling drops stretch");
        Check(env.Draws.Any(draw => Near(draw.Height, draw.Size) && Math.Abs(draw.CenterY - 100) < 12), "Hanging drops are round and stay on the item");
        Check(particles.Stop(drip, clearParticles: true) && particles.ParticleCount == 0, "The drops are cleared");

        HeldItems(particles, env);
        HotStacks(particles, env);
        MovingItems(particles, env);

        Check(log.Messages.Count == 0, "No errors while running frames: " + string.Join(" | ", log.Messages));
        particles.Detach();
    }

    /// <summary> Up to how far from the item centre the temporal motes may get: a slot and a fifth, so a little past the slot's edge. </summary>
    private const double MaxSpread = 48 * 1.2;

    /// <summary> An item that moves between a slot and the cursor takes its effects along: the bees fly after their skep. </summary>
    private static void MovingItems(InventoryParticlesApi particles, FakeEnvironment env)
    {
        var bees = new AssetLocation(Domain, "bees");
        env.OpenedInventories.Clear();
        env.OpenedInventories.Add(env.Inventory);
        env.MouseGrabbed = false;
        env.MouseSlot.Itemstack = null;
        env.Slots[4].Itemstack = new ItemStack(Tests.MakeItem("game:skep-reed-populated-east"));
        env.Hover(4);
        Frames(particles, 1.5f);
        var slot = particles.HoveredRect!.Value;
        int buzzing = particles.CountParticlesOf(bees);
        Check(buzzing >= 3 && particles.PositionsOf(bees).All(p => Distance(p, slot.CenterX, slot.CenterY) < 40), $"Bees buzz around the hovered skep, {buzzing}");

        // Picked up: the slot empties and the cursor gets the stack in the same frame.
        env.MouseSlot.Itemstack = env.Slots[4].Itemstack;
        env.Slots[4].Itemstack = null;
        Frames(particles, Frame);
        Check(particles.CountParticlesOf(bees) <= buzzing + 1, $"The picked up skep keeps its bees instead of getting new ones, {particles.CountParticlesOf(bees)}");

        env.MouseX = 800;
        env.MouseY = 600;
        Frames(particles, 1.5f);
        var cursor = particles.CursorRect!.Value;
        var positions = particles.PositionsOf(bees);
        Check(positions.Count >= 3 && positions.All(p => Distance(p, cursor.CenterX, cursor.CenterY) < 45),
            $"The bees followed the skep to the cursor, farthest {positions.Max(p => Distance(p, cursor.CenterX, cursor.CenterY)):0} px");

        // Carried over to another slot and put down there: the bees stay with it.
        env.MouseX = 221;
        env.MouseY = 23;
        Frames(particles, 1f);
        env.Slots[2].Itemstack = env.MouseSlot.Itemstack;
        env.MouseSlot.Itemstack = null;
        env.Hover(2);
        Frames(particles, 1.5f);
        var placed = particles.HoveredRect!.Value;
        positions = particles.PositionsOf(bees);
        Check(positions.Count >= 3 && positions.Count <= buzzing + 2 && positions.All(p => Distance(p, placed.CenterX, placed.CenterY) < 45),
            $"The bees followed the skep into its new slot, {positions.Count}");

        // A long hop in one frame (like a shift-click into the hotbar): the bees jump along instead of flying across the screen.
        env.MouseSlot.Itemstack = env.Slots[2].Itemstack;
        env.Slots[2].Itemstack = null;
        Frames(particles, 0.5f);
        env.Slots[0].Itemstack = env.MouseSlot.Itemstack;
        env.MouseSlot.Itemstack = null;
        env.Slots[0].Itemstack = env.Slots[0].Itemstack;
        env.Hover(0);
        Frames(particles, 0.5f);
        var far = particles.HoveredRect!.Value;
        positions = particles.PositionsOf(bees);
        Check(positions.Count >= 3 && positions.All(p => Distance(p, far.CenterX, far.CenterY) < 45), $"After a long hop the bees are at the new slot, {positions.Count}");
        env.Slots[2].Itemstack = env.Slots[0].Itemstack;
        env.Slots[0].Itemstack = new ItemStack(Tests.MakeItem("game:stick"));
        env.Hover(2);
        Frames(particles, 0.2f);

        // Swapped with a stick from the cursor: the stick takes the slot, the bees go with the skep onto the cursor.
        int beforeSwap = particles.CountParticlesOf(bees);
        env.MouseSlot.Itemstack = new ItemStack(Tests.MakeItem("game:stick"));
        Frames(particles, 0.2f);
        (env.MouseSlot.Itemstack, env.Slots[2].Itemstack) = (env.Slots[2].Itemstack, env.MouseSlot.Itemstack);
        Frames(particles, Frame);
        Check(particles.CountParticlesOf(bees) <= beforeSwap + 1, "A swapped skep keeps its bees");
        env.Unhover();
        env.MouseX = 600;
        env.MouseY = 400;
        Frames(particles, 1.5f);
        var swapped = particles.CursorRect!.Value;
        positions = particles.PositionsOf(bees);
        Check(positions.Count >= 3 && positions.All(p => Distance(p, swapped.CenterX, swapped.CenterY) < 45), $"The bees went with the swapped skep, {positions.Count}");
        env.Slots[2].Itemstack = env.MouseSlot.Itemstack;
        env.MouseSlot.Itemstack = null;
        env.Hover(2);
        Frames(particles, 0.2f);

        // Thrown away from the cursor: nobody takes the bees over, and they go with the skep.
        env.MouseSlot.Itemstack = env.Slots[2].Itemstack;
        env.Slots[2].Itemstack = null;
        Frames(particles, 0.5f);
        env.Unhover();
        env.MouseSlot.Itemstack = null;
        Frames(particles, 0.5f);
        Check(particles.CountParticlesOf(bees) == 0, $"Bees of a skep that is gone vanish, {particles.CountParticlesOf(bees)} left");
    }

    private static void HotStacks(InventoryParticlesApi particles, FakeEnvironment env)
    {
        var sparkburst = new AssetLocation(Domain, "sparkburst");
        var burnburst = new AssetLocation(Domain, "burnburst");
        env.OpenedInventories.Clear();
        env.OpenedInventories.Add(env.Inventory);
        env.MouseGrabbed = false;
        env.Unhover();
        Frames(particles, ParticleSystemFade + 2f);

        // Taking a red-hot ingot onto the cursor throws sparks once.
        var ingot = new ItemStack(Tests.MakeItem("game:ingot-copper"));
        ingot.Collectible.SetTemperature(env.World, ingot, 1000, false);
        env.MouseX = 300;
        env.MouseY = 300;
        env.MouseSlot.Itemstack = ingot;
        Frames(particles, Frame * 2);
        int sparks = particles.CountParticlesOf(sparkburst);
        Check(sparks >= 8 && particles.CursorRect != null, $"Taking a red-hot ingot onto the cursor throws sparks, {sparks}");

        // The server then replaces the stack with its own copy: that is not another pickup.
        env.MouseSlot.Itemstack = ingot.Clone();
        Frames(particles, Frame * 2);
        Check(particles.CountParticlesOf(sparkburst) <= sparks, "A synced copy of the stack on the cursor throws no new sparks");

        // A cold ingot throws nothing. Embers of the first burst live up to 1.2 s.
        env.MouseSlot.Itemstack = null;
        Frames(particles, 1.5f);
        env.MouseSlot.Itemstack = new ItemStack(Tests.MakeItem("game:ingot-copper"));
        Frames(particles, Frame * 2);
        Check(particles.CountParticlesOf(sparkburst) == 0, "A cold ingot throws no sparks");
        env.MouseSlot.Itemstack = null;

        // Put into the hand without tongs, the server drops it and tells the player; the hand slot under the mouse bursts.
        env.HotbarSlots[2].Itemstack = ingot;
        env.ActiveSlot = env.HotbarSlots[2];
        env.HoverHotbar(2);
        Frames(particles, 0.1f);
        particles.OnInGameError(null, InventoryParticlesApi.TooHotErrorCode, "Requires tongs to hold");
        env.HotbarSlots[2].Itemstack = null;
        Frames(particles, Frame);
        int burnt = particles.CountParticlesOf(burnburst);
        var (slotX, slotY) = env.HotbarItemCenter(2);
        env.Draws.Clear();
        particles.OnTopFrame();
        Check(burnt > sparks && env.Draws.Count > 0 && env.Draws.All(draw => Distance(draw, slotX, slotY) < 40),
            $"Dropping a stack too hot to hold bursts with more particles ({burnt}) than picking it up ({sparks}), where it was");

        // Other errors, or one long after the last hot stack was seen, burst nothing.
        env.UnhoverHotbar();
        env.ActiveSlot = null;
        Frames(particles, 3f);
        particles.OnInGameError(null, "someothererror", "");
        particles.OnInGameError(null, InventoryParticlesApi.TooHotErrorCode, "");
        Frames(particles, Frame);
        Check(particles.CountParticlesOf(burnburst) == 0, "Only a recent hot stack bursts, and only for the tongs error");
    }

    private static void HeldItems(InventoryParticlesApi particles, FakeEnvironment env)
    {
        // Playing: no inventory open, the mouse steers the camera. The gear in the active hotbar slot shows its particles there.
        env.OpenedInventories.Clear();
        env.MouseGrabbed = true;
        env.ActiveSlot = env.HotbarSlots[0];
        env.Draws.Clear();
        Frames(particles, 1f);
        var (gearX, gearY) = env.HotbarItemCenter(0);
        Check(env.Draws.Count > 0 && env.Draws.All(draw => Distance(draw, gearX, gearY) < MaxSpread), $"The gear in hand shows its particles in the hotbar at {gearX}/{gearY}: {env.Draws.Count} draws, {particles.EffectCount} effects, farthest {(env.Draws.Count > 0 ? env.Draws.Max(draw => Distance(draw, gearX, gearY)) : 0):0} px, first {(env.Draws.Count > 0 ? env.Draws[0] : default)}");

        // The off-hand too.
        env.OffhandSlot = env.HotbarSlots[1];
        env.Draws.Clear();
        Frames(particles, 1f);
        var (gemX, gemY) = env.HotbarItemCenter(1);
        Check(env.Draws.Any(draw => Distance(draw, gemX, gemY) < 30) && env.Draws.Any(draw => Distance(draw, gearX, gearY) < 30), "Both hands show their particles");
        Check(particles.EffectCount == 2, $"One effect per hand, got {particles.EffectCount}");

        // A hand slot under the mouse shows its particles once, as the hovered slot.
        env.OpenedInventories.Add(env.Inventory);
        env.MouseGrabbed = false;
        env.HoverHotbar(0);
        Frames(particles, 0.5f);
        Check(particles.EffectCount == 2 && particles.HoveredRect != null, $"The hovered hand slot is not doubled, {particles.EffectCount} effects");
        env.UnhoverHotbar();
        Frames(particles, 0.2f);
        Check(particles.EffectCount == 2, "The hand slot takes over again when the mouse leaves");

        // Not in spectator mode (no hotbar) and not when disabled.
        env.Spectator = true;
        Frames(particles, ParticleSystemFade + 2f);
        Check(particles.EffectCount == 0 && particles.ParticleCount == 0, "Spectators have no hotbar and see no hand particles");
        env.Spectator = false;
        particles.ApplyConfig(new InventoryParticlesConfig { ShowInHotbar = false });
        Frames(particles, 1f);
        Check(particles.EffectCount == 0, "ShowInHotbar = false hides hand particles");
        particles.ApplyConfig(new InventoryParticlesConfig());
        Frames(particles, 0.5f);
        Check(particles.EffectCount == 2, "Hand particles come back with the default settings");

        // Switching to an empty slot stops the effect; the particles that are out finish their life.
        env.ActiveSlot = env.HotbarSlots[2];
        Frames(particles, Frame * 2);
        Check(particles.EffectCount == 1 && particles.ParticleCount > 0, "An empty hand shows nothing new");
        env.ActiveSlot = null;
        env.OffhandSlot = null;
    }

    private static float ParticleSystemFade => InventoryParticles.Particles.ParticleSystem.HiddenGraceSeconds + InventoryParticles.Particles.ParticleSystem.FastFadeSeconds + 0.1f;

    private static void Frames(InventoryParticlesApi particles, float seconds)
    {
        for (float t = 0; t < seconds - Frame / 2; t += Frame)
        {
            particles.OnHudFrame(Frame);
        }
    }

    private static double Distance(Draw draw, double x, double y) => Math.Sqrt(Math.Pow(draw.CenterX - x, 2) + Math.Pow(draw.CenterY - y, 2));

    private static double Distance((float X, float Y) point, double x, double y) => Math.Sqrt(Math.Pow(point.X - x, 2) + Math.Pow(point.Y - y, 2));

    private static bool Near(double actual, double expected) => Math.Abs(actual - expected) < 0.01;

    private static void Check(bool condition, string label) => Tests.Check(condition, label);

    private sealed class RotRustyGears : IItemParticleProvider
    {
        public bool Collect(in ItemParticleQuery query, List<ItemParticleMatch> matches)
        {
            if (query.Stack.Collectible?.Code?.Path != "gear-rusty")
            {
                return false;
            }

            matches.Add(new ItemParticleMatch(new AssetLocation(Domain, "rot")) { Source = "test-provider" });
            return true;
        }
    }

    internal readonly record struct Draw(float CenterX, float CenterY, float Size, float Height);

    /// <summary>
    /// A faked client with one open dialog holding a five-slot grid at y 0..48, slots at x 0, 100, 200, 300, 500,
    /// and a hotbar HUD with three slots at y 900..948, x 400, 448, 496.
    /// </summary>
    private sealed class FakeEnvironment
    {
        public readonly List<Draw> Draws = new();
        public readonly DummySlot[] HotbarSlots;
        public readonly GuiElementItemSlotGrid HotbarGrid;
        public ItemSlot? ActiveSlot;
        public ItemSlot? OffhandSlot;
        public bool Spectator;
        public readonly IClientWorldAccessor World;
        public readonly List<EnumBlendMode> BlendModes = new();
        public readonly List<IInventory> OpenedInventories = new();
        public readonly DummySlot[] Slots;
        public readonly DummySlot MouseSlot = new();
        public readonly GuiElementItemSlotGrid Grid;
        public readonly ElementBounds[] SlotBounds;
        public readonly IInventory Inventory;
        public readonly ICoreClientAPI Api;
        public int MouseX;
        public int MouseY;
        public bool MouseGrabbed;
        public ItemSlot? HoveredSlot;
        public int DepthDisabled;
        public int DepthEnabled;

        public FakeEnvironment(string assets)
        {
            var dialogs = new List<GuiDialog>();
            object? world = null, gui = null, input = null, render = null, assetManager = null, classes = null;
            var logger = new Tests.TestLogger();

            Api = (ICoreClientAPI)FakeProxy.Create(typeof(ICoreClientAPI), (method, _) => method.Name switch
            {
                "get_World" => world,
                "get_Gui" => gui,
                "get_Input" => input,
                "get_Render" => render,
                "get_Assets" => assetManager,
                "get_ClassRegistry" => classes,
                "get_Logger" => logger,
                _ => Default(method.ReturnType)
            });

            var inventoryManager = FakeProxy.Create(typeof(IPlayerInventoryManager), (method, _) => method.Name switch
            {
                "get_CurrentHoveredSlot" => HoveredSlot,
                "get_MouseItemSlot" => MouseSlot,
                "get_OpenedInventories" => OpenedInventories,
                "get_ActiveHotbarSlot" => ActiveSlot,
                "get_OffhandHotbarSlot" => OffhandSlot,
                _ => Default(method.ReturnType)
            });
            var worldData = FakeProxy.Create(typeof(IWorldPlayerData), (method, _) =>
                method.Name == "get_CurrentGameMode" ? (Spectator ? EnumGameMode.Spectator : EnumGameMode.Survival) : Default(method.ReturnType));
            var player = FakeProxy.Create(typeof(IClientPlayer), (method, _) => method.Name switch
            {
                "get_InventoryManager" => inventoryManager,
                "get_WorldData" => worldData,
                _ => Default(method.ReturnType)
            });
            // Item temperatures cool down by the calendar; a stopped clock keeps them.
            var calendar = FakeProxy.Create(typeof(IGameCalendar), (method, _) => method.Name == "get_TotalHours" ? 100.0 : Default(method.ReturnType));
            world = FakeProxy.Create(typeof(IClientWorldAccessor), (method, _) => method.Name switch
            {
                "get_Player" => player,
                "get_Calendar" => calendar,
                _ => Default(method.ReturnType)
            });
            World = (IClientWorldAccessor)world;
            var icons = new IconUtil(Api);
            gui = FakeProxy.Create(typeof(IGuiAPI), (method, _) => method.Name switch
            {
                "get_OpenedGuis" => dialogs,
                "get_Icons" => icons,
                _ => Default(method.ReturnType)
            });
            input = FakeProxy.Create(typeof(IInputAPI), (method, _) => method.Name switch
            {
                "get_MouseX" => MouseX,
                "get_MouseY" => MouseY,
                "get_MouseGrabbed" => MouseGrabbed,
                _ => Default(method.ReturnType)
            });
            render = FakeProxy.Create(typeof(IRenderAPI), (method, args) =>
            {
                switch (method.Name)
                {
                    case "Render2DTexture" when args is { Length: 7 } && args[0] is int:
                        float x = (float)args[1]!, y = (float)args[2]!, w = (float)args[3]!, h = (float)args[4]!;
                        Draws.Add(new Draw(x + w / 2, y + h / 2, w, h));
                        break;
                    case "GlToggleBlend":
                        BlendModes.Add((EnumBlendMode)args![1]!);
                        break;
                    case "GLDisableDepthTest":
                        DepthDisabled++;
                        break;
                    case "GLEnableDepthTest":
                        DepthEnabled++;
                        break;
                    case "get_FrameWidth" or "get_FrameHeight":
                        return 1000;
                }

                return Default(method.ReturnType);
            });
            assetManager = FakeProxy.Create(typeof(IAssetManager), (method, args) =>
                method.Name == "GetManyInCategory" ? FindAssets(assets, (string)args![0]!, (string)args[1]!) : Default(method.ReturnType));
            classes = FakeProxy.Create(typeof(IClassRegistryAPI), (method, args) =>
                method.Name is "GetBlockClass" or "GetItemClass" ? Tests.ResolveClass((string)args![0]!) : Default(method.ReturnType));

            Slots = new[]
            {
                new DummySlot(new ItemStack(Tests.MakeItem("game:stick"))),
                new DummySlot(new ItemStack(Tests.MakeItem("game:gear-temporal"))),
                new DummySlot(),
                new DummySlot(new ItemStack(Tests.MakeItem("game:flint"))),
                new DummySlot()
            };
            Inventory = (IInventory)FakeProxy.Create(typeof(IInventory), (method, args) => method.Name switch
            {
                "get_Count" => Slots.Length,
                "get_Item" => Slots[(int)args![0]!],
                _ => Default(method.ReturnType)
            });
            OpenedInventories.Add(Inventory);

            var viewport = ElementBounds.Fixed(0, 0, 1000, 1000).WithEmptyParent();
            var gridBounds = ElementBounds.Fixed(0, 0, 600, 48).WithParent(viewport);
            Grid = new GuiElementItemSlotGrid(Api, Inventory, _ => { }, 5, null!, gridBounds);
            SlotBounds = new[] { 0, 100, 200, 300, 500 }.Select(x => ElementBounds.Fixed(x, 0, 48, 48).WithParent(gridBounds)).ToArray();
            Grid.SlotBounds = SlotBounds;
            viewport.CalcWorldBounds();
            gridBounds.CalcWorldBounds();

            foreach (var bounds in SlotBounds)
            {
                bounds.CalcWorldBounds();
            }

            Check(Grid.renderedSlots.Count == Slots.Length, "The grid lists its slots");
            var composer = new GuiComposer(Api, ElementBounds.Empty, "inventoryparticles-test");
            composer.interactiveElements.Add("grid", Grid);
            var dialog = new FakeDialog(Api, EnumDialogType.Dialog);
            dialog.Composers["test"] = composer;
            dialogs.Add(dialog);

            HotbarSlots = new[]
            {
                new DummySlot(new ItemStack(Tests.MakeItem("game:gear-temporal"))),
                new DummySlot(new ItemStack(Tests.MakeItem("game:gem-diamond-rough"))),
                new DummySlot()
            };
            var hotbarInventory = (IInventory)FakeProxy.Create(typeof(IInventory), (method, args) => method.Name switch
            {
                "get_Count" => HotbarSlots.Length,
                "get_Item" => HotbarSlots[(int)args![0]!],
                _ => Default(method.ReturnType)
            });
            var hotbarBounds = ElementBounds.Fixed(400, 900, 144, 48).WithParent(viewport);
            HotbarGrid = new GuiElementItemSlotGrid(Api, hotbarInventory, _ => { }, 3, null!, hotbarBounds);
            HotbarGrid.SlotBounds = new[] { 0, 48, 96 }.Select(x => ElementBounds.Fixed(x, 0, 48, 48).WithParent(hotbarBounds)).ToArray();
            hotbarBounds.CalcWorldBounds();

            foreach (var bounds in HotbarGrid.SlotBounds)
            {
                bounds.CalcWorldBounds();
            }

            var hotbarComposer = new GuiComposer(Api, ElementBounds.Empty, "inventory-hotbar");
            hotbarComposer.interactiveElements.Add("hotbargrid", HotbarGrid);
            var hud = new FakeDialog(Api, EnumDialogType.HUD);
            hud.Composers["hotbar"] = hotbarComposer;
            dialogs.Add(hud);
        }

        /// <summary> Items are drawn 3 px left and 1 px up of their slot centre. </summary>
        public (double X, double Y) HotbarItemCenter(int index)
        {
            var bounds = HotbarGrid.SlotBounds[index];
            return (bounds.renderX + 24 - 3, bounds.renderY + 24 - 1);
        }

        public void HoverHotbar(int index)
        {
            HotbarGrid.hoverSlotId = index;
            HoveredSlot = HotbarSlots[index];
            var bounds = HotbarGrid.SlotBounds[index];
            MouseX = (int)(bounds.renderX + 24);
            MouseY = (int)(bounds.renderY + 24);
        }

        public void UnhoverHotbar()
        {
            HotbarGrid.hoverSlotId = -1;
            HoveredSlot = null;
        }

        public void Hover(int slotId)
        {
            Grid.hoverSlotId = slotId;
            HoveredSlot = Slots[slotId];
            var bounds = SlotBounds[slotId];
            MouseX = (int)(bounds.renderX + 24);
            MouseY = (int)(bounds.renderY + 24);
        }

        public void Unhover()
        {
            Grid.hoverSlotId = -1;
            HoveredSlot = null;
        }

        private static List<IAsset> FindAssets(string assets, string category, string pathBegins)
        {
            var found = new List<IAsset>();

            foreach (string domainFolder in Directory.GetDirectories(assets))
            {
                string domain = Path.GetFileName(domainFolder);
                string folder = Path.Combine(domainFolder, category, pathBegins);

                if (!Directory.Exists(folder))
                {
                    continue;
                }

                foreach (string file in Directory.GetFiles(folder, "*", SearchOption.AllDirectories))
                {
                    string path = Path.GetRelativePath(domainFolder, file).Replace('\\', '/');
                    found.Add(FakeAsset(new AssetLocation(domain, path), File.ReadAllText(file)));
                }
            }

            return found;
        }

        private static IAsset FakeAsset(AssetLocation location, string text) => (IAsset)FakeProxy.Create(typeof(IAsset), (method, args) =>
        {
            switch (method.Name)
            {
                case "get_Location":
                    return location;
                case "get_Name":
                    return Path.GetFileName(location.Path);
                case "ToText":
                    return text;
                case "ToObject":
                    // IAsset.ToObject<T>(settings) is JsonUtil.ToObject<T>(text, domain, settings).
                    var toObject = typeof(JsonUtil).GetMethod(nameof(JsonUtil.ToObject), new[] { typeof(string), typeof(string), typeof(JsonSerializerSettings) })!;
                    return toObject.MakeGenericMethod(method.GetGenericArguments()).Invoke(null, new[] { text, location.Domain, args?[0] });
                default:
                    return Default(method.ReturnType);
            }
        });
    }

    private static object? Default(Type type) => type == typeof(void) || !type.IsValueType ? null : Activator.CreateInstance(type);

    private sealed class FakeDialog(ICoreClientAPI api, EnumDialogType type) : GuiDialog(api)
    {
        public override string ToggleKeyCombinationCode => "";

        public override EnumDialogType DialogType => type;

        public override bool IsOpened() => true;

        public override bool IsOpened(string dialogComposerName) => true;

        public override bool ShouldReceiveRenderEvents() => true;
    }
}

/// <summary> Interface fake whose calls go to a handler. Same approach as SmoothSwapping's integration smoke test. </summary>
public class FakeProxy : DispatchProxy
{
    private System.Func<MethodInfo, object?[]?, object?> handler = null!;
    private static bool apiAccessConfigured;

    public static object Create(Type interfaceType, System.Func<MethodInfo, object?[]?, object?> handler)
    {
        object proxy = Create(interfaceType, typeof(FakeProxy));

        if (!apiAccessConfigured)
        {
            // IClientPlayer includes an internal default interface method. DispatchProxy normally adds this attribute
            // for internal types, but misses internal methods of public types. Give only this in-memory test proxy
            // assembly access to the installed API internals.
            Type generator = typeof(DispatchProxy).Assembly.GetType("System.Reflection.DispatchProxyGenerator", true)!;
            object map = generator.GetField("s_alcProxyAssemblyMap", BindingFlags.Static | BindingFlags.NonPublic)!.GetValue(null)!;
            object?[] lookup = { System.Runtime.Loader.AssemblyLoadContext.Default, null };
            bool found = (bool)map.GetType().GetMethod("TryGetValue")!.Invoke(map, lookup)!;

            if (!found)
            {
                throw new Exception("DispatchProxy did not register its dynamic assembly.");
            }

            lookup[1]!.GetType().GetMethod("GenerateInstanceOfIgnoresAccessChecksToAttribute", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(lookup[1], new object[] { typeof(ICoreClientAPI).Assembly.GetName().Name! });
            apiAccessConfigured = true;
        }

        ((FakeProxy)proxy).handler = handler;
        return proxy;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args) => handler(targetMethod!, args);
}
