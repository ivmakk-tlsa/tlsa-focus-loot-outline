# Changelog

All notable changes to this mod are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this mod uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

## [1.0.2] - 2026-09-10

### Added

- An object the game itself cannot detect is not outlined. The game finds a lootable object through its interaction collider; when that collider is switched off, no loot prompt can ever appear for it. This catches decor copies of loot props, objects placed outside the playable map, and containers the game closes after use.
- An industrial trash can standing at or inside a military tent is not outlined. Those copies sit in a fenced yard the survivor cannot enter.
- The `DevLabels` overlay now also labels, in red, every object a skip rule keeps dark, with the rule name, so a wrongly skipped object can be reported.

### Changed

- The outline color is now one hex setting (`Color`, for example `#FFD91A`) in place of the separate `Red`, `Green`, `Blue`, and `Alpha` channels.

### Removed

- The `FuelRed`/`FuelGreen`/`FuelBlue`/`FuelAlpha` settings. Fuel cans always outline in red now, to match the game's own explosive-can highlight. The `IncludeFuel` toggle still turns the fuel highlight on or off.
- The `DepthTest` (x-ray) toggle. The outline always draws through walls now. The depth-tested mode left a large object like the player vehicle with no visible outline at all, even in direct view, so it had no good use.

### Fixed

- Mobile lighting towers placed as unreachable base decor are no longer outlined; towers that hold loot still are. The decor copy is the same prop with its interaction collider switched off, which the new detection rule reads.
- Industrial trash cans placed inside or at military tents are no longer outlined; lootable copies of the same trash can in the open are outlined again. The 1.0.1 fix excluded that trash can by name, which also hid the lootable copies.
- A battery-gated antidote dispenser stops being outlined once its antidote is taken. The game switches off its interaction collider at that point, which the new detection rule reads.
- Objects the map places outside the playable area are no longer outlined.

## [1.0.1] - 2026-09-05

### Added

- Carryable fuel cans (a carry interaction) are now outlined, in their own red color by default, to match the game's own red x-ray highlight on the explosive can. The color and an on/off toggle are configurable under `[Color]` (`FuelRed`/`FuelGreen`/`FuelBlue`/`FuelAlpha`) and `[Filter]` (`IncludeFuel`).

### Fixed

- Supply caches no longer draw stray spikes from the beacon cables or a duplicate shadow outline.
- The ground shadow decal under a survivor drop no longer outlines as a bright square.
- Decorative ivy and bushes around caches, survivor drops, and the antidote dispenser are no longer outlined, while harvestable plants still are.
- A decorative industrial-trash pile that appears only as unreachable military-camp decor is no longer outlined; lootable industrial dumpsters still are.
- Mobile lighting towers are outlined again. They were excluded as a never-lootable false positive, but they can hold loot depending on placement.

## [1.0.0] - 2026-09-04

### Added

- Outlines searchable containers and other lootable objects while focus mode is active: loot containers, corpses, sector stashes, supply caches, gated containers, pickups, crafting and utility stations, and objectives.
- Configurable outline color, glow strength, and x-ray depth test.
- Unsearched-only filter, and a per-kind toggle for each highlight group.
- Config re-reads on the next focus press, so edits apply without a restart.
