# Pantheon Entity Scanner

Exports nearby player, NPC, and local-character snapshots to JSONL for external parsers or database import.

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
- `/entityscan local on|off`
- `/entityscan distance <meters|0>`
