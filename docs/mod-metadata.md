# Mod 元数据契约 v1（DLL 内嵌元数据）

游戏内 Mod 菜单、`sprocket-mod-system` 管理器和 Registry 需要把**同一个模组**认成同一个东西。
本文件定义三方的公共字段来源，**不依赖联网**。

## 三条原则

1. **本地优先**：DLL 内嵌元数据是权威来源；Registry 只补充展示信息（描述、分类、仓库）。
2. **只读静态元数据**：读取方不得 `Assembly.Load`、不得执行第三方代码、**永不执行 DLL 代码**（管理器侧硬约束见
   `sprocket-mod-spec.md` 的「DLL 分类」）。
3. **缺失可降级**：任何字段缺失都不得让模组无法加载，也不得让菜单或管理器报错。

## 字段来源与优先级

| 字段 | 1（最高） | 2 | 3（兜底） |
| --- | --- | --- | --- |
| Id | `Sprocket.Mod.Id` | Registry 匹配结果 | 派生 `file:<DLL 文件名>` |
| DisplayName | `Sprocket.Mod.DisplayName` | `MelonInfo.Name` | 程序集名 → 文件名 |
| Version | `MelonInfo.Version` | `AssemblyInformationalVersion` / `AssemblyVersion` | `VS_FIXEDFILEINFO` |
| Authors | `Sprocket.Mod.Authors`（逗号分隔） | `MelonInfo.Author` | 未知 |
| Credits | `MelonAdditionalCredits` | Registry | 空 |
| Description | `Sprocket.Mod.Description` | Registry `description` | 空（显示“无描述”） |
| Homepage / Repository / Category / Tags / License | `Sprocket.Mod.*` | Registry | 空 |

`MelonInfo` 本身没有描述字段（见 `MelonLoader/Attributes/MelonInfoAttribute.cs`），
所以**不要把描述塞进 `downloadLink`**；描述一律走 `Sprocket.Mod.Description` 或 Registry。

## 键名表（除 Id 外全部可选）

| 键 | 含义 | 约束 |
| --- | --- | --- |
| `Sprocket.Mod.Id` | 稳定模组 ID | 已进 Registry 的模组必须与 Registry `id` 一致（如 `example.example-mod`）。发布后不得更改。**键位与配置的命名空间也从它继承**。 |
| `Sprocket.Mod.DisplayName` | 菜单显示名 | 单语言字符串；v1 不做多语言 |
| `Sprocket.Mod.Description` | 菜单描述 | 单行或短多行 |
| `Sprocket.Mod.Authors` | 作者列表 | 逗号分隔 |
| `Sprocket.Mod.Homepage` | 主页 | 完整 URL 或空 |
| `Sprocket.Mod.Repository` | 仓库 | `owner/repo` |
| `Sprocket.Mod.Category` | 分类 | 与 Registry `category` 取值一致 |
| `Sprocket.Mod.License` | SPDX 标识 | 如 `MIT` |

v1 只支持单语言字符串。Registry 的 `display_name`/`description` 多语言映射只用于网站展示；
游戏内菜单不联网，只读 `Sprocket.Mod.DisplayName`。

## 填写方式

```csharp
[assembly: AssemblyMetadata("Sprocket.Mod.Id", "example.example-mod")]
[assembly: AssemblyMetadata("Sprocket.Mod.DisplayName", "Example Mod")]
[assembly: AssemblyMetadata("Sprocket.Mod.Description", "An example mod that demonstrates the SprocketModAPI surface.")]
[assembly: AssemblyMetadata("Sprocket.Mod.Authors", "Example Author")]
[assembly: AssemblyMetadata("Sprocket.Mod.Repository", "example/ExampleMod")]
[assembly: AssemblyMetadata("Sprocket.Mod.Category", "graphics")]
[assembly: AssemblyMetadata("Sprocket.Mod.License", "MIT")]
```

`AssemblyMetadata` 允许重复出现，键名大小写敏感，读取方按序号全序比较键名。

## 读取方义务

**ModAPI（游戏内）**：从 `MelonBase.RegisteredMelons` 取 `Info`（名称/版本/作者/下载链接）、
`MelonAssembly`（`Assembly`、`Location`、`Hash`），并对**已加载程序集**用
`Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()` 读 `Sprocket.Mod.*`。
已加载程序集是可信的（MelonLoader 已经执行过它），无需静态解析。

## 启用/禁用约定

- 禁用 = 把 `<Name>.dll` 重命名为 `<Name>.dll.disable`（MelonLoader 只加载 `*.dll`），**重启后生效**；菜单必须提示需要重启，不得声称“已停止运行”。
- 读取方必须把 `*.dll.disable` 识别为「已禁用模组」，而不是「未识别」或「孤儿文件」，并能静态读出其内嵌元数据。
- 启用 = 改回 `<Name>.dll`。
- 托管文件名为 `<Name>.dll.disable`，`Plugins/` 与 `Mods/` 使用同一约定。
