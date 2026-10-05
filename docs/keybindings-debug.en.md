# Keybinding Self-Troubleshooting

[中文](keybindings-debug.zh.md) | **English**

This guide helps diagnose keybindings on mods that rely on Sprocket Mod API when they fail to trigger, trigger only in some scenes, or behave differently from the expected key combination.

## Before you start

Before troubleshooting, update the affected mods to the current version through the mod manager.

If you need to send logs to the developer, open the mod manager's **Settings** page, find the **BepInEx** section, and click **Upload log**. After you confirm the public upload, the manager reads up to the final 8 MiB of `BepInEx\LogOutput.log`, uploads the raw text, and shows a link you can copy; include that link as well. Uploading never happens automatically, and the log is not redacted automatically.

## Enable debugging

Open the in-game mod menu (the `MODS` button at the bottom left of **Settings → General**), open the `Sprocket Mod API` config page, and switch on `Keybinding debug log` under the `Keybinding diagnostics` section. `Keybinding debug: routing` controls whether routing entries are written, and `Keybinding debug: bindings` controls whether action entries are written.

```json
{
  "Enabled": true,
  "LogRouting": true,
  "LogBindings": true,
  "LogEveryFrame": false
}
```

The switches are re-read before each write, so a change takes effect immediately.

Keep `Keybinding debug: every frame` off by default. Turn it on only when the developer asks, because the log volume grows noticeably.

## Recommended reproduction

1. Confirm the affected mods are updated to the latest version.
2. Enter the scene where the key should work, such as `Gameplay` while driving.
3. Make sure settings, text fields, pause menus, and the mod keybinding window are all closed.
4. Release all Shift, Ctrl, and Alt keys, then tap the target key once by itself.
5. On the mod manager's **Settings** page, click **Upload log** in the **BepInEx** section, confirm the public upload, and copy the generated link.
6. Tell the mod author the key pressed, the scene, and the expected action, and include the share link.

## Reading the log

### Routing entries

```text
[SMA-KEY-ROUTE] scene=Sandbox, context=Gameplay, stable=True,
focused=True, externalBlock=False, blocks=0
```

This is the normal state for dispatching an ordinary Gameplay action. The fields mean:

- `context=Gameplay`: the action is currently in a game context that allows it; an action may instead allow only `Designer`, `MainMenu`, or another context.
- `stable=True`: the two-frame stabilization period after a scene or menu transition has ended.
- `focused=True`: the game window has keyboard focus.
- `externalBlock=False` and `blocks=0`: no mod window or other mod is blocking API input.

The following states deliberately suppress the action: `focused=False`, `context=Settings`, `context=TextInput`, `externalBlock=True`, a non-zero `blocks` value, or `stable=False`. Close the relevant window, release the old key, and press it again to confirm recovery.

### Action entries

```text
[SMA-KEY] action=example:toggle, primaryRaw=True, secondaryRaw=False,
physical=False, enabled=True, context=Gameplay, contextMatch=True,
allowed=True, modifiers=LeftShift, primary=<keyboard>/l|0
```

- `primaryRaw=True` or `secondaryRaw=True`: Unity's Input System received the target main key; this proves the input really reached the API.
- `physical=True`: the main key and modifiers match the binding, so the action can proceed to the dispatch decision.
- `enabled=True`: the action has not been disabled by its owning mod.
- `contextMatch=True`: the action allows the current context.
- `allowed=True`: the owning mod's optional `Gate` allows the action.

An action actually fires only when both `physical=True` and `allowed=True` hold and routing is in a dispatchable state.

## Common results

| Log pattern | Meaning | Self-service action |
| --- | --- | --- |
| No `[SMA-KEY-ROUTE]` entries at all | The mod never reached input update, or the log is not from this run | Update the mod, restart the game, and upload a fresh `LogOutput.log` |
| Routing entries but no target `action=` | The mod did not register that action, or its main key was not pressed | Confirm the mod is enabled and check the binding again in the keybinding manager |
| `primaryRaw=True` but `physical=False` | The main key arrived, but the modifier set does not match | Release Shift/Ctrl/Alt and retry, or re-record the combination you actually want in the keybinding manager |
| `primary=<keyboard>/l|0` with `modifiers=LeftShift` | The binding is `L`, but `Shift+L` was actually pressed | Press `L` alone; if you want `Shift+L`, record `LeftShift+L` |
| `contextMatch=False` | The action is not allowed in the current scene or menu context | Use the action in a scene the mod defines, or contact the mod author to change the action's context |
| `allowed=False` | The mod's own Gate rejected the action | Check that mod's prerequisites; this is not lost API input |
| `suppressed=true` | The API is being suppressed by focus, the settings page, text input, a window, or an input block | Close the UI, return to the game window, release the keys first, and retry |

## Modifier rules

Bindings match on the **exact modifier combination**. An unmodified `L` binding matches only `L`; `Shift+L`, `Ctrl+L`, and `Alt+L` are all different bindings. Left and right Shift, Ctrl, and Alt are also tracked separately.

For a combination, record it in the in-game `MOD KEYBINDINGS` by holding the modifier first and then pressing the main key. Do not rely on uppercase letters: the Input System treats `Shift+L` as a combination, not as a plain `L`.

## What to send the developer

- The `LogOutput.log` share link generated by the mod manager.
- The configured binding, the actual key pressed (including left/right Shift/Ctrl/Alt), the scene, and the expected behavior.
- Whether it happens only in the settings page, the designer, while driving, in a pause menu, or after focus is lost.
