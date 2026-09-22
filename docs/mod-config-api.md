# 模组配置 API

声明式配置：模组只声明「有哪些设置项」，控件渲染、持久化、变更通知由 API 负责。
游戏内 Mod 菜单通过同一份声明渲染配置页，因此**模组不需要自己写 UI**。

## 注册

```csharp
private IModConfigRegistration? config;

public override void OnInitializeMelon()
{
    if (!SprocketApi.TryGetService<IModConfigService>(out var service))
        return;

    config = service.Register(new ModConfigDefinition
    {
        // ModId 省略 = 继承本程序集的 Sprocket.Mod.Id（配置文件名与键位命名空间都用它）
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

public override void OnDeinitializeMelon() => config?.Dispose();
```

## 读取与写入

```csharp
bool enabled = config.GetBool("enabled");
double strength = config.GetNumber("strength");
string mode = config.GetText("mode");

config.SetBool("enabled", false);
config.SetNumber("strength", 2.0);
config.SetText("mode", "mode-b");
config.ResetToDefault("strength");
```

- 写入成功立即持久化，并发布 `IModConfigService.Changed`（参数只含 `ModId` 与 `Key`）。
- 写入相同值**不产生事件、也不写文件**。
- 键位相关设置请使用 [按键注册](keybindings-api.md) 的 `IInputService`，v1 配置控件不含键位控件。

## 条目类型

| 工厂 | 值类型 | 说明 |
| --- | --- | --- |
| `ModConfigEntryDefinition.Toggle` | `bool` | 开关 |
| `ModConfigEntryDefinition.Slider` | `double` | 需要 `minimum < maximum` 且 `step > 0`，默认值必须在范围内 |
| `ModConfigEntryDefinition.Choice` | `string` | 选项非空、不重复，默认值必须是其中一个选项 |
| `ModConfigEntryDefinition.Text` | `string` | `maxLength > 0`，默认值长度不得超过它 |

## 菜单侧

```csharp
foreach (ModConfigSnapshot page in service.Snapshots)
    foreach (ModConfigEntrySnapshot entry in page.Entries)
        Render(entry.Definition, entry.Value);

var registration = service.Find("example.example-mod");
registration?.SetNumber("strength", 3.0);
```

`Snapshots` 按 `DisplayName`（忽略大小写）再按 `ModId` 排序；`RegistrationsChanged` 在模组注册或注销配置页后触发。
所有读写都必须回到游戏主线程。

## 持久化

- 每个模组一个文件：`UserData/SprocketModAPI/modconfig/<modId>.json`，其中 `<modId>` = `Sprocket.Mod.Id`
  （未声明时退化为程序集名）。
- 架构：`{"ConfigVersion":"<版本>","ModId":"…","Values":{…}}`。
- `ConfigVersion` 比当前新（文件来自更新构建）、JSON 损坏或版本不可读时：先备份为
  `<file>.corrupt-<UTC 时间戳>.bak`，再重建为空配置。
- `ConfigVersion` 比当前旧时**自动迁移**：回写为当前版本（迁移会清掉旧文件里的 `SchemaVersion` 与
  `ApiVersion` 字段），内容保留、不产生备份。
- 单项值类型不符、选项已不在列表、文本超长时：**只丢弃该项**并回退到声明默认值，其余项继续加载；数值超出新范围时钳制到边界并告警。
- 文件中已不再声明的键会被保留，不会因为一次保存而被抹掉。
- 写入是原子的（先写 `.tmp` 再替换）。

## 校验与失败语义

注册时立即校验，错误直接抛出，不留到菜单渲染：

- `ModId`、`Section.Id`、`Entry.Key` 只能是字母、数字、`-`、`_`、`.`，长度 1..64；
- section id 与 entry key 各自唯一，`SectionId` 必须指向已声明的 section（或留空）；
- 同一个 `ModId` 只能注册一次，重复注册会被拒绝并记录日志；
- 读取未知键抛 `ArgumentException`；读写类型不匹配抛 `InvalidOperationException`；
- 写入越界数值、非法选项或超长文本抛 `ArgumentOutOfRangeException` / `ArgumentException`。

## v1 限制

- 没有条件显示（“勾选 A 才显示 B”）、没有分组折叠、没有键位控件。
- 只有一个扁平层级：section 与 entry 都不支持嵌套。
