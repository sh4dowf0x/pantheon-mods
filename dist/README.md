# Distributions

This folder contains ready-to-install zip packages:

- `PantheonAutoFollowMod.zip`
- `PantheonCombatDataMod.zip`
- `PantheonEntityScannerMod.zip`
- `PantheonLootDataMod.zip`
- `PantheonMacroRelayMod.zip`
- `PantheonPerformanceKeeperMod.zip`
- `PantheonTargetHealthBarsMod.zip`

Each zip contains:

- the shared `PantheonAddonLoader.dll`
- the shared `PantheonAddonFramework.dll`
- one standalone addon DLL
- that addon's config file
- install notes

Addon DLLs now install into `GameFolder\Mods\PantheonAddons`. Remove older `%APPDATA%\PantheonAddons\PantheonAddons.dll`, `%APPDATA%\PantheonAddons\FollowBeacon.dll`, `%APPDATA%\PantheonAddons\MacroRelay.dll`, `%APPDATA%\PantheonAddons\EntityScanner.dll`, `%APPDATA%\PantheonAddons\LootData.dll`, `%APPDATA%\PantheonAddons\PerformanceKeeper.dll`, or `%APPDATA%\PantheonAddons\TargetHealthBars.dll` files before installing these split packages.
