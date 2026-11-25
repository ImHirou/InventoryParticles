# Effect format and C# API

## Units

JSON values work like vanilla `particleProperties`, so numbers from there carry over directly:

- **Length.** 1.0 is the edge of an inventory slot on screen: `scaled(48)` pixels, 48 at GUI scale 1. An item fills most of its slot, so 0.5 from the centre is the slot's edge and 0.6 is a little past it. Sizes, speeds, spawn areas and gravity scale with the GUI.
- **Axes.** +Y is up, and `gravityEffect > 0` pulls down.
- **Color.** `hsvaColor` and `color` go from 0 to 255, as in vanilla. Hue is 0–255 too, not 0–360.
- **`NatFloat`** (`rate`, `size`, `lifeLength`, `velocity`, `gravityEffect`, `swirl`, `hang`, `hsvaColor`) must be an object: `{ "avg": 1, "var": 0.5 }`. A bare number fails to load. `"dist"` (`uniform`, `gaussian`, `invexp`…) is supported too. Without `var` the default stays; for this mod's fields it is 0.
- **Evolutions** (`sizeEvolve`, `opacityEvolve`, `redEvolve`, `greenEvolve`, `blueEvolve`) are vanilla `EvolvingNatFloat`s: `{ "transform": "linear", "factor": -150 }`.
  - The value is `transform(start, age / life)`.
  - For opacity and color, the start is the alpha or the channel (0–255): `linear -150` from alpha 150 fades the particle out by the end of its life.
  - For size, the start is a multiplier of 1: `linear 1.3` makes the particle 2.3 times as large at the end, `quadratic -0.6` shrinks it to 0.64, and `sinus` with factor 3.1416 grows it to twice its size halfway through and back.

Particles that leave the screen die early, so falling or rising particles can have a long `lifeLength`.

## Effects

Each effect is a file `assets/<domain>/config/inventoryparticles/effects/<name>.json`, and its code is `<domain>:<name>`. An effect is a list of emitters:

```json
{
    "emitters": [
        {
            "code": "steam",
            "shape": "puff",
            "rate": { "avg": 6, "var": 0 },
            "origin": [ 0, 0.2 ],
            "spawnArea": { "type": "box", "width": 0.45, "height": 0.06 },
            "color": [ 230, 230, 230, 100 ],
            "size": { "avg": 0.2, "var": 0.05 },
            "sizeEvolve": { "transform": "linear", "factor": 1.4 },
            "opacityEvolve": { "transform": "linear", "factor": -100 },
            "lifeLength": { "avg": 1.6, "var": 0.3 },
            "velocity": [ { "avg": 0, "var": 0.04 }, { "avg": 0.5, "var": 0.1 } ],
            "drag": 0.4,
            "wobble": { "amplitude": 0.04, "frequency": 0.8 },
            "fadeIn": 0.2
        }
    ]
}
```

| Field | Default | Description |
|---|---|---|
| `code` | — | Emitter name for logs |
| `shape` | `square` | `square` (vanilla quad with a soft edge), `dot`, `puff` (soft blob for smoke), `star` (twinkle), `drop` (liquid drop, pointed at the top) |
| `texture` | — | Your own texture instead of a shape, as a full path: `game:textures/particle/smoke-3.png`. White textures work best because they can be tinted |
| `blend` | `standard` | `glow` blends additively, for fire, sparks and magic |
| `layer` | `belowcursor` | `top` draws above the whole GUI, including tooltips and the stack on the cursor. `aboveitem` draws right above the item: like `belowcursor`, but above the stack on the cursor when the effect plays there. Use it for glints and flames on the item itself |
| `space` | `screen` | `anchor` makes particles move along with the item instead of leaving a trail. When the item moves between a slot, the cursor and a hand, its running effects go along: anchor-space particles keep their place on screen for a short hop and then follow, so insects fly after their item |
| `rate` | 5 | Particles per second at intensity 1 |
| `burst` | 0 | Particles spawned at once when the effect starts. Effects for the one-shot triggers only spawn their bursts |
| `minTemperature`, `maxTemperature` | — | The emitter only runs in this range, in °C (when the effect has a temperature) |
| `minIntensity` | 0 | The emitter only runs at this intensity or higher |
| `origin` | `[0, 0]` | Offset from the item's centre |
| `spawnArea` | `point` | `{ "type": "box", "width", "height" }`, `{ "type": "circle", "radius" }`, `{ "type": "ring", "radius", "innerRadius" }` |
| `hsvaColor` | — | `[hue, saturation, value, alpha]`, four `NatFloat`s |
| `color` | white | `[r, g, b]` or `[r, g, b, a]` |
| `size` | 0.1 | Edge length of a particle |
| `lifeLength` | 1 | Seconds |
| `velocity` | 0 | `[x, y]` per second |
| `gravityEffect` | 0 | Downward acceleration; negative values speed particles up upwards |
| `drag` | 0 | Fraction of the velocity lost per second. Falling particles reach `gravityEffect / drag` at most |
| `randomVelocityChange` | 0 | Random drift, like vanilla's `randomVelocityChange` |
| `swirl` | 0 | Speed around `origin`; positive turns clockwise |
| `attraction` | 0 | Pulls particles back to `origin` like a spring (acceleration = attraction × distance, per second squared). With `randomVelocityChange` and `drag` it makes insects buzz around the item |
| `wobble` | — | `{ "amplitude", "frequency" }`, a sideways sway |
| `hang` | 0 | Seconds a particle clings to the item before it moves, growing from a third of its size meanwhile, like a forming drop. It follows the item while it clings, then moves in `space` |
| `stretch` | 0 | Draws fast particles taller: the height grows by this factor per unit of vertical speed, up to four times. For falling drops |
| `fadeIn`, `fadeOut` | 0 | Seconds to fade in after spawning and to fade out before dying |

## Rules

Rules bind effects to items. Each file `assets/<domain>/config/inventoryparticles/rules/*.json` is an array:

```json
[
    {
        "code": "pot",
        "priority": 50,
        "match": { "classes": [ "BlockCookingContainer", "BlockCookedContainer" ] },
        "minTemperature": 50,
        "intensityByTemperature": { "from": 50, "to": 200, "min": 0.3, "max": 1 },
        "effects": [ "steam" ]
    },
    {
        "code": "torch",
        "priority": 100,
        "match": { "codes": [ "game:torch-*-lit-*" ] },
        "effects": [ "torchflame" ],
        "origin": [ -0.07, 0.28 ]
    },
    {
        "code": "bucket",
        "priority": 100,
        "match": { "classes": [ "BlockBucket" ] },
        "requireLiquid": true,
        "liquidTint": true,
        "effects": [ "drip" ],
        "origin": [ 0, -0.35 ]
    }
]
```

| Field | Description |
|---|---|
| `code` | Unique name. The config's `DisabledRules` refers to it |
| `priority` | Rules are checked from the highest priority to the lowest; the **first** one that fits wins |
| `match.codes` | Item and block codes with `*` (via `WildcardUtil`); a leading `@` makes a regular expression. Without a domain, `game` is assumed |
| `match.classes` | Registered class names (`BlockMeal`, `ItemIngot`…); subclasses match too. If both lists are given, both must match |
| `minTemperature`, `maxTemperature` | °C; a stack without a temperature counts as 20 °C |
| `intensityByTemperature` | The intensity changes linearly from `min` at `from` °C to `max` at `to` °C. `max` may be lower than `min` |
| `intensity` | Base intensity, 1 by default |
| `requireLiquid` | Only applies while the stack holds a liquid: a liquid container (bucket, jug, bowl…) or a watering can with water. Solids such as rot don't count |
| `liquidTint` | Multiplies `tint` with the color of the liquid as the container shows it |
| `minSpoilage` | Only applies while the stack is at least this spoiled: 0 is fresh, 1 is rotten, the "% spoiled" of its tooltip |
| `triggers` | `["hover"]`, `["cursor"]`, `["held"]`, `["custom"]`, `["pickup"]`, `["toohot"]`. Without the field, all continuous triggers; the one-shot triggers only reach rules that list them |
| `effects` | Effect codes; without a domain, the rule file's domain is used |
| `origin` | `[x, y]`, added to the `origin` of every emitter. This puts the flame on the tip of a torch |
| `scale` | Multiplies the particle sizes |
| `tint` | `[r, g, b, a]`, multipliers from 0 to 1 |

Triggers:

| Trigger | When |
|---|---|
| `hover` | The stack is in the slot under the mouse |
| `cursor` | The stack is held on the mouse cursor |
| `held` | The stack is in one of the player's hands (the active hotbar slot or the off-hand slot) and shown in the hotbar, also while playing with no inventory open. A hand slot under the mouse shows its particles once, as `hover` |
| `custom` | Another mod plays the stack's particles with `PlayItem` |
| `pickup` | One-shot: the stack was just taken onto the cursor. Its effects spawn their `burst`s once at the cursor |
| `toohot` | One-shot: the game made the player drop the stack because it is too hot to hold without tongs (vanilla does that in the hand and in the crafting grid). Its effects spawn their `burst`s once where the stack was last seen |

Your own rules can come before the built-in ones by priority. A built-in rule can be turned off with `DisabledRules`.

## C# API

### Setup

```csharp
var particles = capi.ModLoader.GetModSystem<InventoryParticlesModSystem>()?.Api;
```

**Required dependency.** Add `"dependencies": { "inventoryparticles": "1.0.0" }` to `modinfo.json` and a reference to the DLL to your csproj:

```xml
<Reference Include="InventoryParticles">
  <HintPath>libs/InventoryParticles.dll</HintPath>
  <Private>false</Private>
</Reference>
```

**Optional dependency.** Don't list the mod in `dependencies`. Use InventoryParticles types only from a separate method marked `[MethodImpl(MethodImplOptions.NoInlining)]`, and call it after checking:

```csharp
if (capi.ModLoader.IsModEnabled("inventoryparticles")) ParticlesIntegration.Init(capi);
```

All calls go on the main thread:
- registering works from your own `StartClientSide` on (InventoryParticles has `ExecuteOrder 0.1`);
- playing works once the world is loaded (`IsReady`); before that you get `ParticleHandle.Invalid`.

### Your own effect and playing it

```csharp
var code = new AssetLocation("mymod", "spark");
particles.RegisterEffect(code, new GuiParticleEffect
{
    Emitters = new[]
    {
        new GuiEmitterProps
        {
            Shape = EnumGuiParticleShape.Star,
            Blend = EnumGuiParticleBlend.Glow,
            Layer = EnumParticleLayer.AboveItem,
            Rate = NatFloat.createUniform(12, 0),
            Size = NatFloat.createUniform(0.1f, 0.03f),
            LifeLength = NatFloat.createUniform(0.6f, 0.2f),
            Color = new[] { 255, 220, 120, 255 },
            SpawnArea = new SpawnAreaProps { Type = EnumSpawnArea.Circle, Radius = 0.3f },
            FadeOut = 0.3f
        }
    }
});

// At a slot while some open dialog shows it; stops after 2 seconds.
var handle = particles.Play(code, ParticleAnchor.Slot(slot), new PlayOptions { Duration = 2f });

// A one-off burst of 20 particles at the cursor.
particles.Burst(code, ParticleAnchor.Cursor(), 20);

// At a moving point, e.g. an animated item. null means the point is hidden.
var flying = particles.Play(code, ParticleAnchor.Tracked(() => animation.Done ? null : new GuiItemRect(animation.X, animation.Y, 0f)));

particles.SetIntensity(flying, 0.5f);
particles.Stop(flying);                       // live particles finish their life
particles.Stop(handle, clearParticles: true); // particles vanish at once
```

Anchors:

| Anchor | Where it plays |
|---|---|
| `ParticleAnchor.Point(x, y, size)` | A point in screen pixels (origin top left). `size` is the unit of length; 0 means the size of a slot |
| `ParticleAnchor.Slot(slot)` | A slot, while some open dialog shows it |
| `ParticleAnchor.Cursor()` | The stack on the cursor |
| `ParticleAnchor.Tracked(func)` | The position `func` returns every frame |

A hidden anchor spawns no particles. If it stays hidden for more than 0.1 s, its old particles fade quickly (`PlayOptions.FadeWhenHidden`).

`PlayOptions`:

| Field | Sets |
|---|---|
| `Intensity` | The effect's intensity |
| `Duration` | Duration in seconds |
| `Temperature` | Temperature for the emitters' `minTemperature`/`maxTemperature` |
| `Scale` | Multiplies the particle sizes |
| `Tint` | Multiplies the colors |
| `OriginX`, `OriginY` | Moves the effect's centre |
| `Layer` | The layer for every emitter |
| `FadeWhenHidden` | Fade old particles while the anchor is hidden |
| `Trigger` | For `PlayItem` only |

### Item particles

`PlayItem(stack, anchor, options)` plays whatever the rules and providers pick for the stack and checks again every 0.25 s, e.g. for the temperature. It is meant for your own GUIs. `GetItemParticles(stack, trigger)` returns that pick without playing it.

Your own logic for picking effects:

```csharp
class GlowingSwordParticles : IItemParticleProvider
{
    public bool Collect(in ItemParticleQuery query, List<ItemParticleMatch> matches)
    {
        if (query.Stack.Collectible is not ItemGlowingSword sword) return false;
        matches.Add(new ItemParticleMatch(new AssetLocation("mymod", "spark")) { Intensity = sword.GetCharge(query.Stack) });
        return true; // don't check the rules for this stack
    }
}

particles.RegisterProvider(new GlowingSwordParticles());
```

Rules from code are the same objects as in JSON: `particles.RegisterRule(new ItemParticleRule { Code = "mymod-ruby", Match = new RuleMatch { Codes = new[] { "mymod:ruby-*" } }, Effects = new[] { "inventoryparticles:sparkle" }, Tint = new[] { 1f, 0.2f, 0.3f, 1f } })`. Registrations from code survive `.inventoryparticles reload`.

The `OnItemParticles` event fires after the providers and rules, for the hovered stack, the stack on the cursor, the stacks in hand, `PlayItem` stacks and the one-shot triggers (`args.Query.Trigger`). It can change `args.Matches` or set `args.Cancel = true`, e.g. to hide the particles of an item your mod is animating:

```csharp
particles.OnItemParticles += args =>
{
    if (args.Query.Slot != null && myAnimations.IsMoving(args.Query.Slot)) args.Cancel = true;
};
```

Useful properties:
- `HoveredRect` and `CursorRect`: where the hovered stack and the stack on the cursor were drawn in the last frame;
- `TryGetSlotRect(slot, out rect)`: where any visible slot is drawn;
- `ParticleCount`: the number of live particles;
- `EffectCodes`: the codes of all known effects.
