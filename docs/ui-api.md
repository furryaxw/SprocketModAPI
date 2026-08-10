# UI 注册 API

获取 `IUiService` 后，为模组创建 owner scope，并在模组卸载时释放该 scope。

```csharp
if (!SprocketApi.TryGetService<IUiService>(out var ui))
    return;

var scope = ui.CreateScope(new UiOwnerDefinition { ModId = "example-mod" });
```

`UiCapabilitySnapshot.Available` 表示当前可用能力，使用 `Supports(UiCapability.Button)` 或 `Supports(UiCapability.MenuButton)` 检查。创建失败通过 `UiCreateResult<T>` 返回：检查 `Succeeded`，成功时读取 `Value`，失败时读取 `Failure` 和 `Message`。

## API 管理的普通按钮

`CreateButtonAsync` 使用经过验证的隐藏模板创建普通 Unity `Button`。对象由 API 管理，不属于 Sprocket 主菜单的原生按钮池。

`UiButtonDefinition` 字段：`Parent`（必须位于启用的 Canvas 和 GraphicRaycaster 下）、`Text`、`Enabled`、可选 `Size`、可选 `AnchoredPosition` 和 `OnClick`。

```csharp
var result = await scope.CreateButtonAsync(new UiButtonDefinition
{
    Parent = parentTransform,
    Text = "Open panel",
    OnClick = OpenPanel
});
```

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
    Text = "Laser Rangefinder",
    BelowNativeButtonText = "Custom Battle",
    OnClick = OpenRangefinder
});
```

原生模板不可用或找不到指定锚点时，接口会返回结构化失败，不会把按钮静默放到错误位置。只有已确认的 Sprocket `Tab` 和 `MenuPanel` 可用时才能创建菜单按钮。所有 UI 回调都在 Unity 主线程执行。

常见 `UiFailureCode` 包括 `CapabilityUnavailable`、`TemplateNotFound`、`InvalidParent`、`OwnerDisposed`、`Cancelled` 和 `CreationFailed`。普通按钮由 API 销毁；菜单按钮属于游戏对象池，不应由模组直接调用 Unity `Destroy`。
