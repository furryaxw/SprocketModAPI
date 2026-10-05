# Sprocket Mod API v1.0.0

## 本版变更

- **宿主改为 BepInEx 6 IL2CPP**：目标环境是 Sprocket `0.2.55.5`、Unity `6000.3.21f1`、BepInEx `6.0.0-be.788`（net6）。
  插件入口由 `MelonMod` 换成 `BasePlugin`，程序集标识由 `MelonInfo` 换成 `BepInPlugin`。
- **模组数据移到 `BepInEx\config\SprocketModAPI\`**：键位覆盖与模组配置都在这里；
  原 `UserData\SprocketModAPI\` 下的文件需要手动搬过去。
- **元数据来源改为 BepInEx 已加载插件表**：`Sprocket.Mod.*` 程序集元数据优先，其次 `BepInPlugin`；
  依赖与不兼容声明改为插件 GUID。
- **修复**：
  - 加法加载的场景（`SettingsMenu`、`PauseMenu`、`VehicleControlUI`、`VehicleDesignerUI`）不触发场景回调，
    导致设置页入口不出现、键位上下文判定失效；现在按已加载场景集合的差集发布加载与卸载事件。
  - 按键读取直接读键盘设备：按每个动作动态创建的 `InputAction` 在游戏自己的 InputSystem 配置下读不到按键，
    所有模组键位都不触发。
  - 修饰键比较不再区分大小写，`<Keyboard>/leftCtrl` 与 `<keyboard>/leftctrl` 都能匹配。
  - 模组禁用后可以重新启用：开关按磁盘实际路径操作，不再拿加载时的 `.dll` 路径去改已改名的文件。
  - UI 模块不再在已销毁的对象上访问属性而每帧抛异常；单个模块的更新异常只报告一次并隔离，不再拖垮其余模块。
  - 键位窗口与 Mod 菜单列表的滚动灵敏度。

## 兼容性

- 公共 API 版本仍为 `2.0`：服务接口（`IModConfigService`、`IInputService`、`IUiService`、`IModMetadataService`）签名未变。
- **元数据契约有破坏性变化**：`ModMetadata.Games` 与 `ModMetadata.MelonLoaderVersion` 删除，
  `IncompatibleAssemblies` 改名为 `IncompatiblePlugins`（值是插件 GUID），
  `RequiredDependencies` 与 `OptionalDependencies` 的值也从程序集名改为插件 GUID，
  `ModKind` 只剩 `Unknown` 与 `Plugin`。引用这些成员的模组需要重新编译。
- **宿主要求变化**：MelonLoader 版模组不能在这个宿主下加载；配套模组都在 BepInEx 6 下重新构建。
- 支持环境：Sprocket `0.2.55.5`、BepInEx `6.0.0-be.788`（IL2CPP）、Unity `6000.3.21f1`、Windows x64。
