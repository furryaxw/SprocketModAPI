# UI Registration API

[中文](ui-api.zh.md) | **English**

After acquiring `IUiService`, create an owner scope for the mod and release that scope when the mod unloads.

```csharp
if (!SprocketApi.TryGetService<IUiService>(out var ui))
    return;

var scope = ui.CreateScope(new UiOwnerDefinition { ModId = "example.example-mod" });
```

`UiCapabilitySnapshot.Available` reports the currently available capabilities; check it with `Supports(UiCapability.MenuButton)`. Creation failures come back through `UiCreateResult<T>`: check `Succeeded`, read `Value` on success, and read `Failure` and `Message` on failure.

## Status Snapshot and Debugging

`IUiService.StatusChanged` publishes an immutable `UiStatusChangedEventArgs` on the Unity main thread. `Previous` and `Current` are complete `UiCapabilitySnapshot`s covering `GameVersion`, `Available`, `SceneName`, `IsMainMenuReady`, and `MenuGeneration`; the event arguments never expose Unity objects or internal handles. Only actual state changes raise the event. Subscriber exceptions are isolated and logged without interrupting the UI lifecycle, and no events are published after the service is disposed.

On the `Sprocket Mod API` configuration page of the in-game Mod menu (the `MODS` button at the bottom-left of the in-game "Settings → General" page), the `UI diagnostics` section provides a toggle; `UI debug: every frame` and `UI debug: lifecycle` under `UI diagnostics` control per-frame logging and lifecycle logging:

```json
{ "Enabled": false, "LogLifecycle": true, "LogEveryFrame": false }
```

`[SMA-UI-TRACE]` output appears only when `Enabled` is `true`. If the configuration cannot be read, a single Warning is logged and operation continues in the disabled state.

## Native Menu Buttons

`CreateMenuButtonAsync` registers a native menu button: after it returns an `IUiMenuButtonHandle`, the service adds a Sprocket `Tab` to the native `MenuPanel`'s button pool through `MenuPanel.Button` once the panel has drawn its buttons and the anchor named by `BelowNativeButtonText` exists. The button belongs to the game's object pool and follows the native main-menu lifecycle; after each screen switch the native menu redecorates the panel, and the service recreates that one button for the same handle.

`Parent` and `BelowNativeButtonText` need at least one of the two. `Parent` is only a host check: when set it must sit under an active Canvas with a `GraphicRaycaster`, while the button itself is created in the native `MenuPanel`'s button pool and positioned by the native layout.

`UiMenuButtonDefinition` additionally supports `Selected` and `BelowNativeButtonText`. The creation result's `IUiMenuButtonHandle` reads and writes `Enabled`, `Text`, and `Selected`, and releases the mod's ownership through `Dispose()`.

```csharp
var result = await scope.CreateMenuButtonAsync(new UiMenuButtonDefinition
{
    Parent = menuButtonsTransform,
    Text = "Open panel",
    Selected = false,
    OnClick = OpenPanel
});
```

## Advanced Main-Menu Placement Interface

`CreateMainMenuButtonAsync` places a native menu button below a specified vanilla button. `BelowNativeButtonText` should hold the text the vanilla button currently displays.

```csharp
var result = await scope.CreateMainMenuButtonAsync(new UiMenuButtonDefinition
{
    Parent = menuButtonsTransform,
    Text = "Example Mod",
    BelowNativeButtonText = "Custom Battle",
    OnClick = OpenExamplePanel
});
```

While the anchor has not appeared yet, the button stays registered instead of landing at the end of the native list (which would be "silently placed in the wrong position"). `SceneUnavailable` is returned when the main-menu scene is not loaded. All UI callbacks run on the Unity main thread.

Common `UiFailureCode` values include `InvalidParent`, `SceneUnavailable`, `OwnerDisposed`, `Cancelled`, and `CreationFailed`. Menu buttons belong to a game object pool and should not be passed to Unity `Destroy` directly by a mod.
