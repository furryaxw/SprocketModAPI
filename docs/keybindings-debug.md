# Keybinding debug logging

On first startup the API creates `UserData/SprocketModAPI/keybindings.debug.json`:

```json
{
  "Enabled": false,
  "LogRouting": true,
  "LogBindings": true,
  "LogEveryFrame": false
}
```

Set `Enabled` to `true` and restart the game. Routing entries use the
`[SMA-KEY-ROUTE]` prefix and binding entries use `[SMA-KEY]`. Binding entries
include raw control state, modifier state, context match, action gate result,
and suppression reason. This distinguishes a key that is not reaching the
Input System from one rejected by focus, scene, context, or an input block.
