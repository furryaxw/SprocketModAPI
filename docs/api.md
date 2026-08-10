# 公共 API

Sprocket Mod API 当前发行版本为 `0.1.0`，公共 API 版本为 `1.1`。

```csharp
if (!SprocketApi.IsCompatible(new Version(1, 0)))
    return;
```

模组应引用游戏 `Mods` 目录中唯一的 `SprocketModAPI.dll`，并声明 `MelonAdditionalDependencies("SprocketModAPI")`。不要随模组分发另一份私有 API DLL。

## API 分类

- [按键注册](keybindings-api.md)
- [UI 注册](ui-api.md)

通过 `SprocketApi.TryGetService<T>()` 获取服务。模组卸载时必须释放自己创建的 scope 和 handle。
