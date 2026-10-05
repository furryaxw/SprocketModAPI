# Keybinding Registration API

[中文](keybindings-api.zh.md) | **English**

Acquire `IInputService` through `SprocketApi.TryGetService` and register an action with a stable `ActionId`.

A keybinding's stable ID is `<ModId>:<ActionId>`, where ModID is inferred by the API from the `Sprocket.Mod.Id` of the **calling assembly**; if that metadata is not declared, it falls back to the assembly name. Do not change `Sprocket.Mod.Id` or `ActionId` after release, or the user's saved bindings can no longer be matched automatically. Passing `ModId` explicitly still works (the override path).

```csharp
private IInputActionHandle? toggle;

public override void Load()
{
    if (!SprocketApi.TryGetService<IInputService>(out var input))
        return;

    toggle = input.RegisterAction(new ModActionDefinition
    {
        // ModId = "example.example-mod", // not recommended
        ActionId = "toggle-example",
        DisplayName = "Toggle example",
        Category = "Example",
        DefaultPrimary = new KeyChord("<Keyboard>/o"),
        Contexts = InputContextMask.Gameplay,
        Gate = () => panelReady
    });
    toggle.Pressed += ToggleExample;
}

public override bool Unload()
{
    toggle?.Dispose();
    return true;
}
```

Each action has a primary and a secondary key slot:

```csharp
toggle.SetBinding(0, new KeyChord("<Keyboard>/p"));
toggle.SetBinding(1, null);
toggle.RestoreDefaults();
```

**Defaults can be entirely empty**: when both `DefaultPrimary` and `DefaultSecondary` are left blank, the action is unbound right after registration (`Primary.IsEmpty`, and the UI shows `Unbound`); players bind it themselves in the keybinding window.

`KeyChord` uses Unity Input System control paths, for example `<Keyboard>/k` and `<Mouse>/middleButton`. `ModifierKeys` supports left/right Shift, Ctrl, and Alt, as well as `AnyShift`, `AnyCtrl`, and `AnyAlt`. A modifier key can be used as the primary key on its own.

Events and state queries include `Pressed`, `Released`, `Tapped`, `WasPressedThisFrame`, `WasReleasedThisFrame`, and `IsPressed`.

Context priority is `TextInput > Settings > PauseMenu > Designer > MainMenu > Gameplay > OtherMenu`. Losing focus, a scene transition, the settings page, text input, and an input block each release any pressed action once; after recovery, the action is only re-armed after a physical key release has been observed.

Modal windows should use `AcquireInputBlock` to block API actions and release the returned token when the window closes.

`IInputActionHandle.Enabled` enables or disables an action independently; both `Unregister()` and `Dispose()` remove the action. `IInputService.ActionsChanged` fires when the action list changes and can be used to refresh a mod's own settings UI.
