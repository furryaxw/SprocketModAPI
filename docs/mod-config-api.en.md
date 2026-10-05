# Mod Config API

[中文](mod-config-api.zh.md) | **English**

Declarative configuration: a mod only declares which settings exist; the API handles control rendering, persistence, and change notification.
The in-game mod menu renders the config page from that same declaration, so **a mod does not need to write its own UI**.

## Registration

```csharp
private IModConfigRegistration? config;

public override void Load()
{
    if (!SprocketApi.TryGetService<IModConfigService>(out var service))
        return;

    config = service.Register(new ModConfigDefinition
    {
        // Omitting ModId inherits Sprocket.Mod.Id from this assembly (used for both the config file name and the keybinding namespace)
        DisplayName = "Example Mod",
        Sections = new[]
        {
            new ModConfigSectionDefinition { Id = "general", Title = "General" }
        },
        Entries = new[]
        {
            ModConfigEntryDefinition.Toggle("enabled", "Enable example feature", true, sectionId: "general"),
            ModConfigEntryDefinition.Slider("strength", "Example strength", 1.0, 0.5, 4.0, 0.1,
                description: "Relative strength of the example effect", sectionId: "general"),
            ModConfigEntryDefinition.Choice("mode", "Example mode", "mode-a", new[] { "mode-a", "mode-b" }),
            ModConfigEntryDefinition.Text("note", "Example note", "", 32)
        }
    });
}

public override bool Unload()
{
    config?.Dispose();
    return true;
}
```

## Reading and writing

```csharp
bool enabled = config.GetBool("enabled");
double strength = config.GetNumber("strength");
string mode = config.GetText("mode");

config.SetBool("enabled", false);
config.SetNumber("strength", 2.0);
config.SetText("mode", "mode-b");
config.ResetToDefault("strength");
```

- A successful write is persisted immediately and publishes `IModConfigService.Changed` (the arguments carry only `ModId` and `Key`).
- Writing the same value **raises no event and does not touch the file**.
- For keybinding-related settings, use `IInputService` from [keybinding registration](keybindings-api.en.md); the v1 config controls do not include a keybinding control.

## Entry types

| Factory | Value type | Notes |
| --- | --- | --- |
| `ModConfigEntryDefinition.Toggle` | `bool` | On/off toggle |
| `ModConfigEntryDefinition.Slider` | `double` | Requires `minimum < maximum` and `step > 0`; the default value must be within range |
| `ModConfigEntryDefinition.Choice` | `string` | Options must be non-empty and unique; the default value must be one of them |
| `ModConfigEntryDefinition.Text` | `string` | `maxLength > 0`; the default value must not be longer than that |

## Menu side

```csharp
foreach (ModConfigSnapshot page in service.Snapshots)
    foreach (ModConfigEntrySnapshot entry in page.Entries)
        Render(entry.Definition, entry.Value);

var registration = service.Find("example.example-mod");
registration?.SetNumber("strength", 3.0);
```

`Snapshots` is ordered by `DisplayName` (case-insensitive) and then by `ModId`; `RegistrationsChanged` fires after a mod registers or unregisters a config page.
All reads and writes must run on the game's main thread.

## Persistence

- One file per mod: `BepInEx/config/SprocketModAPI/modconfig/<modId>.json`, where `<modId>` = `Sprocket.Mod.Id`
  (falling back to the assembly name when it is not declared).
- Schema: `{"ConfigVersion":"<version>","ModId":"…","Values":{…}}`.
- When `ConfigVersion` is newer than the current one (the file came from a newer build), the JSON is corrupt, or the version cannot be read: back the file up to
  `<file>.corrupt-<UTC timestamp>.bak` first, then rebuild it as an empty config.
- When `ConfigVersion` is older than the current one, it is **migrated automatically**: the file is written back at the current version (migration drops the `SchemaVersion` and
  `ApiVersion` fields from the old file); the contents are preserved and no backup is made.
- When a single entry's value has the wrong type, its option is no longer in the list, or its text is too long: **only that entry is discarded** and it falls back to the declared default, while the other entries keep loading; a numeric value outside the new range is clamped to the bound with a warning.
- Keys that are no longer declared in the file are preserved and will not be wiped by a single save.
- Writes are atomic (write `.tmp` first, then replace).

## Validation and failure semantics

Validation runs immediately at registration and throws on error rather than deferring it to menu rendering:

- `ModId`, `Section.Id`, and `Entry.Key` may contain only letters, digits, `-`, `_`, and `.`, with a length of 1..64;
- section ids and entry keys are each unique, and `SectionId` must point to a declared section (or be left empty);
- the same `ModId` can be registered only once; a duplicate registration is rejected and logged;
- reading an unknown key throws `ArgumentException`; a type mismatch between read and write throws `InvalidOperationException`;
- writing an out-of-range number, an invalid option, or over-long text throws `ArgumentOutOfRangeException` / `ArgumentException`.

## v1 limitations

- No conditional display ("show B only when A is checked"), no collapsible groups, and no keybinding control.
- Only a single flat level: neither sections nor entries support nesting.
