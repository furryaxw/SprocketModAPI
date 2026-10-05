using System;
using System.Collections.Generic;

namespace SprocketModAPI
{
    // 模组在 BepInEx 中的加载形态；禁用条目形态无法确定时用 `Unknown`。
    public enum ModKind
    {
        Unknown = 0,
        Plugin = 2
    }

    // 单个模组的只读元数据快照。来源与优先级见 `docs/mod-metadata.en.md`：
    // `Sprocket.Mod.*` 程序集元数据优先，其次 `BepInPlugin`，最后退化为程序集名或文件名；
    // 缺失字段一律为空，不抛异常。
    public sealed class ModMetadata
    {
        // 稳定 ID；未声明 `Sprocket.Mod.Id` 时派生为 `file:<程序集名或文件名>`。
        public string Id { get; init; } = "";

        // 是否可与 Registry 按 ID 直接对齐（即声明了 `Sprocket.Mod.Id`）。
        public bool HasDeclaredId => !string.IsNullOrEmpty(RegistryId);

        public string RegistryId { get; init; } = "";
        public string DisplayName { get; init; } = "";
        public string Version { get; init; } = "";
        public IReadOnlyList<string> Authors { get; init; } = Array.Empty<string>();

        // 额外致谢；`BepInPlugin` 没有该字段，只从 `Sprocket.Mod.Credits` 读取。
        public string Credits { get; init; } = "";

        public string Description { get; init; } = "";
        public string Repository { get; init; } = "";
        public string Homepage { get; init; } = "";
        public string Category { get; init; } = "";
        public string License { get; init; } = "";
        public ModKind Kind { get; init; }
        public string AssemblyName { get; init; } = "";

        // 程序集文件路径；禁用条目的路径以 `.disable` 结尾。
        public string Location { get; init; } = "";

        public string AssemblyHash { get; init; } = "";

        // `AssemblyMetadata` 原始键值，键按 `StringComparer.Ordinal` 比较。
        public IReadOnlyDictionary<string, string> RawMetadata { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);

        // 声明依赖的插件 GUID；BepInEx 用 GUID 而不是程序集名表达依赖。
        public IReadOnlyList<string> RequiredDependencies { get; init; } = Array.Empty<string>();

        // 声明软依赖的插件 GUID；缺失不影响加载。
        public IReadOnlyList<string> OptionalDependencies { get; init; } = Array.Empty<string>();

        // 声明互斥的插件 GUID。
        public IReadOnlyList<string> IncompatiblePlugins { get; init; } = Array.Empty<string>();

        // 是否来自磁盘上的 `*.dll.disable`。加载中的模组恒为 `false`；
        // 禁用条目由菜单模块扫描磁盘补齐，本次不加载。
        public bool IsDisabled { get; init; }
    }

    // 只读元数据服务。快照在插件注册/注销后变化，通过 `Changed` 通知。
    // 事件在主线程发布，参数不暴露内部集合。
    public interface IModMetadataService
    {
        // 按 `ModMetadata.DisplayName`（忽略大小写）再按 `ModMetadata.Id` 排序的快照。
        IReadOnlyList<ModMetadata> Entries { get; }

        // 内容与上次发布不同时触发（首次建立快照也算一次），重复刷新且一致时不触发。
        event Action? Changed;

        // 按 `ModMetadata.Id` 精确查找；找不到返回 `null`。
        ModMetadata? Find(string id);

        void Refresh();
    }
}
