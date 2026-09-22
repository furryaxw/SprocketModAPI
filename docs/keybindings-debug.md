# 键位自助排障

本指南用于排查依赖 Sprocket Mod API 的模组键位无法触发、只在部分场景无法触发或与预期组合键不一致的问题。

## 准备工作

排障前先通过模组管理器把相关模组更新到当前版本。

如果需要把日志发给开发者，请在模组管理器的**设置页**找到 **MelonLoader** 区域，点击“上传 Latest.log”。确认公开上传后，管理器会读取 `MelonLoader\Latest.log` 的末尾最多 8 MiB，上传原文并显示可复制的分享链接；将该链接一并提供。该操作不会自动执行，也不会自动脱敏。

## 开启调试

打开游戏内 Mod 菜单（「设置 → General」左下角的 `MODS` 按钮），在 `Sprocket Mod API` 的配置页进入 `Keybinding diagnostics` 区域，打开 `Keybinding debug log`。`Keybinding debug: routing` 决定是否输出路由日志，`Keybinding debug: bindings` 决定是否输出动作日志。

```json
{
  "Enabled": true,
  "LogRouting": true,
  "LogBindings": true,
  "LogEveryFrame": false
}
```

开关每次输出前重新读取，改动立即生效。

`Keybinding debug: every frame` 默认应保持关闭。只有在开发者要求时才开启它，因为日志量会明显增加。

## 推荐复现步骤

1. 确保相关模组已更新到最新版本。
2. 进入需要使用该键位的场景，例如驾驶状态的 `Gameplay`。
3. 确保没有打开设置、文字输入框、暂停菜单或模组键位管理窗口。
4. 先松开所有 Shift、Ctrl 和 Alt，再单独短按目标键一次。
5. 在模组管理器的**设置页**，于 **MelonLoader** 区域点击“上传 Latest.log”，确认公开上传后复制生成的链接。
6. 向模组作者说明按键、场景和预期动作，并附上分享链接。

## 如何阅读日志

### 路由日志

```text
[SMA-KEY-ROUTE] scene=Sandbox, context=Gameplay, stable=True,
focused=True, externalBlock=False, blocks=0
```

这是可以派发普通 Gameplay 动作的正常状态。字段含义：

- `context=Gameplay`：当前位于该动作允许的游戏上下文；动作也可能只允许 `Designer`、`MainMenu` 等其他上下文。
- `stable=True`：场景或菜单切换后的两帧稳定期已经结束。
- `focused=True`：游戏窗口拥有键盘焦点。
- `externalBlock=False` 与 `blocks=0`：没有模组窗口或其他模组正在阻断 API 输入。

以下状态会刻意抑制动作：`focused=False`、`context=Settings`、
`context=TextInput`、`externalBlock=True`、`blocks` 大于零，或 `stable=False`。
关闭对应窗口、松开旧按键后再重新按下即可验证恢复情况。

### 动作日志

```text
[SMA-KEY] action=example:toggle, primaryRaw=True, secondaryRaw=False,
physical=False, enabled=True, context=Gameplay, contextMatch=True,
allowed=True, modifiers=LeftShift, primary=<keyboard>/l|0
```

- `primaryRaw=True` 或 `secondaryRaw=True`：Unity Input System 已收到目标主键；这证明输入确实进入 API。
- `physical=True`：主键和修饰键都符合绑定，动作可继续进入派发判定。
- `enabled=True`：动作没有被所属模组禁用。
- `contextMatch=True`：动作允许当前上下文。
- `allowed=True`：所属模组的可选 `Gate` 允许该动作。

动作实际触发必须同时满足 `physical=True` 和 `allowed=True`，并且路由日志处于可派发状态。

## 常见结果与处理

| 日志现象 | 含义 | 自助处理 |
| --- | --- | --- |
| 没有任何 `[SMA-KEY-ROUTE]` | 模组没有运行到输入更新，或日志不是本次启动产生 | 更新模组并重新启动游戏，再上传新的 `Latest.log` |
| 有路由日志但没有目标 `action=` | 对应模组未注册该动作，或没有按到该动作的主键 | 确认相关模组已启用，重新查看键位管理中的绑定 |
| `primaryRaw=True`，但 `physical=False` | 主键被收到，但修饰键集合不匹配 | 松开 Shift/Ctrl/Alt 后重试，或在键位管理中将组合键重新录制为实际想用的组合 |
| `primary=<keyboard>/l|0` 且 `modifiers=LeftShift` | 绑定是 `L`，实际按下的是 `Shift+L` | 单独按 `L`；若需要 `Shift+L`，录制 `LeftShift+L` |
| `contextMatch=False` | 动作不允许当前场景或菜单上下文 | 在模组规定的场景使用，或联系模组作者调整动作上下文 |
| `allowed=False` | 模组自身 Gate 拒绝动作 | 检查该模组前置条件；这不是 API 输入丢失 |
| `suppressed=true` | API 正被焦点、设置页、文本输入、窗口或输入 block 抑制 | 关闭 UI、回到游戏窗口并先松开按键后重试 |

## 修饰键规则

键位按**精确修饰键组合**匹配。无修饰键绑定 `L` 只匹配单独的 `L`；
`Shift+L`、`Ctrl+L` 和 `Alt+L` 都是不同的绑定。左右 Shift、Ctrl 和 Alt 也会分别记录。

如需组合键，请在游戏内 `MOD KEYBINDINGS` 中先按住修饰键、再按主键进行录制。不要依赖大写字母：输入系统会将 `Shift+L` 视为组合键，而不是普通 `L`。

## 反馈给开发者时请附上

- 模组管理器生成的 `Latest.log` 分享链接。
- 已配置的键位、实际按下的键（包括左右 Shift/Ctrl/Alt）、发生场景和预期行为。
- 是否只在设置页、设计器、驾驶、暂停菜单或失焦后发生。

---

# Keybinding Self-Troubleshooting

This guide helps diagnose mod keybindings that do not trigger, only work in some scenes, or behave differently from the expected key combination.

## Before you start

Update the affected mods through the mod manager and make sure you are testing the latest available version. Do not continue troubleshooting an old DLL.

If you need to send logs to the developer, open the mod manager's **Settings** page, find the **MelonLoader** section, and click **Upload Latest.log**. After you confirm the public upload, the manager reads up to the final 8 MiB of `MelonLoader\Latest.log`, uploads the raw text, and shows a link you can copy. Send that link with the report. Uploading is never automatic, and the log is not redacted automatically.

## Enable debugging

Open the in-game mod menu (the `MODS` button at the bottom left of **Settings → General**), open the `Sprocket Mod API` config page, and switch on `Keybinding debug log` under `Keybinding diagnostics`. `Keybinding debug: routing` selects the routing entries and `Keybinding debug: bindings` selects the action entries.

```json
{
  "Enabled": true,
  "LogRouting": true,
  "LogBindings": true,
  "LogEveryFrame": false
}
```

Every switch is re-read before each write, so a change takes effect immediately.

Keep `Keybinding debug: every frame` off unless the developer asks for it; enabling it can produce a large log.

## Recommended reproduction

1. Confirm that the affected mods are up to date.
2. Enter the scene where the key should work, such as `Gameplay` while driving.
3. Close settings, text fields, pause menus, and the mod keybinding window.
4. Release every Shift, Ctrl, and Alt key, then tap the target key by itself.
5. On the mod manager's **Settings** page, use **Upload Latest.log** in the **MelonLoader** section, confirm the public upload, and copy the generated link.
6. Tell the developer the key pressed, scene, and expected action, and include the link.

## Reading the log

### Routing entries

```text
[SMA-KEY-ROUTE] scene=Sandbox, context=Gameplay, stable=True,
focused=True, externalBlock=False, blocks=0
```

This is the normal state for dispatching a Gameplay action:

- `context=Gameplay`: the current context; an action may instead require `Designer`, `MainMenu`, or another context.
- `stable=True`: the post-transition stabilization period has completed.
- `focused=True`: the game window owns keyboard focus.
- `externalBlock=False` and `blocks=0`: no mod window or input block is suppressing API actions.

The following states intentionally suppress actions: `focused=False`, `context=Settings`, `context=TextInput`, `externalBlock=True`, a non-zero `blocks` value, or `stable=False`. Close the relevant UI and release the old key before trying again.

### Action entries

```text
[SMA-KEY] action=example:toggle, primaryRaw=True, secondaryRaw=False,
physical=False, enabled=True, context=Gameplay, contextMatch=True,
allowed=True, modifiers=LeftShift, primary=<keyboard>/l|0
```

- `primaryRaw=True` or `secondaryRaw=True`: Unity's Input System received the target control; input reached the API.
- `physical=True`: the control and modifier set match the binding.
- `enabled=True`: the owning mod has not disabled the action.
- `contextMatch=True`: the action allows the current context.
- `allowed=True`: the owning mod's optional `Gate` allows the action.

An action can trigger only when `physical=True`, `allowed=True`, and routing is dispatchable.

## Common results

| Log pattern | Meaning | Self-service action |
| --- | --- | --- |
| No `[SMA-KEY-ROUTE]` entries | The mod never reached input update, or the log is from another run | Update the mod, restart the game, and upload a fresh `Latest.log` |
| Routing entries but no target `action=` | The action was not registered, or its primary control was not pressed | Confirm the mod is enabled and review its binding in the keybinding manager |
| `primaryRaw=True` but `physical=False` | The control arrived but modifiers do not match | Release Shift/Ctrl/Alt and retry, or record the intended combination again |
| `primary=<keyboard>/l|0` with `modifiers=LeftShift` | The binding is `L`, but the input is `Shift+L` | Press `L` alone; record `LeftShift+L` if that is intended |
| `contextMatch=False` | The action is not allowed in the current scene or menu | Use the action in its supported context or ask the mod author to adjust it |
| `allowed=False` | The owning mod's Gate rejected the action | Check that mod's prerequisites; this is not API input loss |
| `suppressed=true` | Focus, settings, text input, a window, or an input block is suppressing the API | Close the UI, refocus the game, release the old key, and retry |

## Modifier rules

Bindings use **exact modifier matching**. An unmodified `L` binding matches only `L`; `Shift+L`, `Ctrl+L`, and `Alt+L` are different bindings. Left and right Shift, Ctrl, and Alt are also tracked separately.

For a combination, open `MOD KEYBINDINGS`, hold the modifier first, and press the main key while recording. Do not rely on uppercase letters: the Input System treats `Shift+L` as a combination rather than plain `L`.

## What to send the developer

- The `Latest.log` share link generated by the mod manager.
- Configured binding, actual key pressed (including left/right Shift/Ctrl/Alt), scene, and expected behavior.
- Whether the problem occurs only in settings, the designer, driving, a pause menu, or after focus is lost.
