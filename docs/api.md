# 公共 API

Sprocket Mod API 当前发行版本为 `0.3.0`，公共 API 版本为 `2.0`。

```csharp
if (!SprocketApi.IsCompatible(new Version(1, 0)))
    return;
```

模组应引用游戏 `Mods` 目录中唯一的 `SprocketModAPI.dll`，并声明 `MelonAdditionalDependencies("SprocketModAPI")`。不要随模组分发另一份私有 API DLL。

## API 分类

- [按键注册](keybindings-api.md)
- [UI 注册](ui-api.md)
- [模组配置](mod-config-api.md)：`IModConfigService` 的声明式配置注册、持久化与变更事件。
- [模组元数据](mod-metadata.md)：`IModMetadataService` 提供只读元数据快照，以及让模组声明 `Sprocket.Mod.*` 程序集元数据的跨仓库契约（与 `sprocket-mod-system` 共用）。

通过 `SprocketApi.TryGetService<T>()` 获取服务。模组卸载时必须释放自己创建的 scope 和 handle。

```csharp
if (SprocketApi.TryGetService<IModMetadataService>(out var metadata))
{
    foreach (ModMetadata mod in metadata.Entries)
        Console.WriteLine($"{mod.DisplayName} {mod.Version} ({mod.Id})");
}
```

`ModMetadata.Id` 优先取模组声明的 `Sprocket.Mod.Id`，未声明时退化为 `file:<程序集名>`。所以菜单不能假设每个条目都能对应到 Registry。
