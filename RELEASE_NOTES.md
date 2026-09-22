# Sprocket Mod API v0.3.0

## 本版变更

- **模组元数据服务** `IModMetadataService`：从已加载模组读取 `Sprocket.Mod.*` 程序集元数据与 `MelonInfo`，
  缺失字段按程序集名、文件名逐级降级，不联网、不执行第三方代码。见 `docs/mod-metadata.md`。
- **声明式模组配置** `IModConfigService`：模组声明开关、滑条、下拉与文本条目；控件渲染、原子写入、
  损坏文件先备份再重置、逐项降级与变更事件由 API 负责，每个模组一个文件。见 `docs/mod-config-api.md`。
- **游戏内 Mod 菜单**：入口是「设置 → General」左下角的按钮；左侧列表支持多词搜索与 `[CFG]` / `[DISABLED]`
  标记，右侧显示描述、兼容游戏、必需/可选依赖、不兼容程序集、程序集与路径，以及模组声明的配置页，
  并可按约定把模组禁用为 `.dll.disable`（提示重启）。
- **键位默认可以为空**：`ModActionDefinition` 的主/副默认键位允许全空，注册后即为未绑定状态，
  玩家在原生键位窗口自行绑定。
- **模块生命周期按模块隔离**：单个模块初始化或销毁失败只记录错误，其余服务照常可用。

## 兼容性

- 公共 API 版本为 `2.0`，**不再接受 1.x**。`ModId` 由 `init` 改为 `set`，访问器签名变化属于二进制
  破坏性改动：用 1.1 编译的模组必须重新编译，否则调用处会因找不到原访问器而抛
  `MissingMethodException`。请求 `1.x` 的模组会被直接判为不兼容。
- 键位与模组配置文件带自己的 `ConfigVersion`：更旧的版本自动迁移并回写（不再写 `SchemaVersion`
  与 `ApiVersion`），已有绑定与配置保留；比当前更新的文件备份后重置。
- 其余变更为新增接口（模组元数据、声明式配置、游戏内 Mod 菜单）。
- 继续支持 Sprocket `0.2.53.2`、MelonLoader net6、Unity `2022.3.62f2` 和 Windows x64。
