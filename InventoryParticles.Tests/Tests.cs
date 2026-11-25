using System.Reflection;
using System.Runtime.CompilerServices;
using InventoryParticles.Api;
using InventoryParticles.Definitions;
using InventoryParticles.Gui;
using InventoryParticles.Particles;
using Vintagestory.API.Common;
using Vintagestory.API.MathTools;
using Vintagestory.GameContent;

internal static class Tests
{
    private const string Domain = "inventoryparticles";
    private const float Frame = 1f / 60f;
    internal const int BundledEffects = 17;
    internal const int BundledRules = 22;

    private static int checks;

    [MethodImpl(MethodImplOptions.NoInlining)]
    public static int Run()
    {
        string assets = typeof(Tests).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .First(attribute => attribute.Key == "AssetsDirectory").Value!;
        var log = new TestLogger();

        var effects = LoadEffects(assets, log);
        var rules = LoadRules(assets, log);
        Check(log.Messages.Count == 0, "Bundled JSON loads without warnings: " + string.Join(" | ", log.Messages));

        RuleReferences(effects, rules);
        RuleMatching(rules);
        TemperatureCurve();
        AssetCodes();
        Textures();
        SpawnRate();
        LifetimeAndFading();
        Gates(effects);
        HiddenAnchors();
        CapacityAndBursts();
        TemporalColor(effects);
        Layers();
        HangingDrops(effects);
        OffScreen();
        Insects(effects);
        Check(log.Messages.Count == 0, "No warnings while testing: " + string.Join(" | ", log.Messages));
        EngineTests.Run(assets);

        return checks;
    }

    #region Loading

    private static EffectRegistry LoadEffects(string assets, TestLogger log)
    {
        var registry = new EffectRegistry();
        string folder = Path.Combine(assets, Domain, "config", "inventoryparticles", "effects");

        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Order())
        {
            // The same deserializer IAsset.ToObject uses.
            var effect = JsonUtil.ToObject<GuiParticleEffect>(File.ReadAllText(file), Domain);
            var code = EffectRegistry.CodeFromAsset(new AssetLocation(Domain, "config/inventoryparticles/effects/" + Path.GetRelativePath(folder, file).Replace('\\', '/')));
            Check(effect != null && effect.Emitters.Length > 0, $"Effect {code} has emitters");
            registry.Register(code, effect!, log);

            foreach (var emitter in effect!.Emitters)
            {
                Check(emitter.LifeLength.avg > 0f && emitter.Size.avg > 0f, $"Effect {code}/{emitter.Code} has a life and a size");
                Check(emitter.Rate.avg > 0f || emitter.Burst > 0, $"Effect {code}/{emitter.Code} emits something");
            }
        }

        foreach (string name in new[] { "temporal", "steam", "hotmetal", "torchflame", "lampflame", "candleflame", "rust", "rot", "sparkle", "shine", "drip", "drip-narrow", "drip-spout", "flies", "bees", "sparkburst", "burnburst" })
        {
            Check(registry.TryGet(new AssetLocation(Domain, name), out _), $"Effect {Domain}:{name} exists");
        }

        return registry;
    }

    private static RuleRegistry LoadRules(string assets, TestLogger log)
    {
        var registry = new RuleRegistry();
        registry.Attach(ResolveClass, log, new FakeInspector());
        string folder = Path.Combine(assets, Domain, "config", "inventoryparticles", "rules");

        foreach (string file in Directory.GetFiles(folder, "*.json", SearchOption.AllDirectories).Order())
        {
            var rules = JsonUtil.ToObject<ItemParticleRule[]>(File.ReadAllText(file), Domain);
            Check(rules is { Length: > 0 }, $"Rule file {Path.GetFileName(file)} has rules");

            foreach (var rule in rules!)
            {
                rule.Domain = Domain;
                registry.Register(rule);
            }
        }

        var codes = registry.Rules.Select(rule => rule.Source.Code).ToArray();
        Check(codes.Length == codes.Distinct().Count(), "Rule codes are unique");
        Check(codes.Length == BundledRules, $"All {BundledRules} bundled rules are active, got {codes.Length}");
        return registry;
    }

    internal static Type? ResolveClass(string name) =>
        typeof(BlockMeal).Assembly.GetType("Vintagestory.GameContent." + name) ??
        typeof(Block).Assembly.GetType("Vintagestory.API.Common." + name);

    #endregion

    #region Rules

    private static void RuleReferences(EffectRegistry effects, RuleRegistry rules)
    {
        foreach (var rule in rules.Rules)
        {
            foreach (var effect in rule.Effects)
            {
                Check(effects.TryGet(effect, out _), $"Rule {rule.Source.Code} refers to the existing effect {effect}");
            }
        }

        var order = rules.Rules.Select(rule => rule.Source.Priority).ToArray();
        Check(order.SequenceEqual(order.OrderByDescending(priority => priority)), "Rules are checked by descending priority");
        Check(rules.Rules[^1].Source.Code == "hotmetal", "The generic hot metal rule is checked last");
    }

    private static void RuleMatching(RuleRegistry rules)
    {
        ExpectEffect(rules, Item("game:gear-temporal"), null, "temporal");
        ExpectEffect(rules, Item("game:largegear-temporal-3"), null, "temporal");
        ExpectEffect(rules, Item("game:clothes-neck-gear-amulet-temporal"), null, "temporal");
        ExpectEffect(rules, Item("game:gear-rusty"), null, "rust");
        ExpectEffect(rules, Item("game:rot"), null, "rot");
        ExpectEffect(rules, Block<Block>("game:torch-basic-lit-up"), null, "torchflame");
        ExpectEffect(rules, Block<Block>("game:torch-basic-extinct-up"), null, null);
        ExpectEffect(rules, Block<Block>("game:torch-basic-burnedout-up"), null, null);
        ExpectEffect(rules, Block<Block>("game:oillamp-blue-up"), null, "lampflame");
        ExpectEffect(rules, Block<Block>("game:oillamp-red-north"), null, "lampflame");
        ExpectEffect(rules, Block<Block>("game:oillamp-genie-blue-raw"), null, null);
        ExpectEffect(rules, Block<Block>("game:oillamp-genie-blue-fired"), null, null);
        ExpectEffect(rules, Item("game:clothes-neck-gear-amulet-rusty"), null, null);
        ExpectEffect(rules, Item("game:ingot-copper"), null, null);

        // Gold and silver jewelry shine in their metal's color; copper, bronze and brass ones don't.
        var collar = ExpectEffect(rules, Item("game:clothes-neck-fancy-royal-collar"), null, "shine");
        Check(collar.Source == "jewelry-gold" && collar.Tint != null && collar.Tint.X > collar.Tint.Z, "Gold jewelry glints warm");
        var lunula = ExpectEffect(rules, Item("game:clothes-neck-lunula"), null, "shine");
        Check(lunula.Source == "jewelry-silver" && lunula.Tint != null && lunula.Tint.Z >= lunula.Tint.X, "Silver jewelry glints cool");

        foreach (string code in new[] { "game:clothes-arm-silver-bracelet", "game:clothes-head-crown", "game:clothes-emblem-silver-brooch", "game:clothes-waist-gold-waist-chain" })
        {
            ExpectEffect(rules, Item(code), null, "shine");
        }

        foreach (string code in new[] { "game:clothes-neck-copper-torc", "game:clothes-neck-bronze-gem-necklace", "game:clothes-neck-pearl-necklace", "game:clothes-face-funeralmask1" })
        {
            ExpectEffect(rules, Item(code), null, null);
        }

        // Buckets drip only with a liquid inside, in the liquid's color.
        ExpectEffect(rules, Block<BlockBucket>("game:woodbucket"), null, null);
        var bucket = new ItemStack(Block<BlockBucket>("game:woodbucket"));
        bucket.Attributes.SetString(FakeInspector.LiquidAttribute, "honey");
        var drip = new List<ItemParticleMatch>();
        Check(rules.Collect(bucket, null, EnumParticleTrigger.Held, null, drip) && drip.Single().Effect == new AssetLocation(Domain, "drip"), "A bucket of honey drips");
        Check(drip[0].Tint is { } honey && Near(honey.X, FakeInspector.Honey.X) && Near(honey.Z, FakeInspector.Honey.Z), $"The drops take the liquid's color, got {drip[0].Tint}");
        Check(drip[0].OriginY < -0.3f, "Drops form at the bottom of the bucket");
        bucket.Attributes.SetString(FakeInspector.LiquidAttribute, "rot");
        drip.Clear();
        Check(!rules.Collect(bucket, null, EnumParticleTrigger.Hover, null, drip) && drip.Count == 0, "A bucket of rot (not a liquid) doesn't drip");

        // Watering cans drip from the spout, jugs and bowls from the bottom, all only with a liquid inside.
        ExpectEffect(rules, Liquid(Block<BlockWateringCan>("game:wateringcan-blue-fired"), "honey"), null, "drip-spout");
        ExpectEffect(rules, Block<BlockWateringCan>("game:wateringcan-blue-fired"), null, null);
        ExpectEffect(rules, Liquid(Block<BlockLiquidContainerTopOpened>("game:jug-red-fired"), "honey"), null, "drip-narrow");
        ExpectEffect(rules, Liquid(Block<BlockLiquidContainerTopOpened>("game:bowl-red-fired"), "honey"), null, "drip-narrow");
        ExpectEffect(rules, Block<BlockLiquidContainerTopOpened>("game:bowl-red-fired"), null, null);

        ExpectEffect(rules, Item("game:candle"), null, "candleflame");
        ExpectEffect(rules, Block<BlockSkep>("game:skep-reed-populated-east"), null, "bees");
        ExpectEffect(rules, Block<BlockSkep>("game:skep-reed-empty-east"), null, null);

        // Bread from the clay oven has no temperature, so it shows nothing.
        ExpectEffect(rules, Item("game:bread-spelt-perfect"), null, null);

        // Flies over food that is at least half spoiled.
        ExpectEffect(rules, Spoiled(Item("game:redmeat-raw"), 0.6f), null, "flies");
        ExpectEffect(rules, Spoiled(Item("game:redmeat-raw"), 0.3f), null, null);
        ExpectEffect(rules, Spoiled(Item("game:bread-spelt-perfect"), 0.7f), null, "flies");

        // One-shot triggers only reach rules that ask for them.
        ExpectEffect(rules, Hot(Item("game:ingot-copper"), 900), null, "sparkburst", EnumParticleTrigger.Pickup);
        ExpectEffect(rules, Hot(Item("game:ingot-copper"), 300), null, null, EnumParticleTrigger.Pickup);
        ExpectEffect(rules, Hot(Item("game:ingot-copper"), 300), null, "burnburst", EnumParticleTrigger.TooHot);
        ExpectEffect(rules, Item("game:gear-temporal"), null, null, EnumParticleTrigger.Pickup);
        ExpectEffect(rules, Hot(Item("game:ingot-iron"), 900), null, "burnburst", EnumParticleTrigger.TooHot);
        ExpectEffect(rules, Hot(Item("game:ingot-copper"), 900), null, "hotmetal", EnumParticleTrigger.Cursor);

        var emerald = ExpectEffect(rules, Item("game:gem-emerald-rough"), null, "sparkle");
        Check(emerald.Tint != null && emerald.Tint.Y > emerald.Tint.X, "Emerald sparkles are tinted green");

        // Temperature-driven rules.
        ExpectEffect(rules, Item("game:ingot-copper"), 240f, null);
        var warm = ExpectEffect(rules, Item("game:ingot-copper"), 300f, "hotmetal");
        var hot = ExpectEffect(rules, Item("game:ingot-copper"), 1100f, "hotmetal");
        Near(warm.Intensity, 0.2 + 0.8 * 50 / 1050.0, 1e-4, "Hot metal intensity at 300 °C");
        Check(hot.Intensity > warm.Intensity, "Hotter metal emits more");

        ExpectEffect(rules, Block<BlockCookedContainer>("game:claypot-burned-cooked"), 40f, null);
        var pot = ExpectEffect(rules, Block<BlockCookedContainer>("game:claypot-burned-cooked"), 125f, "steam");
        Near(pot.Intensity, 0.3 + 0.7 * 0.5, 1e-4, "Pot steam intensity at 125 °C");
        ExpectEffect(rules, Block<BlockCookingContainer>("game:claypot-burned-fired"), 125f, "steam");
        var bowl = ExpectEffect(rules, Block<BlockMeal>("game:bowl-meal"), 125f, "steam");
        Check(bowl.Source == "mealbowl" && bowl.Intensity < pot.Intensity, "Meal bowls steam less than pots");
        // A pot is never hot enough for the metal rule, but if it were, the pot rule still comes first.
        Check(ExpectEffect(rules, Block<BlockCookedContainer>("game:claypot-burned-cooked"), 400f, "steam").Source == "pot", "Pot rule beats the hot metal rule");

        // Triggers and disabled rules.
        var custom = new RuleRegistry();
        custom.Attach(ResolveClass, new TestLogger());
        custom.Register(new ItemParticleRule
        {
            Code = "hoveronly",
            Match = new RuleMatch { Codes = new[] { "game:stick" } },
            Triggers = new[] { EnumParticleTrigger.Hover },
            Effects = new[] { "inventoryparticles:steam" }
        });
        var stick = new ItemStack(Item("game:stick"));
        var matches = new List<ItemParticleMatch>();
        Check(custom.Collect(stick, null, EnumParticleTrigger.Hover, null, matches), "Hover-only rule matches on hover");
        matches.Clear();
        Check(!custom.Collect(stick, null, EnumParticleTrigger.Cursor, null, matches) && matches.Count == 0, "Hover-only rule ignores the cursor");
        custom.SetDisabled(new[] { "HOVERONLY" });
        Check(!custom.Collect(stick, null, EnumParticleTrigger.Hover, null, matches), "Disabled rules are skipped (case-insensitive)");

        // Without an inspector, rules with per-stack conditions never apply.
        var blind = new RuleRegistry();
        blind.Attach(ResolveClass, new TestLogger());
        blind.Register(new ItemParticleRule { Code = "spoiled", MinSpoilage = 0.1f, Effects = new[] { "inventoryparticles:flies" } });
        Check(!blind.Collect(Spoiled(Item("game:redmeat-raw"), 0.9f), null, EnumParticleTrigger.Hover, null, matches), "Spoilage rules need an inspector");

        // An unknown class must not turn the rule into a match-everything rule.
        var unknown = new RuleRegistry();
        var unknownLog = new TestLogger();
        unknown.Attach(ResolveClass, unknownLog);
        unknown.Register(new ItemParticleRule { Code = "unknownclass", Match = new RuleMatch { Classes = new[] { "NoSuchBlockClass" } }, Effects = new[] { "steam" } });
        Check(unknown.Rules.Count == 0 && unknownLog.Messages.Count == 1, "Rules with only unknown classes are dropped with a warning");
    }

    private static ItemParticleMatch ExpectEffect(RuleRegistry rules, CollectibleObject collectible, float? temperature, string? effect, EnumParticleTrigger trigger = EnumParticleTrigger.Hover) =>
        ExpectEffect(rules, new ItemStack(collectible), temperature, effect, trigger);

    private static ItemParticleMatch ExpectEffect(RuleRegistry rules, ItemStack stack, float? temperature, string? effect, EnumParticleTrigger trigger = EnumParticleTrigger.Hover)
    {
        var matches = new List<ItemParticleMatch>();
        temperature ??= stack.Attributes.TryGetFloat(FakeInspector.TemperatureAttribute);
        rules.Collect(stack, null, trigger, temperature, matches);
        string label = $"{stack.Collectible.Code} ({trigger}) at {temperature?.ToString() ?? "room temperature"}";

        if (effect == null)
        {
            Check(matches.Count == 0, $"{label} shows nothing, got {string.Join(", ", matches.Select(match => match.Effect))}");
            return null!;
        }

        Check(matches.Count == 1 && matches[0].Effect == new AssetLocation(Domain, effect),
            $"{label} shows {Domain}:{effect}, got {string.Join(", ", matches.Select(match => match.Effect))}");
        return matches[0];
    }

    private static bool Near(float a, float b) => Math.Abs(a - b) < 1e-4f;

    private static ItemStack Liquid(CollectibleObject container, string liquid)
    {
        var stack = new ItemStack(container);
        stack.Attributes.SetString(FakeInspector.LiquidAttribute, liquid);
        return stack;
    }

    private static ItemStack Spoiled(CollectibleObject collectible, float spoilage) => Spoiled(new ItemStack(collectible), spoilage);

    private static ItemStack Spoiled(ItemStack stack, float spoilage)
    {
        stack.Attributes.SetFloat(FakeInspector.SpoilageAttribute, spoilage);
        return stack;
    }

    /// <summary> The rules get the temperature from the caller; ExpectEffect reads it back from this attribute. </summary>
    private static ItemStack Hot(CollectibleObject collectible, float temperature)
    {
        var stack = new ItemStack(collectible);
        stack.Attributes.SetFloat(FakeInspector.TemperatureAttribute, temperature);
        return stack;
    }

    private static Item Item(string code) => MakeItem(code);

    internal static Item MakeItem(string code) => new() { Code = new AssetLocation(code) };

    private static T Block<T>(string code) where T : Block, new() => new() { Code = new AssetLocation(code) };

    private static void TemperatureCurve()
    {
        var curve = new TemperatureIntensity { From = 50, To = 200, Min = 0.3f, Max = 1f };
        Near(curve.Evaluate(0), 0.3, 1e-6, "Curve below the range");
        Near(curve.Evaluate(125), 0.65, 1e-6, "Curve in the middle");
        Near(curve.Evaluate(5000), 1, 1e-6, "Curve above the range");
    }

    private static void AssetCodes()
    {
        Check(EffectRegistry.CodeFromAsset(new AssetLocation("mymod", "config/inventoryparticles/effects/sub/glow.json")) == new AssetLocation("mymod", "sub/glow"),
            "Effect codes come from the file path");
    }

    #endregion

    #region Particles

    private static void Textures()
    {
        static int Alpha(int[] pixels, int n, int x, int y) => (pixels[y * n + x] >> 24) & 0xFF;

        foreach (var shape in Enum.GetValues<EnumGuiParticleShape>())
        {
            var pixels = ParticleTextures.CreatePixels(shape, 16);
            Check(pixels.All(pixel => (pixel & 0xFFFFFF) == 0xFFFFFF), $"{shape} sprite is white");
            Check(Alpha(pixels, 16, 8, 8) > 230, $"{shape} sprite is opaque in the centre");

            if (shape != EnumGuiParticleShape.Square)
            {
                Check(Alpha(pixels, 16, 0, 0) == 0, $"{shape} sprite is clear in the corners");
            }
        }

        var square = ParticleTextures.CreatePixels(EnumGuiParticleShape.Square, 16);
        Check(Alpha(square, 16, 0, 8) < Alpha(square, 16, 8, 8), "Square sprite fades at the edge");

        // Row 0 is drawn at the top of the screen.
        var drop = ParticleTextures.CreatePixels(EnumGuiParticleShape.Drop, 16);
        int Width(int row) => Enumerable.Range(0, 16).Count(x => Alpha(drop, 16, x, row) > 128);
        Check(Width(3) > 0 && Width(3) < Width(12), $"Drops are pointed at the top and round at the bottom, widths {Width(3)} and {Width(12)}");
        Check(ParticleTextures.CreatePixels(EnumGuiParticleShape.Square, 4).All(pixel => ((pixel >> 24) & 0xFF) == 255), "Tiny squares stay solid");
    }

    private static void SpawnRate()
    {
        var system = new ParticleSystem(512, new Random(1234));
        var instance = system.Start(new AssetLocation("test:rate"), Effect(new GuiEmitterProps { Rate = NatFloat.createUniform(10, 0), LifeLength = NatFloat.createUniform(5, 0) }), ParticleAnchor.Point(100, 100))!;
        Simulate(system, 1f);
        Check(system.Count is >= 10 and <= 11, $"10 particles per second spawn 10±1 in a second, got {system.Count}");
        Check(system.Particles[0].X is > 90f and < 110f, "Particles spawn at the anchor");

        instance.SetIntensity(2f);
        int before = system.Count;
        Simulate(system, 1f);
        Check(system.Count - before is >= 19 and <= 21, $"Intensity 2 doubles the rate, got {system.Count - before}");

        system.DensityMultiplier = 0f;
        before = system.Count;
        Simulate(system, 1f);
        Check(system.Count == before, "Density 0 stops spawning");
    }

    private static void LifetimeAndFading()
    {
        var system = new ParticleSystem(512, new Random(1));
        var effect = Effect(new GuiEmitterProps { Rate = NatFloat.createUniform(20, 0), LifeLength = NatFloat.createUniform(0.5f, 0) });
        var instance = system.Start(new AssetLocation("test:life"), effect, ParticleAnchor.Point(0, 0))!;
        Simulate(system, 1f);
        Check(system.Count is > 5 and <= 11, $"Short-lived particles die, {system.Count} alive");
        instance.Stop(false);
        Simulate(system, 0.55f);
        Check(system.Count == 0 && system.Effects.Count == 0, "Stopped effects disappear once their particles die");

        var slow = Effect(new GuiEmitterProps { Rate = NatFloat.createUniform(20, 0), LifeLength = NatFloat.createUniform(10, 0) });
        var fading = system.Start(new AssetLocation("test:fade"), slow, ParticleAnchor.Point(0, 0))!;
        Simulate(system, 1f);
        Check(system.Count > 10, "Long-lived particles accumulate");
        fading.StopAndFade();
        Simulate(system, ParticleSystem.FastFadeSeconds + 0.05f);
        Check(system.Count == 0, "Stopping with a fade clears the particles within the fade time");

        var timed = system.Start(new AssetLocation("test:duration"), slow, ParticleAnchor.Point(0, 0))!;
        timed.EndTime = system.Time + 0.5;
        Simulate(system, 1f);
        Check(!timed.Alive, "Effects stop after their duration");
        int count = system.Count;
        Simulate(system, 0.5f);
        Check(system.Count == count, "Nothing spawns after the duration");
        timed.Stop(true);
        Check(system.Count == 0, "Stopping with clearParticles removes the particles");
    }

    private static void Gates(EffectRegistry effects)
    {
        Check(effects.TryGet(new AssetLocation(Domain, "hotmetal"), out var hotmetal), "Hot metal effect exists");

        string[] Emitters(float temperature)
        {
            var system = new ParticleSystem(4096, new Random(7));
            var instance = system.Start(new AssetLocation(Domain, "hotmetal"), hotmetal!, ParticleAnchor.Point(0, 0))!;
            instance.Temperature = temperature;
            var resolver = new FakeResolver();
            var codes = new HashSet<string>();

            // Collect every frame: short-lived sparks may all be dead at the end.
            for (int frame = 0; frame < 180; frame++)
            {
                system.Update(Frame, resolver);

                foreach (ref readonly var particle in system.Particles)
                {
                    codes.Add(particle.Props.Code ?? "");
                }
            }

            return codes.Order().ToArray();
        }

        Check(Emitters(200).Length == 0, "No hot metal particles below 250 °C");
        Check(Emitters(300).SequenceEqual(new[] { "smoke" }), "Only smoke at 300 °C");
        Check(Emitters(600).SequenceEqual(new[] { "embers", "smoke" }), "Smoke and embers at 600 °C");
        Check(Emitters(1100).SequenceEqual(new[] { "embers", "smoke", "sparks" }), "Smoke, embers and sparks at 1100 °C");
    }

    private static void HiddenAnchors()
    {
        var system = new ParticleSystem(512, new Random(3));
        var effect = Effect(new GuiEmitterProps { Rate = NatFloat.createUniform(30, 0), LifeLength = NatFloat.createUniform(10, 0) });
        system.Start(new AssetLocation("test:hidden"), effect, ParticleAnchor.Tracked(() => null));
        Simulate(system, 1f);
        Check(system.Count == 0, "Hidden anchors spawn nothing");

        bool visible = true;
        var toggled = system.Start(new AssetLocation("test:toggle"), effect, ParticleAnchor.Tracked(() => visible ? new GuiItemRect(50, 50, 20) : null))!;
        Simulate(system, 1f);
        Check(system.Count > 20, "Visible anchors spawn");
        Check(system.Particles[0].Size0 is > 1.9f and < 2.1f, "Sizes are in item units (0.1 × 20 px)");
        visible = false;
        Simulate(system, ParticleSystem.HiddenGraceSeconds + ParticleSystem.FastFadeSeconds + 0.05f);
        Check(system.Count == 0 && toggled.Alive, "Particles of a hidden anchor fade, the effect keeps running");
        visible = true;
        Simulate(system, 0.5f);
        Check(system.Count > 5, "The effect resumes when the anchor is visible again");
    }

    private static void CapacityAndBursts()
    {
        var system = new ParticleSystem(5, new Random(5));
        var effect = Effect(new GuiEmitterProps { Rate = NatFloat.createUniform(100, 0), LifeLength = NatFloat.createUniform(10, 0) });
        system.Start(new AssetLocation("test:capacity"), effect, ParticleAnchor.Point(0, 0));
        Simulate(system, 1f);
        Check(system.Count == 5, "The particle limit is respected");

        system = new ParticleSystem(512, new Random(5));
        var burst = system.Start(new AssetLocation("test:burst"), effect, ParticleAnchor.Point(0, 0))!;
        burst.BurstOverride = 7;
        burst.BurstOnly = true;
        system.Update(Frame, new FakeResolver());
        Check(system.Count == 7 && !burst.Alive, $"Bursts spawn their count once and stop, got {system.Count}");

        system = new ParticleSystem(512, new Random(5));
        var many = new List<EffectInstance?>();
        for (int i = 0; i < ParticleSystem.MaxEffects + 5; i++)
        {
            many.Add(system.Start(new AssetLocation("test:many"), effect, ParticleAnchor.Point(0, 0)));
        }

        Check(many.Count(instance => instance == null) == 5, "The number of running effects is limited");
    }

    private static void TemporalColor(EffectRegistry effects)
    {
        Check(effects.TryGet(new AssetLocation(Domain, "temporal"), out var temporal), "Temporal effect exists");
        var system = new ParticleSystem(512, new Random(11));
        system.Start(new AssetLocation(Domain, "temporal"), temporal!, ParticleAnchor.Point(0, 0));
        Simulate(system, 1f);
        Check(system.Count > 3, "Temporal motes spawn");

        foreach (ref readonly var particle in system.Particles)
        {
            // Vanilla's ItemTemporalGear uses HsvToRgba(110..124, 180, 100..149, 150): teal green.
            Check(particle.G0 > particle.R0 && particle.G0 >= particle.B0 && particle.B0 > particle.R0, $"Temporal motes are teal green, got {particle.R0}/{particle.G0}/{particle.B0}");
            Check(Math.Abs(particle.A0 - 150) < 0.5f, "Temporal motes start at vanilla's alpha 150");
            Check(!particle.Relative && particle.Glow, "Temporal motes glow and stay where they appeared when the gear moves");
        }

        int vanilla = ColorUtil.HsvToRgba(117, 180, 125, 150);
        Check((vanilla & 0xFF) == 36 && ((vanilla >> 8) & 0xFF) == 125 && ((vanilla >> 16) & 0xFF) == 101, "HsvToRgba puts red in the lowest byte");
    }

    private static void Layers()
    {
        Check(ParticleSystem.ResolveLayer(EnumParticleLayer.AboveItem, ParticleAnchor.Cursor()) == EnumParticleLayer.Top, "Above-item particles of the cursor stack are drawn above it");
        Check(ParticleSystem.ResolveLayer(EnumParticleLayer.AboveItem, ParticleAnchor.Point(0, 0)) == EnumParticleLayer.BelowCursor, "Above-item particles elsewhere stay below the cursor stack");
        Check(ParticleSystem.ResolveLayer(EnumParticleLayer.Top, ParticleAnchor.Point(0, 0)) == EnumParticleLayer.Top, "Top stays top");
        Check(ParticleSystem.ResolveLayer((EnumParticleLayer)42, ParticleAnchor.Point(0, 0)) == EnumParticleLayer.BelowCursor, "Unknown layers fall back to below the cursor");

        var system = new ParticleSystem(64, new Random(9));
        system.Start(new AssetLocation("test:aboveitem"), Effect(new GuiEmitterProps { Layer = EnumParticleLayer.AboveItem, Burst = 3, Rate = NatFloat.createUniform(0, 0) }), ParticleAnchor.Point(10, 10));
        Simulate(system, Frame);
        Check(system.Count == 3 && system.CountIn(EnumParticleLayer.BelowCursor) == 3 && system.CountIn(EnumParticleLayer.AboveItem) == 0, "Above-item particles are counted in the layer they are drawn in");
    }

    private static void HangingDrops(EffectRegistry effects)
    {
        Check(effects.TryGet(new AssetLocation(Domain, "drip"), out var drip) && drip!.Emitters.Single() is { Shape: EnumGuiParticleShape.Drop, Stretch: > 0f } dripProps && dripProps.Hang.avg > 0f,
            "Bucket drops are drop shaped, hang before falling and stretch while falling");
        Check(effects.TryGet(new AssetLocation(Domain, "shine"), out var shine) && shine!.Emitters.All(emitter => emitter.Layer == EnumParticleLayer.AboveItem),
            "Jewelry glints are drawn above the item, also on the cursor");

        var system = new ParticleSystem(64, new Random(2));
        double anchorX = 100, anchorY = 100;
        var props = new GuiEmitterProps
        {
            Burst = 1,
            Rate = NatFloat.createUniform(0, 0),
            Hang = NatFloat.createUniform(0.5f, 0),
            LifeLength = NatFloat.createUniform(10, 0),
            GravityEffect = NatFloat.createUniform(10, 0),
            Stretch = 0.1f
        };
        system.Start(new AssetLocation("test:drop"), Effect(props), ParticleAnchor.Tracked(() => new GuiItemRect(anchorX, anchorY, 48)));
        Simulate(system, 0.25f);
        Check(system.Count == 1 && system.Particles[0].Clinging && system.Particles[0].VY == 0f, "A hanging drop clings without moving");

        anchorX = 200;
        Simulate(system, 0.1f);
        Check(Near(system.Particles[0].X + system.Effects[0].AnchorX, 200f), "A hanging drop follows the item it hangs on");

        Simulate(system, 0.4f);
        var released = system.Particles[0];
        Check(!released.Clinging && released.VY > 0f && released.Y > 100f && Near(released.X, 200f), $"Then it lets go and falls in screen space, at {released.X}/{released.Y}");

        anchorX = 400;
        Simulate(system, 0.1f);
        Check(Near(system.Particles[0].X, 200f), "A falling drop no longer follows the item");
        Check(system.Particles[0].Stretch > 0f, "Falling drops stretch");
    }

    private static void OffScreen()
    {
        var system = new ParticleSystem(64, new Random(4)) { ScreenWidth = 200, ScreenHeight = 200 };
        var props = new GuiEmitterProps { Burst = 5, Rate = NatFloat.createUniform(0, 0), LifeLength = NatFloat.createUniform(20, 0), GravityEffect = NatFloat.createUniform(20, 0) };
        system.Start(new AssetLocation("test:falling"), Effect(props), ParticleAnchor.Point(100, 150, 20));
        Simulate(system, 0.1f);
        Check(system.Count == 5, "Falling particles are alive while on screen");
        Simulate(system, 3f);
        Check(system.Count == 0, $"Particles that fall off the screen die before their life ends, {system.Count} left");

        var anchored = new ParticleSystem(64, new Random(4)) { ScreenWidth = 200, ScreenHeight = 200 };
        anchored.Start(new AssetLocation("test:anchored"), Effect(new GuiEmitterProps { Space = EnumParticleSpace.Anchor, Burst = 2, Rate = NatFloat.createUniform(0, 0), LifeLength = NatFloat.createUniform(20, 0) }), ParticleAnchor.Point(100, 100, 20));
        Simulate(anchored, 1f);
        Check(anchored.Count == 2, "Anchor-space particles are culled by their screen position, not their relative one");
    }

    private static void Insects(EffectRegistry effects)
    {
        foreach (string name in new[] { "flies", "bees" })
        {
            Check(effects.TryGet(new AssetLocation(Domain, name), out var effect), $"Effect {name} exists");
            var system = new ParticleSystem(256, new Random(21));
            system.Start(new AssetLocation(Domain, name), effect!, ParticleAnchor.Point(500, 500, 48));
            var resolver = new FakeResolver();
            float farthest = 0f, speedSum = 0f;
            int minAlive = int.MaxValue, samples = 0;

            for (int frame = 0; frame < 900; frame++)
            {
                system.Update(Frame, resolver);
                minAlive = Math.Min(minAlive, system.Count);

                foreach (ref readonly var p in system.Particles)
                {
                    // Insects move with the item, so their position is relative to it.
                    farthest = Math.Max(farthest, MathF.Sqrt(p.X * p.X + p.Y * p.Y));
                    speedSum += MathF.Sqrt(p.VX * p.VX + p.VY * p.VY);
                    samples++;
                }
            }

            float speed = speedSum / Math.Max(1, samples);
            Check(minAlive >= 1 && farthest < 48 * 0.85f && speed > 10f, $"{name} keep buzzing around the item: at least {minAlive} alive, up to {farthest:0} px away, {speed:0} px/s");
        }
    }

    private static GuiParticleEffect Effect(GuiEmitterProps props)
    {
        var effect = new GuiParticleEffect { Emitters = new[] { props } };
        EffectRegistry.Normalize(effect, "test", null);
        return effect;
    }

    private static void Simulate(ParticleSystem system, float seconds)
    {
        var resolver = new FakeResolver();

        for (float t = 0; t < seconds - Frame / 2; t += Frame)
        {
            system.Update(Frame, resolver);
        }
    }

    #endregion

    internal static void Check(bool condition, string label)
    {
        checks++;

        if (!condition)
        {
            throw new Exception("Check failed: " + label);
        }
    }

    internal static void Near(double actual, double expected, double tolerance, string label) =>
        Check(Math.Abs(actual - expected) <= tolerance, $"{label}: expected {expected}, got {actual}");

    /// <summary>
    /// Reads test attributes instead of game data: <see cref="LiquidAttribute"/> honey (colored), unknown (no color) or anything else
    /// (not a liquid); <see cref="SpoilageAttribute"/> as a number.
    /// </summary>
    internal sealed class FakeInspector : IStackInspector
    {
        public const string LiquidAttribute = "testliquid";
        public const string SpoilageAttribute = "testspoilage";
        public const string TemperatureAttribute = "testtemperature";
        public static readonly Vec4f Honey = new(0.9f, 0.6f, 0.1f, 1f);

        public bool TryGetLiquid(ItemStack container, out Vec4f? color)
        {
            color = null;

            switch (container.Attributes?.GetString(LiquidAttribute))
            {
                case "honey":
                    color = Honey;
                    return true;
                case "unknown":
                    return true;
                default:
                    return false;
            }
        }

        public float? GetSpoilage(ItemStack stack, ItemSlot? slot) => stack.Attributes?.TryGetFloat(SpoilageAttribute);
    }

    private sealed class FakeResolver : IAnchorResolver
    {
        public float SlotSize => 48f;

        public bool TryGetHoveredRect(ItemSlot slot, out GuiItemRect rect)
        {
            rect = default;
            return false;
        }

        public bool TryGetSlotRect(ItemSlot slot, out GuiItemRect rect)
        {
            rect = default;
            return false;
        }

        public bool TryGetHeldRect(ItemSlot slot, out GuiItemRect rect)
        {
            rect = default;
            return false;
        }

        public bool TryGetCursorRect(out GuiItemRect rect)
        {
            rect = default;
            return false;
        }

        public void ReportTrackerError(Exception e) => throw e;
    }

    internal sealed class TestLogger : LoggerBase
    {
        public List<string> Messages { get; } = new();

        protected override void LogImpl(EnumLogType logType, string format, params object[] args)
        {
            if (logType is EnumLogType.Warning or EnumLogType.Error or EnumLogType.Fatal)
            {
                Messages.Add(args is { Length: > 0 } ? string.Format(format, args) : format);
            }
        }
    }
}
