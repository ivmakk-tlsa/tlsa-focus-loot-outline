# Configuration

Every setting lives in `BepInEx\config\com.ivmakk.tlsa.focuslootoutline.cfg`, written the first time you run the game with the mod installed. Edit it with any text editor, then restart the game to apply the change.

## General

| Setting | Default | Values | What it does |
|---|---|---|---|
| `Enabled` | `true` | `true` / `false` | Turns the whole focus highlight on or off. |
| `Verbose` | `false` | `true` / `false` | Writes extra diagnostic lines to the BepInEx log. Turn on only to troubleshoot an object that does not highlight. |

## Color

| Setting | Default | Values | What it does |
|---|---|---|---|
| `Color` | `#FFD91A` | hex `#RRGGBB` or `#RRGGBBAA` | Outline color. The default is a warm yellow-gold. Add two more hex digits for alpha (`#RRGGBBAA`); without them the outline is fully opaque. |
| `Strength` | `1.0` | any | Glow edge strength. Higher makes the outline edge brighter. |
| `DangerColor` | `#FF2020` | hex `#RRGGBB` or `#RRGGBBAA` | Outline color for danger objects: burning ground, acid and infection puddles, gas tanks, traps, and a placed box mine with a ring at its blast radius. |

Fuel cans always outline in red, to match the game's own highlight on the explosive can. Turn that highlight on or off with `IncludeFuel` in the Filter section.

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
| `IncludeDanger` | `true` | `true` / `false` | Highlight danger objects in the danger color while focus is active: burning ground, acid and infection puddles, gas tanks that can explode, traps, and a placed box mine with its blast-radius ring. |
| `IncludeStations` | `true` | `true` / `false` | Highlight crafting and utility stations (workbench, merchant, supply store, upgrades, shrine). |
| `IncludeObjectives` | `true` | `true` / `false` | Highlight objectives and misc interactions (power generator, books, XP interactions). |

## Danger

A burning ground spot has no mesh of its own (its flames are particles), so the mod draws a low ring wall around it instead of an outline, and the same ring marks the blast radius of a placed box mine. These settings shape that ring.

| Setting | Default | Values | What it does |
|---|---|---|---|
| `RingHeight` | `0` | `0` - `1` | `0` draws a flat band on the ground, like a puddle. Above `0` draws a wall of that height in metres (tall reads as a solid tube). Applies to rings built after the change (next scene load). |
| `RingWidth` | `3` | `0.02` - `3` | Width of the flat band in metres (when `RingHeight` is `0`). At or above the radius the band is a filled disc, like a puddle (the default). Smaller, such as `0.15`, draws a hollow ring. Applies to rings built after the change. |
| `RingMinRadius` | `0.75` | `0.25` - `3` | Smallest ring radius in metres. A burning spot's damage area is smaller than its flames, so the ring is floored to this. Applies to rings built after the change. |
| `RingStrength` | `0.35` | `0` - `5` | Glow strength of the ring, separate from `Strength`. Lower is a fainter, thinner line. Applies on the next focus press. |

## Performance

| Setting | Default | Values | What it does |
|---|---|---|---|
| `AttachPerFrame` | `4` | `1` - `32` | How many objects get their outline per frame after focus starts. Lower is smoother but lights up more slowly. |

## Diagnostics

| Setting | Default | Values | What it does |
|---|---|---|---|
| `DevLabels` | `false` | `true` / `false` | Developer overlay that draws each highlighted object's internal name on screen, to report a wrongly highlighted prop. Keep off in normal play. |
