# 公共 API

**中文** | [English](api.en.md)

Sprocket Mod API 当前发行版本为 `1.0.0`，公共 API 版本为 `2.0`。

```csharp
if (!SprocketApi.IsCompatible(new Version(1, 0)))
    return;
```

模组应引用 `BepInEx\plugins` 目录中唯一的 `SprocketModAPI.dll`，并声明 `[BepInDependency("furryaxw.sprocket-mod-api")]`。不要随模组分发另一份私有 API DLL。

## API 分类

- [按键注册](keybindings-api.zh.md)
- [UI 注册](ui-api.zh.md)
- [模组配置](mod-config-api.zh.md)：`IModConfigService` 的声明式配置注册、持久化与变更事件。
- [模组元数据](mod-metadata.zh.md)：`IModMetadataService` 提供只读元数据快照，以及让模组声明 `Sprocket.Mod.*` 程序集元数据的跨仓库契约（与 `sprocket-mod-system` 共用）。

通过 `SprocketApi.TryGetService<T>()` 获取服务。模组卸载时必须释放自己创建的 scope 和 handle。

```csharp
if (SprocketApi.TryGetService<IModMetadataService>(out var metadata))
{
    foreach (ModMetadata mod in metadata.Entries)
        Console.WriteLine($"{mod.DisplayName} {mod.Version} ({mod.Id})");
}
```

`ModMetadata.Id` 优先取模组声明的 `Sprocket.Mod.Id`，未声明时退化为 `file:<程序集名>`。所以菜单不能假设每个条目都能对应到 Registry。
