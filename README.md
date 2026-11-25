# Inventory Particles

Client-side mod for Vintage Story 1.23 that adds particles to items in the GUI: the hovered item, the stack on the cursor, and items held in hand (shown in the hotbar). Not needed on the server.

## Config

`ModConfig/inventoryparticles.json`:

| Field | Default | Description |
|---|---|---|
| `Enabled` | `true` | Master switch, also covers particles from other mods |
| `ShowOnHover` / `ShowOnCursor` / `ShowInHotbar` | `true` | Toggle each source of particles |
| `Density` | `1` | Particle count multiplier (0–10) |
| `Size` | `1` | Particle size multiplier (0.1–10) |
| `MaxParticles` | `512` | Live particle limit |
| `DisabledRules` | `[]` | Rule codes to skip, e.g. `["hotmetal"]` |
| `DisabledEffects` | `[]` | Effect codes to never play, e.g. `["steam"]` |

## Commands

`.inventoryparticles <subcommand>`:

| Subcommand | Description |
|---|---|
| `reload` | Reload config, effects and rules |
| `list` | List effects and active rules |
| `play <effect> [seconds]` | Play an effect at the mouse cursor |
| `debug` | Toggle debug overlay and rule logging |
| `heat <°C>` | Set the held stack's temperature (client only) |

## Modding

- **JSON:** `assets/<domain>/config/inventoryparticles/effects/*.json` and `.../rules/*.json` in any mod.
- **C# API:** register effects and rules, play effects at a slot, the cursor or any screen point.

See [docs/api.md](docs/api.md).

## Build

Set `VINTAGE_STORY` to the game folder.

```bash
./build.sh                                     # Releases/inventoryparticles_<version>.zip
dotnet run --project InventoryParticles.Tests  # smoke tests
```
