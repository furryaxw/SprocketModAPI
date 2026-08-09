# 公共 API

## 版本兼容

项目发行版本为 `0.1.0`，公共 API 版本为 `1.0`。发行版本用于 DLL、Git 标签和 Release；API 版本仅用于模组运行时兼容检查，两者不要求数值相同。

运行时版本由 `SprocketApi.ApiVersion` 返回。兼容规则为 major 相同，并且运行时 minor 不低于调用方请求的 minor。

```csharp
if (!SprocketApi.IsCompatible(new Version(1, 0)))
    return;
```

破坏性接口变更会提升 major；向后兼容的新增能力会提升 minor。

## 引用与加载顺序

在模组项目中引用 `SprocketModAPI.dll`，并禁止将该依赖复制成模组自己的私有副本。运行时程序集应由游戏 `Mods` 目录中的单个 `SprocketModAPI.dll` 提供。

```xml
<Reference Include="SprocketModAPI">
  <HintPath>$(SprocketGameRoot)\Mods\SprocketModAPI.dll</HintPath>
  <Private>false</Private>
</Reference>
```

在程序集声明中增加 MelonLoader 加载依赖：

```csharp
[assembly: MelonAdditionalDependencies("SprocketModAPI")]
```

## 服务获取与生命周期

`SprocketApi.TryGetService<T>()` 从模块化注册表按公开接口解析服务。当前 Keybindings 模块注册 `IInputService`；模块注销后，该接口会立即停止解析，API 关闭后注册表不会保留服务引用。

```csharp
if (!SprocketApi.TryGetService<IInputService>(out var input))
    return;
```

新增模块通过 Core 的统一生命周期接入，不应把实现堆入 Keybindings 模块。游戏原生 UI 组件服务尚未实现。

## 注册动作

```csharp
using SprocketModAPI;

private IInputActionHandle? toggleAction;

public override void OnInitializeMelon()
{
    if (!SprocketApi.TryGetService<IInputService>(out var input))
        return;

    toggleAction = input!.RegisterAction(new ModActionDefinition
    {
        ModId = "example-mod",
        ActionId = "toggle-overlay",
        DisplayName = "Toggle overlay",
        Category = "Display",
        Description = "Show or hide the example overlay",
        DefaultPrimary = new KeyChord("<Keyboard>/o"),
        DefaultSecondary = default,
        Contexts = InputContextMask.Gameplay,
        Gate = CanToggleOverlay
    });

    toggleAction.Pressed += ToggleOverlay;
}

public override void OnDeinitializeMelon()
{
    toggleAction?.Dispose();
    toggleAction = null;
}
```

`ModId` 和 `ActionId` 必须稳定、非空且不能包含冒号。最终动作 ID 为 `modId:actionId`；发布后更改任一部分会使已有用户配置无法自动关联。

## 绑定

`KeyChord` 保存规范化 Unity control path，而不是用于显示的文本。

```csharp
new KeyChord("<Keyboard>/leftCtrl")
new KeyChord("<Keyboard>/k", ModifierKeys.LeftCtrl | ModifierKeys.LeftShift)
new KeyChord("<Mouse>/middleButton")
```

组合键要求修饰键集合精确匹配。若动作绑定为左 `Ctrl + K`，同时按住额外的 `Shift` 不会触发。管理窗口显示组合键时使用 `LeftCtrl+K` 格式。修饰键自身可作为主键；作为主键时不会再次计入修饰键集合。

动作固定提供槽位 `0` 和 `1`：

```csharp
toggleAction.SetBinding(0, new KeyChord("<Keyboard>/p"));
toggleAction.SetBinding(1, null); // 清空第二槽
toggleAction.RestoreDefaults();
```

传入其他槽位会抛出 `ArgumentOutOfRangeException`。若 Primary 与 Secondary 最终完全相同，API 始终保留 Primary 并自动清空 Secondary。

## 事件和查询

`IInputActionHandle` 提供：

- `Pressed`：动作从未按下变为按下。
- `Released`：动作从按下变为未按下，包括硬门禁触发的释放。
- `Tapped`：在 Unity Input System 默认 tap 时间内完成按下和释放。
- `WasPressedThisFrame` / `WasReleasedThisFrame` / `IsPressed`：逐帧查询。
- `Enabled`：单独启用或禁用动作。

回调在 Unity 主线程派发。API 会捕获并记录单个回调异常，然后继续处理其他动作。

## 上下文与自定义门禁

动作默认仅允许 `Gameplay`。可通过 `Contexts` 显式放行其他上下文，并通过 `Gate` 增加模组自己的状态条件。

```csharp
Contexts = InputContextMask.Gameplay | InputContextMask.Designer,
Gate = () => panelReady && !isEditingText
```

API 的全局硬门禁优先于动作上下文和 `Gate`。游戏失焦、API 管理窗口打开或存在输入 block 时，动作不会绕过门禁。

上下文优先级固定为 `TextInput > Settings > PauseMenu > Designer > MainMenu > Gameplay > OtherMenu`。场景或上下文变化后需要连续两个稳定更新才重新派发。硬门禁释放已按动作时只产生一次 `Released`；解除门禁后仍保持按下的旧物理按键不会补发 `Pressed`，必须先释放再重新按下。`Gate` 按动作独立求值，单个 gate 异常不会中止其他动作更新。

## 临时阻断输入

模组打开自己的模态窗口时，应在窗口生命周期内持有 block：

```csharp
private IDisposable? inputBlock;

void OpenPanel(IInputService input)
{
    inputBlock = input.AcquireInputBlock(this, "example panel open");
}

void ClosePanel()
{
    inputBlock?.Dispose();
    inputBlock = null;
}
```

block 可嵌套。只有全部 block 都释放后，API 动作才会恢复。
