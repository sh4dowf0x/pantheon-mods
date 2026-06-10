# Combat Data

Combat Data exports Pantheon's rendered combat-log messages and structured combat-result events to live text and JSONL files.

## Commands

- `/combatdata status`: show enabled state, counters, buffer count, and file cap.
- `/combatdata path`: show text, JSONL, and config paths.
- `/combatdata clear`: clear live logs and counters.
- `/combatdata save`: save the in-memory text buffer to a timestamped snapshot.
- `/combatdata reopen`: close and reopen the live files.
- `/combatdata messages on|off`: include or skip rendered combat-log messages.
- `/combatdata structured on|off`: include or skip structured combat-result events.

## Output

By default, Combat Data writes:

- `%PUBLIC%\PantheonCombatData\combat-live-current.txt`
- `%PUBLIC%\PantheonCombatData\combat-live-current.jsonl`

The live files rotate to `.previous` when either file reaches the configured size cap.

To override the output folder, edit `GameFolder\Mods\PantheonAddons\CombatDataConfig.json` before starting the game:

```json
{
  "OutputFolder": "C:\\pantheonmods\\combat-data",
  "IncludeCombatMessages": true,
  "IncludeStructuredResults": true,
  "MaxFileMegabytes": 5
}
```

Restart the client after changing the config. `/combatdata path` shows the active output and config file locations.
