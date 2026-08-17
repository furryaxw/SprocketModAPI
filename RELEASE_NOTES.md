# Sprocket Mod API v0.2.0-fix1

## 本次修复

- 修复退出游戏时，Unity 已先销毁原生主菜单而 MelonLoader 随后执行模组清理所引发的空引用异常。
- 调整 UI 服务关闭顺序，确保原生对象失效后不再执行最后一次 UI 更新。

## 兼容性

- 公共 API 版本保持 `1.1`，依赖 `v0.2.0` 的模组无需重新适配。
- 继续支持 Sprocket `0.2.53.2`、MelonLoader net6、Unity `2022.3.62f2` 和 Windows x64。

## 现有功能

- 提供统一的键盘和鼠标动作注册、双槽绑定、组合键及 modifier-only 支持。
- 提供 `Pressed`、`Released`、`Tapped` 回调和逐帧状态查询。
- 提供焦点、场景、设置页及可嵌套输入 block 门禁，避免失焦卡键和模态窗口输入穿透。
- 在原生 Keymapping 页面提供事件驱动的模组键位入口与管理窗口。
- 提供 `IUiService.StatusChanged` 状态快照、原生主菜单 Menu Button、scope 所有权、主线程派发和场景清理。
- 支持按模组分组、搜索、滚动、按键捕获、Esc 解绑、恢复默认值和 JSON 持久化。

## 安装

将 `SprocketModAPI.dll` 放入《Sprocket》的 `Mods` 目录。依赖该 API 的模组也应安装到同一目录。
