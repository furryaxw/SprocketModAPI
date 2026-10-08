# 公共 API

Sprocket Mod API 当前发行版本为 `1.1.0`，公共 API 版本为 `2.0`。

```csharp
if (!SprocketApi.IsCompatible(new Version(1, 0)))
    return;
```

模组应引用 `BepInEx\plugins` 目录中唯一的 `SprocketModAPI.dll`，并声明 `[BepInDependency("furryaxw.sprocket-mod-api")]`。不要随模组分发另一份私有 API DLL。

## API 分类

- [按键注册](keybindings-api.md)
- [UI 注册](ui-api.md)
- [模组配置](mod-config-api.md)：`IModConfigService` 的声明式配置注册、持久化与变更事件。
- [模组元数据](mod-metadata.md)：`IModMetadataService` 提供只读元数据快照，以及让模组声明 `Sprocket.Mod.*` 程序集元数据的跨仓库契约（与 `sprocket-mod-system` 共用）。
- `IModRuntimeService`：把插件自己的每帧、晚帧、GUI、场景变化与退出回调登记在 API 的驱动组件上（BepInEx 不给模组帧循环），并可借它启动协程。
- `IModLogService`：按显示名取得 `IModLogger`，日志进 BepInEx 日志源。

## 运行时宿主

`IModRuntimeService` 的每个 `Add*` 返回注销句柄，句柄 `Dispose` 之后不再回调。回调必须留在主线程，抛异常时该登记被停用并只报一次，其余登记继续。

- `AddUpdate` / `AddLateUpdate` 分两个阶段派发，`AddGui` 在 `OnGUI` 阶段绘制。
- `AddSceneChanged` 收到 `(新场景名, 上一个场景名)`；切换与卸载都按加载/卸载顺序成对报出。
- `AddShutdown` 在每帧回调停止送达之后按登记顺序执行一次，用于释放插件自己的资源。
- `StartCoroutine` 的协程跑在 API 的驱动组件上，返回值交给 `StopCoroutine`。

## 日志

`IModLogService.Create(displayName)` 返回 `IModLogger`；`displayName` 为空时使用 `Sprocket Mod`。显示名进 BepInEx 日志源，其余渠道（文件、控制台）由加载器决定。

通过 `SprocketApi.TryGetService<T>()` 获取服务。模组卸载时必须释放自己创建的 scope 和 handle。

```csharp
if (SprocketApi.TryGetService<IModMetadataService>(out var metadata))
{
    foreach (ModMetadata mod in metadata.Entries)
        Console.WriteLine($"{mod.DisplayName} {mod.Version} ({mod.Id})");
}
```

`ModMetadata.Id` 优先取模组声明的 `Sprocket.Mod.Id`，未声明时退化为 `file:<程序集名>`。所以菜单不能假设每个条目都能对应到 Registry。
