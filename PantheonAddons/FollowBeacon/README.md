# Follow Beacon

Follow Beacon is a location-sharing prototype for two Pantheon clients running on the same PC.

One client writes its local player position to a shared JSON file, and another client reads that file to display distance/delta guidance. Follower mode can optionally apply conservative movement assist when `/followbeacon assist on` is enabled.

## Commands

- `/followbeacon leader`: make this client write the shared beacon file.
- `/followbeacon follower`: make this client read the shared beacon file and show guidance.
- `/followbeacon off`: disable reading/writing.
- `/followbeacon show`: show the Follow Beacon window.
- `/followbeacon hide`: hide the Follow Beacon window.
- `/followbeacon status`: show current mode and shared file path.
- `/followbeacon path`: show the shared file path.
- `/followbeacon once`: write one leader beacon immediately.
- `/followbeacon distance <meters>`: set the desired follower distance from the leader.
- `/followbeacon input on|off`: enable or disable manual movement input awareness for `W/A/S/D` and `Q/E`.
- `/followbeacon input status`: show current/last manual input state and event count.
- `/followbeacon input path`: show the manual input event log path.
- `/followbeacon debug on|off`: show or hide raw bearing, delta, and signal age values.
- `/followbeacon probe <input> <seconds>`: manually pulse a movement input for up to 0.5 seconds.
- `/followbeacon probe stop`: stop the current movement probe.
- `/followbeacon assist on|off`: enable or disable conservative follower movement assist.
- `/followbeacon assist status`: show current assist state.

Probe inputs: `forward`, `backward`, `left`, `right`, `sprint`, `turnleft`, `turnright`.

Assist mode is disabled by default. It only runs in follower mode, only with a fresh leader beacon, and pauses when manual input is active, when the follower is within the configured follow distance, when the beacon is stale, or while a movement probe is active. The assist loop holds `turnleft`, `turnright`, and `forward`, recomputes steering from the follower's current heading every frame, and can combine `forward` with a turn input for smoother arcs.

If assist feels too twitchy or too slow, tune `Assist hold scale` in addon configuration. Lower values use shorter assist holds; higher values are more aggressive. `Assist turn deadzone` controls when turn-in-place starts, `Assist move deadzone` controls when it moves straight forward, and `Assist curve deadzone` controls when it can move forward while turning.

Assist can also add `sprint` while moving forward to catch up. By default it starts sprinting at 8m, stops sprinting at 5m, cuts sprint off at 5% endurance, and will not resume sprinting until endurance recovers to 50%.

## Follower Guidance

Follower mode shows smoothed navigation guidance based on the follower's current heading:

- primary action such as `MOVE FORWARD`, `HOLD POSITION`, or `TURN AROUND`
- turn direction and distance to leader
- manual input status and assist eligibility
- last manual movement keys and input-event count
- distance to leader
- signal age and stale warning
- hold/move/turn instruction
- relative bearing in degrees
- smoothed X/Y/Z delta

Raw bearing and delta values are hidden by default. Use `/followbeacon debug on` while tuning.

Input events are appended as JSON lines to:

`%APPDATA%\PantheonAddons\FollowBeacon\input-events.jsonl`

## Shared File

The leader writes:

`%APPDATA%\PantheonAddons\FollowBeacon\leader-location.json`

The file is written atomically through a temporary file so the follower should not read partial JSON.

To override the shared folder, edit `GameFolder\Mods\PantheonAddons\FollowBeaconConfig.json` before starting the game:

```json
{
  "BeaconFolder": "C:\\pantheonmods"
}
```

The config is read when the addon starts. Restart the client after changing it. `/followbeacon path` shows the active shared file and config file locations.
