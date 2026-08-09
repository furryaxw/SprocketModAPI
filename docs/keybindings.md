# 键位管理

## 管理窗口

API 监听 `Settings Menu/Content/Content` 的子节点变化；发现 `Keymapping` 后，再监听该页面的启用和禁用事件。只有 Keymapping 页面激活时才显示 `MOD KEYBINDINGS` 入口。入口左边界通过 `Action buttons` 的 RectTransform 事件保持对齐，不使用逐帧层级轮询。

窗口按 `ModId` 分组显示已注册动作，提供搜索、滚动、两个绑定槽和单动作恢复默认值。模态背景拦截鼠标射线；窗口或捕获状态激活时，API 会暂存原生 Keymapping 页中全部 `Selectable` 的 `interactable` 与 navigation、EventSystem 当前选中对象和导航事件状态，临时禁用原生交互，并在关闭、取消、页面停用、场景卸载或模块销毁时恢复快照。

点击绑定槽后进入捕获状态：

- 目标槽显示 `PRESS A KEY...`。
- 窗口显示 `LISTENING FOR INPUT | ESC TO UNBIND`。
- 按普通键或鼠标键保存新绑定。
- 先按修饰键、再按主键可保存组合键。
- 单独按下并释放修饰键可将修饰键自身设为主键。
- 按 `Esc` 清空当前槽位。
- 点击 Reset 列中的按钮恢复该动作的两个默认槽位。

捕获开始时会忽略触发绑定按钮的鼠标点击，避免把该点击误录为鼠标左键。

同一动作的 Primary 和 Secondary 不允许保存完全相同的 `KeyChord`。配置加载、改绑或恢复默认后若两槽相同，API 保留 Primary 并自动清空 Secondary；相同主键但修饰键集合不同仍视为不同绑定。

## 冲突警告

API 为所有已注册动作的两个绑定槽建立精确 `KeyChord` 索引，并以只读方式扫描当前加载的游戏 `InputActionAsset`。管理窗口在动作名称下按 `Conflict:`、槽位与绑定分组逐行显示所有来源；模组来源使用动作名称，原生来源使用 `GAME ActionMap/Action`。

冲突只警告，不阻止捕获、解绑、恢复默认或保存。动作注册、注销、绑定变化和恢复默认后会立即重建索引；场景加载和每次打开管理窗口时会重新读取当前原生资产。API 不启用、禁用或修改游戏的 `InputAction`。

原生简单绑定与能明确解析主键及左右修饰键的复合绑定会参与索引。无法可靠映射的设备或复合绑定会被跳过，避免报告推测性冲突。

## 配置文件

配置保存在：

```text
UserData\SprocketModAPI\keybindings.json
```

文件包含 schema 版本、API 版本和以稳定动作 ID 为键的绑定覆盖。API 只保存与当前默认值不同的槽位；未覆盖的槽位会继续跟随模组后续版本提供的新默认值。

显式清空会保存为 `null`。模组暂时卸载时，已保存配置仍会保留；使用相同稳定动作 ID 重新安装后会恢复。

加载时要求 schema 版本为 `1`，配置 API 版本与当前公共 API 兼容。根结构损坏、schema 不支持或 API 版本不兼容时，原文件会先保存为：

```text
keybindings.json.corrupt-YYYYMMDD-HHMMSS-fff.bak
```

随后 API 原子写回可用的空配置，因此损坏文件不会阻止动作注册。单个非法动作、未知槽位或非法槽值会逐项记录并移除，同一文件中的其他有效覆盖继续加载并写回清洗后的配置。

写入时先生成 `.tmp` 文件，再替换正式文件。显式 `null`、未覆盖槽位跟随新默认值以及未注册动作配置保留语义保持不变。

## 输入门禁

API 不修改游戏自身的 InputAction，只控制通过 `IInputService` 注册的模组动作。

上下文由场景集合、菜单激活观察器和 EventSystem 当前选中对象共同解析。Sprocket `0.2.53.2` 在设计器和驾驶状态下保持 `Sandbox` 为 active scene，因此 API 分别以附加场景 `VehicleDesignerUI` 和 `VehicleControlUI` 识别 `Designer` 与 `Gameplay`。Settings 和 PauseMenu 使用激活生命周期观察器，文本输入则检查当前选中的 TMP 或 Unity InputField。固定优先级为：`TextInput > Settings > PauseMenu > Designer > MainMenu > Gameplay > OtherMenu`。

场景加载、卸载或上下文变化会立即进入 transition blocked 状态。只有连续两个 Update 观察到相同上下文后才恢复普通动作派发；稳定前只允许动作状态收敛，不产生新的 `Pressed`。

以下情况会抑制 API 动作：

- 游戏窗口失去焦点。
- 游戏设置界面、文本输入或模组键位窗口打开。
- 暂停菜单覆盖 Gameplay。
- 场景切换后的状态清理阶段。
- 任一模组持有 `AcquireInputBlock` 返回的 block。

若动作在门禁生效前处于按下状态，API 会派发一次 `Released` 并清理 held 状态，`WasReleasedThisFrame` 只在该帧为 `true`。恢复焦点或解除门禁时不会补发旧物理按键；必须先观察到完整释放，后续重新按下才会产生新的 `Pressed`。单个动作的 `Gate` 异常只禁用该动作当前帧，并记录稳定动作 ID 和完整异常，不阻断其他动作。

## 支持范围

v1 支持键盘、鼠标、单键和精确修饰键组合。数据类型保留 Unity control path，后续可增加其他输入设备而不更换动作 ID，但当前管理窗口和验证范围不包含手柄。

冲突判断要求 control path 和精确修饰键集合相同。当前不做按场景或 Action Map 上下文消歧，因此同一原生绑定即使只在另一游戏上下文使用，也会显示为警告。
