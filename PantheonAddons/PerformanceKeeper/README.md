# Performance Keeper

Performance Keeper keeps Pantheon's Unity player updating while the client is unfocused and applies separate active/background FPS targets. This is useful when running multiple local clients and the background clients need enough frame rate to keep movement and addon logic responsive.

## Commands

- `/perfmode status`: show current focus, FPS target, v-sync state, and measured average FPS.
- `/perfmode path`: show the config and JSONL sample log paths.
- `/perfmode on`: enable the performance settings.
- `/perfmode off`: disable the addon and restore the Unity settings captured when the addon loaded.
- `/perfmode bgfps <15-240>`: set the target FPS while the window is unfocused.
- `/perfmode activefps <15-240>`: set the target FPS while the window is focused.
- `/perfmode vsync on|off`: enable or disable forcing Unity v-sync off.
- `/perfmode background on|off`: enable or disable `Application.runInBackground`.
- `/perfmode sample <seconds>`: set JSONL performance sample interval.

`/perfkeeper` is also accepted as an alias.

## Output

By default, Performance Keeper writes samples to:

- `%PROGRAMDATA%\PantheonPerformance\performance-ProcessId.jsonl`

To override settings, edit `GameFolder\Mods\PantheonAddons\PerformanceKeeperConfig.json` before starting the game:

```json
{
  "OutputFolder": "C:\\ProgramData\\PantheonPerformance",
  "Enabled": true,
  "RunInBackground": true,
  "ForceVSyncOff": true,
  "ActiveFps": 60,
  "BackgroundFps": 30,
  "SampleSeconds": 5
}
```

Restart the client after changing the config. Use `/perfmode path` to confirm the active config and log paths.
