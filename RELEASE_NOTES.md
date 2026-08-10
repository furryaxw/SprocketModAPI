# Sprocket Mod API v0.2.0

## API 1.1

- 新增 `IUiService.StatusChanged`，提供场景、能力、主菜单 ready 状态和 generation 的只读快照；订阅者异常相互隔离。
- UI 生命周期诊断统一改由 `UserData/SprocketModAPI/ui.debug.json` 控制，默认不输出 `[SMA-UI-TRACE]`。
- 仅保留基于已验证 Sprocket Tab 模板的 Menu Button 句柄；普通 Button 因实机异常已撤回。
- 将公共文档拆分为按键注册和 UI 注册，并补充原生主菜单高级位置接口。
- 新增 scope 所有权、结构化创建失败、主线程派发和场景清理。
- Selection、Prompt 及其他原生 UI 适配器仍属于后续工作。

首个公开版本。

## 功能

- 为模组提供统一的键盘和鼠标动作注册、双槽绑定、组合键和 modifier-only 支持。
- 提供 `Pressed`、`Released`、`Tapped` 回调及逐帧状态查询。
- 提供焦点、场景、设置页和可嵌套输入 block 门禁，避免失焦后卡键或模态窗口输入穿透。
- 在原生 Keymapping 页面提供事件驱动的模组键位入口和模态管理窗口。
- 支持按模组分组、搜索、滚动、捕获、Esc 解绑、恢复默认值和 JSON 持久化。
- 迁移 Sprocket Laser Rangefinder 的 Ctrl/Z/L 与 Sprocket Thermal 的 N 用户键位。

## 安装

将 `SprocketModAPI.dll` 放入《Sprocket》的 `Mods` 目录。依赖模组也必须安装到同一目录。

## 已验证环境

- Sprocket `0.2.53.2`
- MelonLoader net6
- Unity `2022.3.62f2`
- Windows x64

## 验证状态

- Release 构建、离线合约测试、部署文件哈希和程序集版本已验证。
- 最终 UI 微调后的完整游戏内视觉验收仍需单独完成；静态构建不代表所有分辨率均已验收。
