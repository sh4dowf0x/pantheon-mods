# Target Health Bars

Target Health Bars adds numeric health and mana text to Pantheon's offensive and defensive target health bars.

## Commands

- `/targethealth status`: show enabled state, target toggles, format, and font size.
- `/targethealth on`: enable the overlays.
- `/targethealth off`: hide the overlays.
- `/targethealth offensive on|off`: show or hide offensive target health text.
- `/targethealth defensive on|off`: show or hide defensive target health text.
- `/targethealth mana on|off`: show or hide target mana when the target exposes a mana pool.
- `/targethealth format both|percent|numbers`: change health text format.
- `/targethealth fontsize <10-32>`: change text size.

`/thb` is also accepted as a short alias.

## Config

The config file lives at:

`GameFolder\Mods\PantheonAddons\TargetHealthBarsConfig.json`

Default config:

```json
{
  "Enabled": true,
  "ShowOffensive": true,
  "ShowDefensive": true,
  "ShowMana": true,
  "Format": "both",
  "FontSize": 17,
  "TextYOffset": 0
}
```

Mana is shown only when the selected target reports `MaxMana > 0`; targets without mana keep a single HP line.
