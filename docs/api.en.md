# Public API

[中文](api.zh.md) | **English**

The current Sprocket Mod API release is `1.0.0`, and the public API version is `2.0`.

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

Acquire services via `SprocketApi.TryGetService<T>()`. A mod must release any scope and handle it created when it unloads.

```csharp
if (SprocketApi.TryGetService<IModMetadataService>(out var metadata))
{
    foreach (ModMetadata mod in metadata.Entries)
        Console.WriteLine($"{mod.DisplayName} {mod.Version} ({mod.Id})");
}
```

`ModMetadata.Id` prefers the mod's declared `Sprocket.Mod.Id` and falls back to `file:<assembly name>` when it is not declared. The menu therefore cannot assume every entry maps to a Registry entry.
