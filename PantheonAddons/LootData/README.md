# Loot Data

Loot Data is a probe addon for building item and loot databases. It exports item add/remove events, full available item snapshots, and loot-like chat lines to JSONL.

## Commands

- `/lootdata status`: show enabled state and event counters.
- `/lootdata path`: show event and config paths.
- `/lootdata clear`: clear the live event file and counters.
- `/lootdata reopen`: close and reopen the live file.
- `/lootdata inventory on|off`: include or skip inventory add/remove events.
- `/lootdata chat on|off`: include or skip loot-like chat messages.
- `/lootdata raw on|off`: include or skip the full raw item/template object graph.
- `/lootdata snapshot`: write a snapshot of every currently visible inventory item.

## Output

By default, Loot Data writes:

`C:\ProgramData\PantheonLootData\loot-events-current.jsonl`

To override the output folder, edit `GameFolder\Mods\PantheonAddons\LootDataConfig.json` before starting the game:

```json
{
  "OutputFolder": "C:\\pantheonmods\\loot-data",
  "IncludeInventoryEvents": true,
  "IncludeLootChat": true,
  "IncludeRawDump": false,
  "MaxFileMegabytes": 5
}
```

Restart the client after changing the config. `/lootdata path` shows the active output and config file locations.
