# Public API

[中文](api.zh.md) | **English**

The current Sprocket Mod API release is `1.1.0`, and the public API version is `2.0`.

```csharp
if (!SprocketApi.IsCompatible(new Version(1, 0)))
    return;
```

Mods should reference the single `SprocketModAPI.dll` in `BepInEx\plugins` and declare `[BepInDependency("furryaxw.sprocket-mod-api")]`. Do not distribute a second private copy of the API DLL with your mod.

## API Categories

- [Keybinding registration](keybindings-api.en.md)
- [UI registration](ui-api.en.md)
- [Mod configuration](mod-config-api.en.md): declarative configuration registration, persistence, and change events for `IModConfigService`.
- [Mod metadata](mod-metadata.en.md): `IModMetadataService` provides a read-only metadata snapshot, plus the cross-repository contract (shared with `sprocket-mod-system`) that lets mods declare `Sprocket.Mod.*` assembly metadata.
- `IModRuntimeService`: registers a plugin's per-frame, late-frame, GUI, scene-change, and shutdown callbacks on the API's driver component (BepInEx gives mods no frame loop), and starts coroutines for it.
- `IModLogService`: obtains an `IModLogger` under a display name, and logs go to the BepInEx log source.

## Runtime Host

Every `Add*` call on `IModRuntimeService` returns an unregistration handle; a callback is no longer delivered after its handle is disposed. Callbacks must stay on the main thread, and a callback that throws is reported once and disables only its own registration while the remaining registrations continue.

- `AddUpdate` and `AddLateUpdate` dispatch in two separate phases; `AddGui` draws during `OnGUI`.
- `AddSceneChanged` receives `(new scene name, previous scene name)`; switches and unloads are both reported in load/unload order.
- `AddShutdown` runs once, in registration order, after per-frame callbacks stop being delivered, so a plugin can release its own resources.
- `StartCoroutine` runs the coroutine on the API's driver component; hand its return value to `StopCoroutine`.

## Logging

`IModLogService.Create(displayName)` returns an `IModLogger`; an empty `displayName` uses `Sprocket Mod`. The display name goes to the BepInEx log source, and the remaining channels (file, console) are decided by the loader.

Acquire services via `SprocketApi.TryGetService<T>()`. A mod must release any scope and handle it created when it unloads.

```csharp
if (SprocketApi.TryGetService<IModMetadataService>(out var metadata))
{
    foreach (ModMetadata mod in metadata.Entries)
        Console.WriteLine($"{mod.DisplayName} {mod.Version} ({mod.Id})");
}
```

`ModMetadata.Id` prefers the mod's declared `Sprocket.Mod.Id` and falls back to `file:<assembly name>` when it is not declared. The menu therefore cannot assume every entry maps to a Registry entry.
