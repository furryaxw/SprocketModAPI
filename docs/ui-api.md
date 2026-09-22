# UI 注册 API

获取 `IUiService` 后，为模组创建 owner scope，并在模组卸载时释放该 scope。

```csharp
if (!SprocketApi.TryGetService<IUiService>(out var ui))
    return;

var scope = ui.CreateScope(new UiOwnerDefinition { ModId = "example.example-mod" });
```

`UiCapabilitySnapshot.Available` 表示当前可用能力，使用 `Supports(UiCapability.MenuButton)` 检查。创建失败通过 `UiCreateResult<T>` 返回：检查 `Succeeded`，成功时读取 `Value`，失败时读取 `Failure` 和 `Message`。

## 状态快照与调试

`IUiService.StatusChanged` 在 Unity 主线程发布不可变的 `UiStatusChangedEventArgs`。`Previous` 和 `Current` 是完整的 `UiCapabilitySnapshot`，包括 `GameVersion`、`Available`、`SceneName`、`IsMainMenuReady` 与 `MenuGeneration`；事件参数不会暴露 Unity 对象或内部句柄。仅实际状态变化会触发事件。订阅者异常会被隔离并记录，不会中断 UI 生命周期；服务释放后不再发布事件。

在游戏内 Mod 菜单（「设置 → General」左下角的 `MODS` 按钮）的 `Sprocket Mod API` 配置页，`UI diagnostics` 区域提供开关；`UI diagnostics` 的 `UI debug: every frame` 与 `UI debug: lifecycle` 决定逐帧日志与生命周期日志：

```json
{ "Enabled": false, "LogLifecycle": true, "LogEveryFrame": false }
```

只有 `Enabled` 为 `true` 时才输出 `[SMA-UI-TRACE]`。配置无法读取时会记录一次 Warning，并以关闭状态继续运行。

## 原生菜单按钮

`CreateMenuButtonAsync` 通过 `MenuPanel.Button` 创建 Sprocket `Tab`。池对象由游戏管理，并跟随原生主菜单生命周期。

`UiMenuButtonDefinition` 另外支持 `Selected` 和 `BelowNativeButtonText`。创建结果的 `IUiMenuButtonHandle` 可读写 `Enabled`、`Text`、`Selected`，并通过 `Dispose()` 释放模组所有权。

```csharp
var result = await scope.CreateMenuButtonAsync(new UiMenuButtonDefinition
{
    Parent = menuButtonsTransform,
    Text = "Open panel",
    Selected = false,
    OnClick = OpenPanel
});
```

## 高级主菜单位置接口

`CreateMainMenuButtonAsync` 用于将原生菜单按钮放在指定原版按钮下方。`BelowNativeButtonText` 应填写原版按钮当前显示的文本。

```csharp
var result = await scope.CreateMainMenuButtonAsync(new UiMenuButtonDefinition
{
    Parent = menuButtonsTransform,
    Text = "Example Mod",
    BelowNativeButtonText = "Custom Battle",
    OnClick = OpenExamplePanel
});
```

原生模板不可用或找不到指定锚点时，接口会返回结构化失败，不会把按钮静默放到错误位置。只有已确认的 Sprocket `Tab` 和 `MenuPanel` 可用时才能创建菜单按钮。所有 UI 回调都在 Unity 主线程执行。

常见 `UiFailureCode` 包括 `CapabilityUnavailable`、`TemplateNotFound`、`InvalidParent`、`OwnerDisposed`、`Cancelled` 和 `CreationFailed`。菜单按钮属于游戏对象池，不应由模组直接调用 Unity `Destroy`。
