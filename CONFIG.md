# Configuration

Every setting lives in `BepInEx\config\com.ivmakk.tlsa.focuslootoutline.cfg`, written the first time you run the game with the mod installed. Edit it with any text editor. The config re-reads on your next focus press, so a change takes effect without a game restart.

Color channels (`Red`, `Green`, `Blue`, `Alpha`, and the `Fuel*` set) run from `0.0` to `1.0`.

## General

| Setting | Default | Values | What it does |
|---|---|---|---|
| `Enabled` | `true` | `true` / `false` | Turns the whole focus highlight on or off. |
| `Verbose` | `false` | `true` / `false` | Writes extra diagnostic lines to the BepInEx log. Turn on only to troubleshoot an object that does not highlight. |

## Color

The default outline color is a warm yellow-gold. Fuel cans use a separate color, red by default, to match the game's own highlight on the explosive can.

| Setting | Default | Values | What it does |
|---|---|---|---|
| `Red` | `1.0` | `0.0` - `1.0` | Red channel of the main outline color. |
| `Green` | `0.85` | `0.0` - `1.0` | Green channel of the main outline color. |
| `Blue` | `0.1` | `0.0` - `1.0` | Blue channel of the main outline color. |
| `Alpha` | `1.0` | `0.0` - `1.0` | Outline opacity. Lower is more see-through. |
| `Strength` | `1.0` | any | Glow edge strength. Higher makes the outline edge brighter. |
| `FuelRed` | `1.0` | `0.0` - `1.0` | Red channel of the fuel-can outline color. |
| `FuelGreen` | `0.0` | `0.0` - `1.0` | Green channel of the fuel-can outline color. |
| `FuelBlue` | `0.0` | `0.0` - `1.0` | Blue channel of the fuel-can outline color. |
| `FuelAlpha` | `1.0` | `0.0` - `1.0` | Fuel-can outline opacity. |

## Visibility

| Setting | Default | Values | What it does |
|---|---|---|---|
| `DepthTest` | `false` | `true` / `false` | `false` draws the outline through walls (x-ray). `true` lets walls hide the outline. |

## Filter

`OnlyUnsearched` controls which containers light up. The `Include*` toggles turn each group of highlights on or off.

| Setting | Default | Values | What it does |
|---|---|---|---|
| `OnlyUnsearched` | `true` | `true` / `false` | Highlight only objects not yet searched or emptied. Set `false` to keep searched ones lit too. |
| `IncludeStashes` | `true` | `true` / `false` | Highlight sector stashes. |
| `IncludeCaches` | `true` | `true` / `false` | Highlight supply caches. |
| `IncludeGated` | `true` | `true` / `false` | Highlight battery- and item-gated interactables (antidote dispensers, containers that need a battery). |
| `IncludeToolGated` | `true` | `true` / `false` | Highlight tool-gated interactables that need a tool to unlock. |
| `IncludePickups` | `true` | `true` / `false` | Highlight loose loot and pickups (ground items, survivor drops, tool rewards). |
| `IncludeFuel` | `true` | `true` / `false` | Highlight carryable fuel cans, in the fuel color. |
| `IncludeStations` | `true` | `true` / `false` | Highlight crafting and utility stations (workbench, merchant, supply store, upgrades, shrine). |
| `IncludeObjectives` | `true` | `true` / `false` | Highlight objectives and misc interactions (power generator, books, XP interactions). |

## Performance

| Setting | Default | Values | What it does |
|---|---|---|---|
| `AttachPerFrame` | `4` | `1` - `32` | How many objects get their outline per frame after focus starts. Lower is smoother but lights up more slowly. |

## Diagnostics

| Setting | Default | Values | What it does |
|---|---|---|---|
| `DevLabels` | `false` | `true` / `false` | Developer overlay that draws each highlighted object's internal name on screen, to report a wrongly highlighted prop. Keep off in normal play. |
