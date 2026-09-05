# Changelog

All notable changes to this mod are documented here. The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and this mod uses [Semantic Versioning](https://semver.org/spec/v2.0.0.html).

## [Unreleased]

### Added

- Carryable fuel cans (a carry interaction) are now outlined, in their own red color by default, to match the game's own red x-ray highlight on the explosive can. The color and an on/off toggle are configurable under `[Color]` (`FuelRed`/`FuelGreen`/`FuelBlue`/`FuelAlpha`) and `[Filter]` (`IncludeFuel`).

### Fixed

- Supply caches no longer draw stray spikes from the beacon cables or a duplicate shadow outline.
- The ground shadow decal under a survivor drop no longer outlines as a bright square.
- Decorative ivy and bushes around caches, survivor drops, and the antidote dispenser are no longer outlined, while harvestable plants still are.
- A battery-activated antidote wall dispenser stops being outlined once its antidote is taken, instead of staying highlighted after use.
- A decorative industrial-trash pile that appears only as unreachable military-camp decor is no longer outlined; lootable industrial dumpsters still are.

## [1.0.0] - 2026-09-04

### Added

- Outlines searchable containers and other lootable objects while focus mode is active: loot containers, corpses, sector stashes, supply caches, gated containers, pickups, crafting and utility stations, and objectives.
- Configurable outline color, glow strength, and x-ray depth test.
- Unsearched-only filter, and a per-kind toggle for each highlight group.
- Config re-reads on the next focus press, so edits apply without a restart.
