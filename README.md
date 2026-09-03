# Focus Loot Outline

A mod for [*The Last Stand: Aftermath*](https://www.nexusmods.com/thelaststandaftermath) that outlines searchable containers and lootable interactables while focus mode is active, so you can spot them faster. Loot boxes, sector stashes, supply caches, gated containers, pickups, crafting and utility stations, and objectives light up with a colored outline the moment you enter focus, and go dark when you leave it.

It uses the game's own outline pipeline (the same one that outlines zombies), so the highlight looks native. Highlighting is event-driven, with no per-frame work: a container lights when it spawns or when focus starts, not on a timer.

Everything is configurable in `BepInEx\config\com.ivmakk.tlsa.focuslootoutline.cfg`: the outline color and glow strength, whether the outline draws through walls (x-ray) or is occluded, an unsearched-only filter, and a per-kind toggle for each highlight group (stashes, caches, gated, tool-gated, pickups, stations, objectives). The config re-reads on the next focus press, so a color edit takes effect without a restart.

## Install

1. Install [BepInEx 6 (IL2CPP)](https://www.nexusmods.com/thelaststandaftermath/mods/1) for The Last Stand: Aftermath. Start the game once so BepInEx finishes setup, then quit.
2. Extract this mod's zip into the game folder (the folder with the game .exe). The DLL lands in `BepInEx\plugins`. Full path examples:
   - Steam: `C:\Program Files (x86)\Steam\steamapps\common\The Last Stand Aftermath\BepInEx\plugins\FocusLootOutline.dll`
   - Epic: `C:\Program Files\Epic Games\The Last Stand Aftermath\BepInEx\plugins\FocusLootOutline.dll`
3. Start the game. Enter focus mode near loot and containers now outline.

Not working? Open `BepInEx\LogOutput.log` and look for the `Focus Loot Outline loaded` line.

## Uninstall

Delete `FocusLootOutline.dll` from the `BepInEx\plugins` folder.

## Build

This is a BepInEx 6 IL2CPP plugin. It compiles against the game's IL2CPP interop assemblies, so a working game install with BepInEx 6 set up is required. Those assemblies are game-derived and are not part of this repo.

```
dotnet build src/FocusLootOutline.csproj -c Release
```

`Directory.Build.props` sets `GameDir` to the default Steam install path. If the game lives elsewhere, override it without editing the file: set a `GameDir` environment variable, or pass `-p:GameDir=...` on the build. The output DLL is at `src\bin\Release\FocusLootOutline.dll`.

## Package

Add `-p:Package=true` to a Release build to also produce the ready-to-install zip at `dist\FocusLootOutline-<version>.zip`, laid out as `BepInEx\plugins\FocusLootOutline.dll` so a user extracts it at the game root. A plain build skips this step.

```
dotnet build src/FocusLootOutline.csproj -c Release -p:Package=true
```

## License

Licensed under the GNU General Public License v3.0. Copyright (C) 2026 ivmakk. See [LICENSE](LICENSE).

You may reuse and modify this mod, but you must keep it open under the same license and give credit. Do not reupload it without credit.
