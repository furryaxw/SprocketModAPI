# Sprocket Mod API

API 文档：[公共 API 索引](docs/api.md)、[按键注册](docs/keybindings-api.md)、[UI 注册](docs/ui-api.md)、[模组配置](docs/mod-config-api.md)、[模组元数据契约](docs/mod-metadata.md)、[键位管理](docs/keybindings.md)和[键位自助排障](docs/keybindings-debug.md)。

UI 服务提供 `StatusChanged` 状态快照事件，用于观察场景、能力、主菜单 ready 状态和 generation。

面向《Sprocket》MelonLoader 模组的公共运行库。当前公共接口提供统一键位注册、输入路由、配置持久化和游戏内模组键位管理窗口。

当前发行版本为 `0.3.0`，公共 API 版本为 `2.0`。发行版本与 API 兼容版本相互独立。目标环境为 Sprocket `0.2.53.2`、MelonLoader net6 和 Unity Input System。

## 功能

- 通过 `SprocketApi.TryGetService<T>()` 从模块化注册表获取服务，当前提供 `IInputService` 和首批 `IUiService`。
- 使用稳定的 `modId + actionId` 注册模组动作。
- 每个动作支持两个键位槽，可绑定键盘键、鼠标键和精确修饰键组合。
- 支持修饰键自身作为主键，例如单独绑定左 `Ctrl`。
- 提供 `Pressed`、`Released`、`Tapped` 回调和逐帧状态查询。
- 通过场景和 UI 生命周期识别 `Gameplay`、`Designer`、`MainMenu`、`PauseMenu`、`Settings`、`TextInput` 与 `OtherMenu`；暂停菜单覆盖 Gameplay。
- 场景和上下文切换需要连续两个稳定更新后才恢复派发；失焦、设置、文本输入或输入门禁会释放已按动作，并要求旧物理按键完整释放后才能重新触发。
- 提供可嵌套的 `AcquireInputBlock`，供其他模组临时阻断全部 API 动作。
- 提供只读元数据快照 `IModMetadataService`：从已加载模组读取 `Sprocket.Mod.*` 程序集元数据和 `MelonInfo`，以及兼容游戏、必需/可选依赖、不兼容程序集和 MelonLoader 版本要求；缺失字段按程序集名、文件名逐级降级，不联网、不执行第三方代码。
- 提供声明式配置 `IModConfigService`：模组声明开关、滑条、下拉与文本条目，控件渲染、原子持久化（`UserData\SprocketModAPI\modconfig\<modId>.json`）、损坏文件备份恢复和变更事件都由 API 负责。
- 提供游戏内 Mod 菜单（入口是「设置 → General」页左下角的 `MODS` 按钮；键位动作默认不绑键，想用快捷键就在键位窗口里自己绑）：左侧模组列表带搜索，右侧显示元数据/依赖详情与声明式配置页，并支持把模组禁用为 `.dll.disable`（重启生效）。
- 模块生命周期按模块隔离：单个模块初始化或销毁失败只记录错误，其余服务照常可用，不会让整个 API 加载失败。
- 仅在原生 Keymapping 页面激活时显示模组键位入口；入口通过 UI 事件跟随原生页面，并与 `Action buttons` 左边界对齐。
- 管理窗口支持按模组分组、搜索、滚动、双槽绑定、Esc 解绑和恢复默认值；打开期间会接管原生 Keymapping 页的鼠标、滚轮、键盘与导航输入，关闭后恢复原始控件和选中状态。
- 管理窗口警告模组动作之间及与当前已加载游戏 `InputActionAsset` 的精确绑定冲突，但不阻止用户保存。
- 将用户覆盖保存到 `UserData\SprocketModAPI\keybindings.json`；未覆盖的绑定继续跟随模组默认值。
- 配置按 schema/API 版本校验；损坏或不兼容文件会保留诊断备份并恢复为可写配置，单项错误不会阻断其他有效覆盖。
- 隔离动作回调异常，单个模组的错误不会阻断其他动作。

## 安装

1. 安装与游戏版本匹配的 MelonLoader net6。
2. 将 `SprocketModAPI.dll` 放入游戏根目录的 `Mods` 文件夹。
3. 将依赖本 API 的模组 DLL 放入同一 `Mods` 文件夹。

依赖模组应在程序集上声明：

```csharp
[assembly: MelonAdditionalDependencies("SprocketModAPI")]
```

## 使用

进入游戏设置的 Keymapping 页面后，点击 `MOD KEYBINDINGS`。点击任一绑定槽开始捕获；按 `Esc` 会清空当前槽位，点击 Reset 列中的按钮恢复该动作的默认绑定。`Delete` 和 `Backspace` 可像其他键一样被绑定。

模组接入示例和接口约束见 [公共 API](docs/api.md)。键位捕获、持久化和上下文行为见 [键位管理](docs/keybindings.md)。

## 构建

项目目标框架为 .NET 6，默认从相邻的 Sprocket 安装目录读取 MelonLoader、IL2CPP、Unity UI、TextMeshPro 和 Input System 程序集。

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

去掉 `-p:SkipModDeploy=true` 会将生成的 DLL 复制到游戏的 `Mods` 文件夹。仓库位于其他位置时，可通过 `-p:SprocketGameRoot="G:\Sprocket"` 指定游戏根目录。

运行离线合约测试：

```powershell
dotnet run --configuration Release `
  --project .\SprocketModAPI.ContractTests\SprocketModAPI.ContractTests.csproj
```

## 源码布局

- `SprocketModAPI/Core/`：公共入口、服务注册表和统一模块生命周期，不包含具体功能实现。
- `SprocketModAPI/Modules/Keybindings/`：键位公共契约、输入路由、配置持久化、管理窗口和 Settings 观察器。
- `SprocketModAPI/Modules/ModMeta/`：模组元数据公共契约、只读快照服务与 MelonLoader 读取源。
- `SprocketModAPI/Modules/ModConfig/`：声明式配置公共契约、校验、持久化与变更事件。
- `SprocketModAPI/Modules/ModMenu/`：游戏内 Mod 菜单（列表模型、`.dll.disable` 切换与 UGUI 窗口）。
- `SprocketModAPI.ContractTests/`：不启动游戏的公共行为合约测试。
- `docs/`：模组接入、元数据契约、配置契约、菜单说明与键位管理文档。

## 当前限制

- v1 仅支持键盘和鼠标。
- 管理窗口依赖当前 Sprocket 设置场景和 UI 结构，游戏更新后可能需要适配。
- 元数据快照目前只覆盖**已加载**模组：Registry 匹配结果（离线不可得）不在菜单里显示。
- 配置控件 v1 只有开关、滑条、下拉、文本四种：没有条件显示、嵌套分组和键位控件；滑条用点按 `+/-` 代替拖动，下拉用循环切换代替展开列表。
- 游戏内 Mod 菜单是自绘 UGUI：离线合约只覆盖列表模型、搜索、禁用改名与配置读写。
- UI 只提供基于已验证 Sprocket `Tab` 模板的 Menu Button；Selection、Prompt 和其他原生 UI 适配器不在当前范围内。
- 原生冲突导入只读取当前已加载且能明确解析为键盘、鼠标或左右修饰键组合的绑定；无法可靠解释的复合绑定不会产生推测性警告。
- 构建和离线测试通过不代表所有分辨率、主菜单入口和暂停菜单入口均已完成游戏内验收。

## 许可证

Copyright (c) 2026 furryAxw

本项目以 [GNU Lesser General Public License v3.0 或更高版本](LICENSE.txt) 发布。LGPL v3 引用的 GNU GPL v3 正文见 [COPYING.txt](COPYING.txt)。
