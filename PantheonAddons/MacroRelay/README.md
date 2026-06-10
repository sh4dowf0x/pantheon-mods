# Macro Relay

Macro Relay lets one Pantheon client send a macro request to another client through a shared JSON file.

The boxed/receiver client reads the request file, finds the named in-game macro, and runs it locally. The sender client can use chat commands or the custom Macro Relay hotbar.

## Commands

- `/macrorelay list`: list visible macros on this client.
- `/macrorelay receiver on`: allow this client to receive and run relay requests.
- `/macrorelay receiver off`: stop receiving relay requests.
- `/macrorelay send <macro name>`: send a macro request to the shared relay file.
- `/macrorelay run <macro name>`: run a local macro directly.
- `/macrorelay hotbar show`: show the Macro Relay hotbar.
- `/macrorelay hotbar hide`: hide the Macro Relay hotbar.
- `/macrorelay hotbar reload`: reload hotbar configuration.
- `/macrorelay status`: show receiver/listener state.
- `/macrorelay path`: show the active request file and config file locations.

## Hotbar

- Left-click a slot to send its configured macro request.
- Right-click a slot to edit its label, macro name, and hotkey.
- Use the power button to enable or disable hotkey listening.
- Use `+` and `-` to show more or fewer slots.
- Slots are remembered even when hidden with `-`.
- After 12 visible slots, the hotbar starts a second column.

## Shared File

By default, Macro Relay writes requests under:

`%PUBLIC%\PantheonMacroRelay`

For Sandboxie, two-computer setups, or custom sharing, edit `MacroRelayConfig.json` and set `RelayFolder` to a folder both clients can read and write.
