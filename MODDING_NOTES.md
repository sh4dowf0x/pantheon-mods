# Pantheon Addon Exploration Notes

## Current framework shape

This repository is an experimental, third-party C# addon framework for Pantheon: Rise of the Fallen. It uses MelonLoader and Harmony in the loader, while addon authors consume the smaller `PantheonAddonFramework` API.

The public addon API is mostly focused on runtime UI changes and readonly game-state observation:

- Create draggable custom UI windows with text and image components.
- Modify supported existing UI panels, currently XP bar and offensive/defensive target pool bars.
- Read local/player metadata, XP, inventory item names, currencies, stats, and target health percentages.
- Listen for lifecycle, chat, player, local-player, inventory, XP, target, and window movement events.
- Add client-side chat/info messages.
- Register custom slash commands.
- Poll keyboard input.
- Read visible macro buttons and activate an existing macro.
- Add addon configuration controls in the settings UI.

## Clear boundaries

The README describes the API as readonly and UI-oriented. The framework does not aim to automate gameplay, alter gameplay state, modify game files, or draw outside the game UI.

Within normal addon code, we should treat these as out of scope:

- Botting, rotation automation, or automatic key presses.
- Reading hidden/private server state that is not exposed through the framework.
- Changing combat, movement, drops, spawn behavior, inventory contents, or network messages.
- Writing to game installation files.

Extending the loader itself could technically expose more through new Harmony hooks, but that should be handled as framework research and reported carefully, especially if it crosses gameplay or fair-play boundaries.

## Good first experiments

- Addon Lab: a small event-probe window and `/addonlab` slash command for seeing what the framework observes live.
- Chat helper: timestamp, filter, or highlight messages locally.
- Inventory watcher: show recent added/removed items, loot history, or simple session summaries.
- XP/session tracker: XP gained this session, percent/hour, time-to-level estimate.
- Target readability: clearer target percentage, color cues, or bigger text.
- UI layout tools: snap-to-grid, alignment guides, window position logger.
- Macro helper: list visible macro names, optional manual hotkey-to-existing-macro trigger.
- Resource tracker: show selected local player stats in a compact custom window if the public stat values are reliable.

## Current workspace status

- The framework zip has been unpacked under `PantheonAddons-master/PantheonAddons-master`.
- `PantheonAddons/AddonLab/AddonLab.cs` has been added as a practical test addon inside the existing example addon project.
- .NET SDK 6.0.428 is installed, and `dotnet build PantheonAddons/PantheonAddons.csproj` succeeds.
- `GamePath` in `Directory.Build.props` points to the PTR game root: `C:/PantheonPTR/App`.
- MelonLoader v0.7.3 x64 was manually installed into `C:/PantheonPTR/App` from the official LavaGang GitHub release zip.
- MelonLoader generated `MelonLoader/Il2CppAssemblies` under the PTR `GamePath` after first launch.
- `dotnet build PantheonAddonLoader/PantheonAddonLoader.csproj` succeeds after qualifying `UnityEngine.Bindings.ManagedSpanWrapper` in `CustomAssetManager`.
- The addon build copied `PantheonAddons.dll` to `%APPDATA%/PantheonAddons` and `PantheonAddonFramework.dll` to `C:/PantheonPTR/App/UserLibs`.
- The loader build copied `PantheonAddonLoader.dll` to `C:/PantheonPTR/App/Mods`.

## Addon Lab commands

- `/addonlab help`: show available commands.
- `/addonlab show`: show the lab window.
- `/addonlab hide`: hide the lab window.
- `/addonlab clear`: clear the lab log.
- `/addonlab player`: add a note about event-driven player data.
- `/addonlab macros`: list visible macro buttons that the framework can see.
