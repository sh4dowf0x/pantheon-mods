# Combat Data

Combat Data exports Pantheon's rendered combat-log messages, structured combat-result events, and local player XP changes to live text and JSONL files.

## Commands

- `/combatdata status`: show enabled state, counters, buffer count, and file cap.
- `/combatdata path`: show text, JSONL, and config paths.
- `/combatdata clear`: clear live logs and counters.
- `/combatdata save`: save the in-memory text buffer to a timestamped snapshot.
- `/combatdata reopen`: close and reopen the live files.
- `/combatdata messages on|off`: include or skip rendered combat-log messages.
- `/combatdata structured on|off`: include or skip structured combat-result events.
- `/combatdata xp on|off`: include or skip local player experience changes.

## Output

By default, Combat Data writes:

- `%PROGRAMDATA%\PantheonCombatData\combat-live-CharacterName.txt`
- `%PROGRAMDATA%\PantheonCombatData\combat-live-CharacterName.jsonl`

Before the character is loaded, it may briefly use `combat-live-current.*`. Once the local player is detected, it switches to the character-specific file. JSONL rows also include `sourceCharacterName` and `sourceCharacterId`.

Experience rows use category `ExperienceChanged` and include `current`, `toNextLevel`, `experiencePercentage`, previous values, and delta fields. The first XP snapshot after login may have null deltas because Combat Data has no prior sample yet.

The live files rotate to `.previous` when either file reaches the configured size cap.

To override the output folder, edit `GameFolder\Mods\PantheonAddons\CombatDataConfig.json` before starting the game:

```json
{
  "OutputFolder": "C:\\pantheonmods\\combat-data",
  "IncludeCombatMessages": true,
  "IncludeStructuredResults": true,
  "IncludeExperience": true,
  "MaxFileMegabytes": 5
}
```

Restart the client after changing the config. `/combatdata path` shows the active output and config file locations.
