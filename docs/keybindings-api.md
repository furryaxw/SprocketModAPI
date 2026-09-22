# 按键注册 API

通过 `SprocketApi.TryGetService` 获取 `IInputService`，用一个稳定的 `ActionId` 注册动作。

键位的稳定 ID 是 `<ModId>:<ActionId>`，ModID 由 API 从**调用方程序集**的 `Sprocket.Mod.Id` 推断；没有声明该元数据就退化为程序集名。发布后不要修改 `Sprocket.Mod.Id` 与 `ActionId`，否则用户保存的绑定无法自动关联。显式传 `ModId` 仍然有效（覆盖路径）。

```csharp
private IInputActionHandle? toggle;

public override void OnInitializeMelon()
{
    if (!SprocketApi.TryGetService<IInputService>(out var input))
        return;

    toggle = input.RegisterAction(new ModActionDefinition
    {
        // ModId = "example.example-mod", // 不建议
        ActionId = "toggle-example",
        DisplayName = "Toggle example",
        Category = "Example",
        DefaultPrimary = new KeyChord("<Keyboard>/o"),
        Contexts = InputContextMask.Gameplay,
        Gate = () => panelReady
    });
    toggle.Pressed += ToggleExample;
}

public override void OnDeinitializeMelon() => toggle?.Dispose();
```

每个动作提供主、副两个键位槽：

```csharp
toggle.SetBinding(0, new KeyChord("<Keyboard>/p"));
toggle.SetBinding(1, null);
toggle.RestoreDefaults();
```

**默认键位可以全空**：`DefaultPrimary` 与 `DefaultSecondary` 都留空时，动作注册后就是未绑定状态（`Primary.IsEmpty`、界面显示 `Unbound`），玩家自己在键位窗口里绑。

`KeyChord` 使用 Unity Input System control path，例如 `<Keyboard>/k`、`<Mouse>/middleButton`。`ModifierKeys` 支持左右 Shift、Ctrl、Alt，以及 `AnyShift`、`AnyCtrl`、`AnyAlt`。修饰键可单独作为主键。

事件和状态查询包括 `Pressed`、`Released`、`Tapped`、`WasPressedThisFrame`、`WasReleasedThisFrame` 和 `IsPressed`。

上下文优先级为 `TextInput > Settings > PauseMenu > Designer > MainMenu > Gameplay > OtherMenu`。失焦、场景过渡、设置页、文本输入和输入 block 会让已按下动作释放一次；恢复后必须先观察到物理按键释放，动作才会重新布防。

模态窗口应使用 `AcquireInputBlock` 阻止 API 动作，并在窗口关闭时释放返回的 token。

`IInputActionHandle.Enabled` 可独立启用或禁用动作；`Unregister()` 与 `Dispose()` 都会移除动作。`IInputService.ActionsChanged` 在动作列表变化时触发，可用于刷新模组自己的设置 UI。
