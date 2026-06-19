# Loot Data

Loot Data is a probe addon for building item and loot databases. It exports item add/remove events, acquisition source clues, full available item snapshots, and loot-like chat lines to JSONL.

## Commands

- `/lootdata status`: show enabled state and event counters.
- `/lootdata path`: show event and config paths.
- `/lootdata clear`: clear the live event file and counters.
- `/lootdata reopen`: close and reopen the live file.
- `/lootdata inventory on|off`: include or skip inventory add/remove events.
- `/lootdata chat on|off`: include or skip loot-like chat messages.
- `/lootdata raw on|off`: include or skip the full raw item/template object graph.
- `/lootdata icons on|off`: export one PNG per unique item icon key when the icon sprite can be resolved.
- `/lootdata iconprobe on|off`: log sprite lookup failures for icon export troubleshooting.
- `/lootdata snapshot`: write a snapshot of every currently visible inventory item.

## Output

By default, Loot Data writes:

`C:\ProgramData\PantheonLootData\loot-events-current.jsonl`

`item_added` records include an `acquisition` object when source clues are available. Loot Data correlates the item's `corpseId`, recently seen NPC/entity snapshots, recent offensive target state, and matching loot chat lines. The `method`, `confidence`, and `evidence` fields explain how the source was chosen, so downstream importers can prefer high-confidence corpse/entity matches and review lower-confidence target or chat matches.

Writes are guarded by a shared process lock, so multiple clients can append to the same JSONL file without interleaving records.

To override the output folder, edit `GameFolder\Mods\PantheonAddons\LootDataConfig.json` before starting the game:

```json
{
  "OutputFolder": "C:\\pantheonmods\\loot-data",
  "IncludeInventoryEvents": true,
  "IncludeLootChat": true,
  "IncludeRawDump": false,
  "ExportIcons": true,
  "IconProbe": false,
  "IconOutputFolder": "C:\\pantheonmods\\loot-data\\icons",
  "MaxFileMegabytes": 5
}
```

Restart the client after changing the config. `/lootdata path` shows the active output and config file locations.

Icon export is enabled by default. Loot events include an `icon` object with the item's `iconKey`, the deduplicated relative `iconFile`, and an export status. PNGs are only written once per unique icon key. Loot Data also writes `loot-icons-current.jsonl` beside the main event log so importers can build an icon-key-to-file mapping without scanning every loot event.
