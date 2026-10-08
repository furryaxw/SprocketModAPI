# Sprocket Mod API

[中文](README.zh.md) | **English**

API docs: [Public API index](docs/api.en.md), [keybinding registration](docs/keybindings-api.en.md), [UI registration](docs/ui-api.en.md), [mod configuration](docs/mod-config-api.en.md), [mod metadata contract](docs/mod-metadata.en.md), [keybinding management](docs/keybindings.en.md), and [keybinding troubleshooting](docs/keybindings-debug.en.md).

The UI service exposes a `StatusChanged` status-snapshot event for observing the scene, capabilities, main-menu ready state, and generation.

A shared runtime library for Sprocket MelonLoader mods. The current public interface provides unified keybinding registration, input routing, configuration persistence, and an in-game mod keybinding management window.

The current release is `1.0.0`, and the public API version is `2.0`. Release versions and API compatibility versions are independent of each other. The target environment is Sprocket `0.2.55.5`, BepInEx `6.0.0-be.788` (IL2CPP), and the Unity Input System.

## Features

- Acquire services from the modular registry via `SprocketApi.TryGetService<T>()`; `IInputService` and the first batch of `IUiService` are currently available.
- Register mod actions with a stable `modId + actionId`.
- Each action has two key slots, bindable to keyboard keys, mouse buttons, and exact modifier-key combinations.
- A modifier key can itself serve as the primary key, for example binding left `Ctrl` on its own.
- Provides `Pressed`, `Released`, and `Tapped` callbacks plus per-frame state queries.
- Recognizes `Gameplay`, `Designer`, `MainMenu`, `PauseMenu`, `Settings`, `TextInput`, and `OtherMenu` through scene and UI lifecycles; the pause menu overrides Gameplay.
- Scene and context switches resume dispatch only after two consecutive stable updates; losing focus, opening settings, text input, or an input gate releases any pressed actions and requires the previous physical key to be fully released before they can fire again.
- Provides a nestable `AcquireInputBlock` so other mods can temporarily block all API actions.
- Provides a read-only metadata snapshot through `IModMetadataService`: it reads `Sprocket.Mod.*` assembly metadata and `BepInPlugin` from BepInEx's loaded-plugin table, along with required/optional dependencies and incompatible plugins; missing fields fall back in order from display name to assembly name to file name. It never accesses the network or executes third-party code.
- Provides declarative configuration through `IModConfigService`: mods declare toggle, slider, dropdown, and text entries, while the API handles control rendering, atomic persistence (`BepInEx\config\SprocketModAPI\modconfig\<modId>.json`), backup-and-recovery of corrupted files, and change events.
- Provides an in-game Mod menu (opened via the `MODS` button at the bottom-left of the in-game "Settings → General" page; key actions are unbound by default, so bind your own shortcuts in the keybinding window): a mod list with search on the left, and metadata/dependency details plus declarative configuration pages on the right. Mods can be disabled by renaming them to `.dll.disable` (takes effect after a restart).
- Module lifecycle is isolated per module: if one module fails to initialize or dispose, only an error is logged; the remaining services stay available and the whole API does not fail to load.
- The mod keybinding entry is shown only while the native Keymapping page is active; the entry follows the native page through UI events and aligns with the left edge of `Action buttons`.
- The management window supports grouping by mod, search, scrolling, two-slot binding, `Esc` to unbind, and restore-defaults; while open, it takes over mouse, scroll-wheel, keyboard, and navigation input on the native Keymapping page, and restores the original controls and selection state on close.
- The management window warns about exact binding conflicts among mod actions and against the currently loaded game `InputActionAsset`, but does not block the user from saving.
- Saves user overrides to `BepInEx\config\SprocketModAPI\keybindings.json`; bindings without an override keep following the mod's defaults.
- Configuration is validated against schema/API versions; corrupted or incompatible files are kept as diagnostic backups and reset to a writable configuration, and a single invalid entry does not block other valid overrides.
- Action-callback exceptions are isolated, so an error in one mod does not block other actions.

## Installation

1. Install the MelonLoader net6 build that matches your game version.
2. Place `SprocketModAPI.dll` in the `Mods` folder at the game root.
3. Place the DLLs of mods that depend on this API in the same `Mods` folder.

Dependent mods should declare the following on their assembly:

```csharp
[assembly: MelonAdditionalDependencies("SprocketModAPI")]
```

## Usage

Open the game's Settings, go to the Keymapping page, and click `MOD KEYBINDINGS`. Click any binding slot to start capturing; pressing `Esc` clears the current slot, and clicking a button in the Reset column restores that action's default binding. `Delete` and `Backspace` can be bound like any other key.

For a mod integration example and interface constraints, see [Public API](docs/api.en.md). For key capture, persistence, and context behavior, see [Keybinding Management](docs/keybindings.en.md).

## Building

The project targets .NET 6 and by default reads the MelonLoader, IL2CPP, Unity UI, TextMeshPro, and Input System assemblies from a neighboring Sprocket install directory.

```text
G:\Sprocket\
├── MelonLoader\
├── Mods\
└── mod\SprocketModAPI\
```

```powershell
dotnet build .\SprocketModAPI\SprocketModAPI.csproj `
  --configuration Release `
  -p:SkipModDeploy=true
```

Dropping `-p:SkipModDeploy=true` copies the built DLL into the game's `Mods` folder. When the repository lives elsewhere, point at the game root with `-p:SprocketGameRoot="G:\Sprocket"`.

To run the offline contract tests:

```powershell
dotnet run --configuration Release `
  --project .\SprocketModAPI.ContractTests\SprocketModAPI.ContractTests.csproj
```

## Source Layout

- `SprocketModAPI/Core/`: the public entry point, service registry, and common module lifecycle; contains no feature implementations.
- `SprocketModAPI/Modules/Keybindings/`: keybinding public contracts, input routing, configuration persistence, the management window, and Settings observers.
- `SprocketModAPI/Modules/ModMeta/`: mod metadata public contracts, the read-only snapshot service, and the MelonLoader read source.
- `SprocketModAPI/Modules/ModConfig/`: declarative configuration public contracts, validation, persistence, and change events.
- `SprocketModAPI/Modules/ModMenu/`: the in-game Mod menu (list model, `.dll.disable` toggling, and UGUI window).
- `SprocketModAPI.ContractTests/`: public-behavior contract tests that run without launching the game.
- `docs/`: mod integration, metadata contracts, configuration contracts, menu documentation, and keybinding management docs.

## Current Limitations

- v1 仅支持键盘和鼠标。
- 管理窗口依赖当前 Sprocket 设置场景和 UI 结构，游戏更新后可能需要适配。
- 元数据快照目前只覆盖**已加载**模组：Registry 匹配结果（离线不可得）不在菜单里显示。
- 配置控件 v1 只有开关、滑条、下拉、文本四种：没有条件显示、嵌套分组和键位控件；滑条用点按 `+/-` 代替拖动，下拉用循环切换代替展开列表。
- 游戏内 Mod 菜单是自绘 UGUI：离线合约只覆盖列表模型、搜索、禁用改名与配置读写。
- UI 只提供基于原生 `MenuPanel` 按钮池的 Menu Button；Selection、Prompt 和其他原生 UI 适配器不在当前范围内。
- 原生冲突导入只读取当前已加载且能明确解析为键盘、鼠标或左右修饰键组合的绑定；无法可靠解释的复合绑定不会产生推测性警告。
- 构建和离线测试通过不代表所有分辨率、主菜单入口和暂停菜单入口均已完成游戏内验收。

## License

Copyright (c) 2026 furryAxw

This project is released under the [GNU Lesser General Public License v3.0 or later](LICENSE.txt). The GNU GPL v3 text referenced by LGPL v3 is in [COPYING.txt](COPYING.txt).
