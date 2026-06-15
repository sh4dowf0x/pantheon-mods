# Distributions

This folder contains ready-to-install zip packages:

| Package | Addon DLL | Purpose |
| --- | --- | --- |
| `PantheonAllMods.zip` | all current addon DLLs | Installs every packaged addon at once. |
| `PantheonAutoFollowMod.zip` | `FollowBeacon.dll` | Shares leader position between clients and shows follower guidance or optional conservative follow assist. |
| `PantheonCombatDataMod.zip` | `CombatData.dll` | Exports combat messages, structured combat events, and XP changes. |
| `PantheonEntityScannerMod.zip` | `EntityScanner.dll` | Exports player, NPC, ground-spawn, and local-character snapshots. |
| `PantheonLootDataMod.zip` | `LootData.dll` | Exports inventory item changes, item snapshots, and loot-like chat lines. |
| `PantheonMacroRelayMod.zip` | `MacroRelay.dll` | Relays named macro requests between clients through a shared file and optional hotbar. |
| `PantheonPerformanceKeeperMod.zip` | `PerformanceKeeper.dll` | Keeps unfocused clients updating with separate active/background FPS targets. |
| `PantheonTargetHealthBarsMod.zip` | `TargetHealthBars.dll` | Adds numeric health and mana text to target bars. |

Use `PantheonAllMods.zip` to download every current split mod package at once, or use an individual zip when you only want one mod.

Each zip contains:

- the shared `PantheonAddonLoader.dll`
- the shared `PantheonAddonFramework.dll`
- one standalone addon DLL
- that addon's config file
- install notes

Extract the zip into the Pantheon game folder so the zip's `GameFolder` contents merge with the game folder. Addon DLLs belong in `GameFolder\Mods\PantheonAddons`; shared loader files belong in `GameFolder\Mods` and `GameFolder\UserLibs`.

Addon DLLs now install into `GameFolder\Mods\PantheonAddons`. Remove older `%APPDATA%\PantheonAddons\PantheonAddons.dll`, `%APPDATA%\PantheonAddons\FollowBeacon.dll`, `%APPDATA%\PantheonAddons\MacroRelay.dll`, `%APPDATA%\PantheonAddons\EntityScanner.dll`, `%APPDATA%\PantheonAddons\CombatData.dll`, `%APPDATA%\PantheonAddons\LootData.dll`, `%APPDATA%\PantheonAddons\PerformanceKeeper.dll`, or `%APPDATA%\PantheonAddons\TargetHealthBars.dll` files before installing these split packages.
