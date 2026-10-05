# Keybinding Management

[中文](keybindings.zh.md) | **English**

## Management Window

The API watches for child-node changes under `Settings Menu/Content/Content`; once it finds `Keymapping`, it subscribes to that page's enable and disable events. The `MOD KEYBINDINGS` entry appears only while the Keymapping page is active. The entry's left edge stays aligned through RectTransform events on `Action buttons`, without per-frame hierarchy polling.

The window groups registered actions by `ModId` and offers search, scrolling, two binding slots, and per-action restore-defaults. A modal backdrop blocks mouse raycasts; while the window or a capture is active, the API snapshots the `interactable` and navigation state of every `Selectable` on the native Keymapping page, the EventSystem's current selection, and the navigation-event state, temporarily disables native interaction, and restores the snapshot on close, cancel, page deactivation, scene unload, or module disposal.

Clicking a binding slot enters capture mode:

- The target slot shows `PRESS A KEY...`.
- The window shows `LISTENING FOR INPUT | ESC TO UNBIND`.
- Pressing an ordinary key or mouse button saves the new binding.
- Pressing a modifier key first and then the main key saves a combination.
- Pressing and releasing a modifier key on its own sets that modifier as the primary key.
- Pressing `Esc` clears the current slot.
- Clicking a button in the Reset column restores that action's two default slots.

Capture ignores the mouse click that triggered the binding button, so that click is not recorded as a left mouse button.

The Primary and Secondary slots of the same action cannot hold exactly the same `KeyChord`. If both slots match after configuration load, a rebind, or restore-defaults, the API keeps Primary and clears Secondary automatically; the same primary key with a different modifier set still counts as a different binding.

## Conflict Warnings

The API builds an exact `KeyChord` index over both binding slots of every registered action and scans the currently loaded game `InputActionAsset` read-only. The management window lists all sources line by line under the action name, grouped by `Conflict:`, slot, and binding; mod sources use the action name and native sources use `GAME ActionMap/Action`.

Conflicts only warn; they do not block capture, unbind, restore-defaults, or save. The index is rebuilt immediately after action registration, unregistration, binding changes, and restore-defaults; the current native asset is re-read on scene load and each time the management window opens. The API does not enable, disable, or modify the game's `InputAction`.

Native simple bindings and composite bindings whose primary key and left/right modifiers can be resolved unambiguously take part in the index. Devices or composite bindings that cannot be mapped reliably are skipped, so no speculative conflicts are reported.

## Configuration File

The configuration is stored at:

```text
BepInEx\config\SprocketModAPI\keybindings.json
```

The file contains a schema version, an API version, and binding overrides keyed by stable action ID. The API saves only the slots that differ from the current defaults; slots without an override keep following the new defaults a later mod version provides.

An explicit clear is saved as `null`. When a mod is temporarily unloaded, the saved configuration is kept; reinstalling it under the same stable action ID restores it.

Loading requires schema version `1` and a configuration API version compatible with the current public API. If the root structure is corrupted, the schema is unsupported, or the API version is incompatible, the original file is first saved as:

```text
keybindings.json.corrupt-YYYYMMDD-HHMMSS-fff.bak
```

The API then atomically writes back a usable empty configuration, so a corrupted file does not block action registration. A single invalid action, unknown slot, or invalid slot value is logged and removed item by item, while the other valid overrides in the same file keep loading and are written back as a cleaned-up configuration.

Writes generate a `.tmp` file first and then replace the real file. The semantics of an explicit `null`, of slots without an override following the new defaults, and of retaining configuration for unregistered actions all stay unchanged.

## Input Gating

The API does not modify the game's own InputActions; it only controls mod actions registered through `IInputService`.

Context is resolved from the scene set, menu-activation observers, and the EventSystem's current selection. In Sprocket `0.2.55.5`, `Sandbox` stays the active scene in both the designer and driving states, so the API identifies `Designer` and `Gameplay` by the additive scenes `VehicleDesignerUI` and `VehicleControlUI` respectively. Settings and PauseMenu use activation-lifecycle observers, while text input checks the currently selected TMP or Unity InputField. The fixed priority is: `TextInput > Settings > PauseMenu > Designer > MainMenu > Gameplay > OtherMenu`.

Loading a scene, unloading a scene, or a context change immediately enters the transition-blocked state. Normal action dispatch resumes only after two consecutive Updates observe the same context; before it stabilizes, only action-state convergence is allowed and no new `Pressed` is produced.

The following suppress API actions:

- The game window loses focus.
- The game settings screen, text input, or the mod keybinding window is open.
- The pause menu overrides Gameplay.
- The state-cleanup phase after a scene switch.
- Any mod holds a block returned by `AcquireInputBlock`.

If an action was pressed before gating took effect, the API dispatches one `Released` and clears the held state, and `WasReleasedThisFrame` is `true` only on that frame. Regaining focus or lifting the gate does not replay the old physical key; a full release must be observed first, and only a later press produces a new `Pressed`. An exception in a single action's `Gate` disables only that action for the current frame and logs the stable action ID and the full exception without blocking other actions.

## Supported Scope

v1 supports keyboard, mouse, single keys, and exact modifier-key combinations. The data types keep Unity control paths, so other input devices can be added later without changing action IDs, but the current management window and validation scope exclude gamepads.

Conflict detection requires the same control path and the same exact modifier set. There is currently no disambiguation by scene or Action Map context, so the same native binding is shown as a warning even if it is only used in a different game context.
