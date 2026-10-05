# Mod Metadata Contract v1 (Metadata Embedded in the DLL)

[中文](mod-metadata.zh.md) | **English**

The in-game mod menu, the `sprocket-mod-system` manager, and the Registry must recognize the **same mod** as the same entity.
This document defines the shared field sources for all three and **does not depend on network access**.

## Three principles

1. **Local first**: the metadata embedded in the DLL is the authoritative source; the Registry only supplements display information (description, category, repository).
2. **Read-only static metadata**: a reader must not call `Assembly.Load`, must not execute third-party code, and **must never run DLL code** (for the manager-side hard constraint, see "DLL classification" in
   `sprocket-mod-spec.en.md`).
3. **Missing data degrades gracefully**: a missing field must never prevent a mod from loading, nor cause the menu or manager to error out.

## Field sources and precedence

| Field | 1 (highest) | 2 | 3 (fallback) |
| --- | --- | --- | --- |
| Id | `Sprocket.Mod.Id` | Registry match | Derived `file:<DLL file name>` |
| DisplayName | `Sprocket.Mod.DisplayName` | `BepInPlugin.Name` | Assembly name → file name |
| Version | `Sprocket.Mod.Version` | `BepInPlugin.Version` | `AssemblyInformationalVersion` / `AssemblyVersion` / `VS_FIXEDFILEINFO` |
| Authors | `Sprocket.Mod.Authors` (comma-separated) | Empty | Empty |
| Credits | `Sprocket.Mod.Credits` | Registry | Empty |
| Description | `Sprocket.Mod.Description` | Registry `description` | Empty (shown as "no description") |
| Homepage / Repository / Category / Tags / License | `Sprocket.Mod.*` | Registry | Empty |

`BepInPlugin` carries only three fields — GUID, display name, and version (no description, authors, or credits) —
so **do not stuff a description into the version string**; descriptions always go through `Sprocket.Mod.Description` or the Registry.

## Key reference (all optional except Id)

| Key | Meaning | Constraint |
| --- | --- | --- |
| `Sprocket.Mod.Id` | Stable mod ID | A mod already in the Registry must match the Registry `id` (for example `example.example-mod`). It must not change after release. **The keybinding and config namespaces are also inherited from it.** |
| `Sprocket.Mod.DisplayName` | Menu display name | Single-language string; v1 is not localized |
| `Sprocket.Mod.Description` | Menu description | One line or a short few lines |
| `Sprocket.Mod.Authors` | Author list | Comma-separated |
| `Sprocket.Mod.Credits` | Additional credits | Single-line text |
| `Sprocket.Mod.Homepage` | Homepage | Full URL or empty |
| `Sprocket.Mod.Repository` | Repository | `owner/repo` |
| `Sprocket.Mod.Category` | Category | Must match the Registry `category` value |
| `Sprocket.Mod.License` | SPDX identifier | For example `MIT` |

v1 supports single-language strings only. The Registry's `display_name`/`description` localization maps are used for website display only;
the in-game menu is offline and reads only `Sprocket.Mod.DisplayName`.

## How to declare it

```csharp
[assembly: AssemblyMetadata("Sprocket.Mod.Id", "example.example-mod")]
[assembly: AssemblyMetadata("Sprocket.Mod.DisplayName", "Example Mod")]
[assembly: AssemblyMetadata("Sprocket.Mod.Description", "An example mod that demonstrates the SprocketModAPI surface.")]
[assembly: AssemblyMetadata("Sprocket.Mod.Authors", "Example Author")]
[assembly: AssemblyMetadata("Sprocket.Mod.Repository", "example/ExampleMod")]
[assembly: AssemblyMetadata("Sprocket.Mod.Category", "graphics")]
[assembly: AssemblyMetadata("Sprocket.Mod.License", "MIT")]
```

`AssemblyMetadata` may appear more than once; keys are case-sensitive, and readers compare keys in ordinal order.

## Reader obligations

**ModAPI (in-game)**: from BepInEx's loaded plugin table (`IL2CPPChainloader.Instance.Plugins`), read the
`BepInPlugin` metadata (GUID / display name / version) together with the plugin assembly location and its dependency and incompatibility declarations, and for **loaded assemblies** use
`Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()` to read `Sprocket.Mod.*`.
A loaded assembly is trusted (BepInEx has already executed it), so no static parsing is needed.

## Enable/disable convention

- Disable = rename `<Name>.dll` to `<Name>.dll.disable` (BepInEx loads only `*.dll`); it **takes effect after a restart**. The menu must prompt for a restart and must not claim the mod is "stopped".
- A reader must recognize `*.dll.disable` as a "disabled mod" rather than "unrecognized" or an "orphan file", and must be able to read its embedded metadata statically.
- Enable = rename it back to `<Name>.dll`.
- The managed file name is `<Name>.dll.disable`; all plugins live in a single directory (`BepInEx\plugins`), and the convention has only this one form.
