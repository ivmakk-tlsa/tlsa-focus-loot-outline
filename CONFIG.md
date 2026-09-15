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
| `Color` | `#F9E37E` | hex `#RRGGBB` or `#RRGGBBAA` | Outline color for loot, stations, and objectives. The default is pale warm gold; carryables and dangers have separate colors below. Add two more hex digits for alpha (`#RRGGBBAA`); without them the outline is fully opaque. |
| `CarryableColor` | `#1ACC0D` | hex `#RRGGBB` or `#RRGGBBAA` | Outline color for carryable fuel cans and supply bags. Green by default; controlled by `IncludeFuel`. |
| `Strength` | `1.0` | any | Glow strength for object outlines, including carryables and dangers. Higher makes them brighter. Ground discs use `RingStrength`. |
| `FocusSaturation` | `0.55` | `0.3` - `1` | Least screen color saturation while focus is active. Focus mode desaturates the whole screen (to about 0.3), which washes outline colors toward white; this raises it back so highlight colors stay readable. 1 is full color; lower toward 0.3 restores the game's desaturated focus look. |
| `DangerColor` | `#FF2020` | hex `#RRGGBB` or `#RRGGBBAA` | Color for danger outlines and ground discs: burning ground, acid and infection clouds or puddles, explosive props, traps, and mines. Red by default; controlled by `IncludeDanger`. |

Carryable fuel cans and supply bags share `CarryableColor`. Explosive tanks and barrels use `DangerColor`. Stationary supply caches use `Color` and the separate `IncludeCaches` toggle.

Existing `Color` values are preserved when upgrading; set `Color = #F9E37E` to use the new default. Invalid hex colors fall back to the corresponding default.

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
| `IncludeFuel` | `true` | `true` / `false` | Highlight carryable fuel cans and supply bags using `CarryableColor`. The key keeps its original name for compatibility; it controls both kinds of carryable. |
| `IncludeDanger` | `true` | `true` / `false` | Highlight active hazards using `DangerColor`: burning ground, acid and infection clouds or puddles, explosive props, traps, and mines. Ground hazards and placed Box Mines show discs; buried proximity mines highlight their device mesh only. |
| `IncludeStations` | `true` | `true` / `false` | Highlight crafting and utility stations (workbench, campfire, fire barrel, merchant, supply store, upgrades, shrine). |
| `IncludeObjectives` | `true` | `true` / `false` | Highlight objectives and misc interactions (power generator, books, XP interactions). |

## Danger

Ground hazards use flat, filled discs on the ground. A placed Box Mine also shows a disc at its blast radius; buried proximity mines and explosive props highlight only their meshes. A fire station without a usable mesh gets a small disc in the station color.

The settings retain their `Ring` names, but the markers are discs. They are visual guides: small hazard areas are enlarged for visibility.

| Setting | Default | Values | What it does |
|---|---|---|---|
| `RingMinRadius` | `0.75` | `0.25` - `3` | Minimum ground-hazard disc radius in metres; also the radius of a fire station's fallback disc. Does not change a Box Mine's blast-radius disc. Applies to newly built discs; restart to rebuild existing ones. |
| `RingStrength` | `0.35` | `0` - `5` | Glow strength for ground discs, including fire-station fallback discs, separate from `Strength`. Lower makes them fainter. |

## Performance

| Setting | Default | Values | What it does |
|---|---|---|---|
| `AttachPerFrame` | `4` | `1` - `32` | How many objects get their outline per frame after focus starts. Lower is smoother but lights up more slowly. |

## Diagnostics

| Setting | Default | Values | What it does |
|---|---|---|---|
| `DevLabels` | `false` | `true` / `false` | Developer overlay that draws each highlighted object's internal name on screen, to report a wrongly highlighted prop. Keep off in normal play. |
