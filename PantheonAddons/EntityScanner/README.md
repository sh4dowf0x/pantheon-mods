# Pantheon Entity Scanner

Exports nearby player, NPC, ground-spawn, and local-character snapshots to JSONL for external parsers, radar overlays, or database import.

## Install

Copy these files into the game folder:

- `Mods/PantheonAddonLoader.dll`
- `UserLibs/PantheonAddonFramework.dll`
- `Mods/PantheonAddons/EntityScanner.dll`
- optional: `Mods/PantheonAddons/EntityScannerConfig.json`

## Output

Default output folder:

`%PROGRAMDATA%\PantheonEntityScanner`

Live file:

`entities-live-CharacterName.jsonl`

Before the character is loaded, it may briefly use `entities-live-current.jsonl`. Once the local player is detected, it switches to the character-specific file.

## Commands

- `/entityscan status`
- `/entityscan path`
- `/entityscan clear`
- `/entityscan save`
- `/entityscan reopen`
- `/entityscan players on|off`
- `/entityscan npcs on|off`
- `/entityscan ground on|off`
- `/entityscan local on|off`
- `/entityscan distance <meters|0>`

Ground spawns are discovered by a once-per-second scan of loaded `NetworkWorldItem` objects rather than by patching the game's fragile environment lifecycle. They are written with `EntityType` set to `GroundSpawn`.
