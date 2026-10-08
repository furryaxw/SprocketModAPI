# Sprocket Mod API v1.1.0

## 本版变更

- **新增 `IModRuntimeService`**：插件把每帧、晚帧、GUI、场景变化与进程退出回调登记在 API 的驱动组件上（BepInEx 不给模组帧循环），并可借它启动协程；单个回调抛异常时只停用该登记并报一次，其余登记继续。
- **新增 `IModLogService`**：插件按显示名取得 `IModLogger`，日志进 BepInEx 日志源。
- **原生主菜单按钮改为"登记 + 原生面板就绪后创建"**：
  - 请求只登记意图；服务在原生 `MenuPanel` 画出按钮、`BelowNativeButtonText` 指定的锚点就位时，用 `MenuPanel.Button` 把按钮加进游戏自己的 `Tab` 按钮池。
  - 一个句柄在同一个面板实例里维持恰好一个按钮；原生切屏 `ReturnAllToPool` + 重画之后，服务为同一个句柄重建这一个按钮。
  - 锚点还没出现时按钮保持登记状态，不会落到原生按钮列表末尾。
  - 删除"主菜单面板不可用时把 `buttonPrefab` 克隆到调用方 `Parent`"的能力：`Parent` 现在只做宿主 Canvas 校验（与 `BelowNativeButtonText` 至少给一个）。

## 兼容性

- 公共 API 版本仍为 `2.0`：`IInputService`、`IUiService`、`IModConfigService`、`IModMetadataService` 的签名未变，新增服务是纯增量。
- **行为删除**：`CreateMenuButtonAsync` / `CreateMainMenuButtonAsync` 不再在调用方给的 `Parent` 下克隆原生 `Tab`，也不再因锚点缺失而在建出按钮后返回 `TemplateNotFound`。依赖"面板未就绪时克隆一个按钮"的模组需要改用原生路径：服务会在面板就绪后自动创建。
- `UiFailureCode.TemplateNotFound` 仍在枚举里，但后端不再产生它；主菜单场景未加载时 `CreateMenuButtonAsync` 返回 `SceneUnavailable`。
- 配置与键位文件的目录、schema 与格式不变。
- 支持环境：Sprocket `0.2.55.5`、BepInEx `6.0.0-be.788`（IL2CPP）、Unity `6000.3.21f1`、Windows x64。

<!-- sp-compat {"hamish.sprocket": "0.2.55.5", "bepinex.bepinex": "6.0.0-be.788"} -->
