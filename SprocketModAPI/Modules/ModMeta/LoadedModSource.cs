using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Security.Cryptography;
using BepInEx;
using BepInEx.Unity.IL2CPP;

namespace SprocketModAPI
{
    // 从一个已加载插件读出的原始信息。它只描述“读到了什么”，不做优先级合并，
    // 因此可以在不启动游戏的情况下构造并测试 `ModMetadataReader`。
    internal sealed class LoadedModDescriptor
    {
        internal string PluginName { get; init; } = "";
        internal string PluginVersion { get; init; } = "";
        internal ModKind Kind { get; init; }
        internal string AssemblyName { get; init; } = "";
        internal string AssemblyVersion { get; init; } = "";
        internal string InformationalVersion { get; init; } = "";
        internal string Location { get; init; } = "";
        internal string AssemblyHash { get; init; } = "";
        internal IReadOnlyList<string> OptionalDependencies { get; init; } = Array.Empty<string>();
        internal IReadOnlyList<string> RequiredDependencies { get; init; } = Array.Empty<string>();
        internal IReadOnlyList<string> IncompatiblePlugins { get; init; } = Array.Empty<string>();
        internal IReadOnlyDictionary<string, string> Metadata { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal interface ILoadedModSource
    {
        IReadOnlyList<LoadedModDescriptor> Read();
    }

    // 生产实现：从 BepInEx 已加载的插件表读取。
    // 单个插件读取失败只记录并跳过，不得中断其他插件的元数据读取。
    internal sealed class BepInExPluginSource : ILoadedModSource
    {
        private static readonly Dictionary<string, string> HashCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly object HashLock = new();

        private readonly Action<string> warn;

        internal BepInExPluginSource(Action<string> warn)
        {
            this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
        }

        public IReadOnlyList<LoadedModDescriptor> Read()
        {
            var result = new List<LoadedModDescriptor>();
            foreach (PluginInfo plugin in IL2CPPChainloader.Instance.Plugins.Values)
            {
                try
                {
                    result.Add(Describe(plugin));
                }
                catch (Exception exception)
                {
                    warn($"[SMA-META] Failed to read metadata for a loaded plugin: {exception}");
                }
            }

            return result;
        }

        private static LoadedModDescriptor Describe(PluginInfo plugin)
        {
            BepInPlugin? info = plugin.Metadata;
            Assembly? managed = plugin.Instance?.GetType().Assembly;

            var metadata = new Dictionary<string, string>(StringComparer.Ordinal);
            if (managed != null)
            {
                foreach (AssemblyMetadataAttribute attribute in managed.GetCustomAttributes<AssemblyMetadataAttribute>())
                {
                    // 重复键以第一个为准，保持与 ModMetadataReader 文档一致的确定性。
                    if (!string.IsNullOrEmpty(attribute.Key))
                        metadata.TryAdd(attribute.Key!, attribute.Value ?? "");
                }
            }

            CollectDependencies(plugin, out IReadOnlyList<string> required, out IReadOnlyList<string> optional);
            string location = GetLocation(plugin, managed);

            return new LoadedModDescriptor
            {
                PluginName = info?.Name ?? "",
                PluginVersion = info?.Version?.ToString() ?? "",
                Kind = ModKind.Plugin,
                AssemblyName = managed?.GetName().Name ?? "",
                AssemblyVersion = managed?.GetName().Version?.ToString() ?? "",
                InformationalVersion = managed?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "",
                Location = location,
                AssemblyHash = HashOf(location),
                RequiredDependencies = required,
                OptionalDependencies = optional,
                IncompatiblePlugins = CollectGuids<BepInIncompatibility>(managed, attribute => attribute.IncompatibilityGUID),
                Metadata = metadata
            };
        }

        private static string GetLocation(PluginInfo plugin, Assembly? managed)
        {
            try
            {
                if (!string.IsNullOrEmpty(plugin.Location))
                    return plugin.Location;
            }
            catch (Exception)
            {
                // 插件未提供位置时退回程序集位置。
            }

            return managed?.Location ?? "";
        }

        // `BepInDependency` 用标志位区分硬依赖与软依赖，两者在菜单里分列。
        private static void CollectDependencies(
            PluginInfo plugin,
            out IReadOnlyList<string> required,
            out IReadOnlyList<string> optional)
        {
            var requiredGuids = new List<string>();
            var optionalGuids = new List<string>();
            var requiredSeen = new HashSet<string>(StringComparer.Ordinal);
            var optionalSeen = new HashSet<string>(StringComparer.Ordinal);

            IEnumerable<BepInDependency>? dependencies = null;
            try
            {
                dependencies = plugin.Dependencies;
            }
            catch (Exception)
            {
                // 依赖表不可读时按“无依赖”处理，不影响其余元数据。
            }

            if (dependencies != null)
            {
                foreach (BepInDependency dependency in dependencies)
                {
                    if (dependency == null)
                        continue;

                    string guid = (dependency.DependencyGUID ?? "").Trim();
                    if (guid.Length == 0)
                        continue;

                    if (dependency.Flags == BepInDependency.DependencyFlags.HardDependency)
                    {
                        if (requiredSeen.Add(guid))
                            requiredGuids.Add(guid);
                    }
                    else if (optionalSeen.Add(guid))
                    {
                        optionalGuids.Add(guid);
                    }
                }
            }

            required = requiredGuids;
            optional = optionalGuids;
        }

        // 把某个程序集特性里的 GUID 收集为去重、去空白后的列表。
        internal static IReadOnlyList<string> CollectGuids<TAttribute>(Assembly? assembly, Func<TAttribute, string> selector)
            where TAttribute : Attribute
        {
            if (assembly == null)
                return Array.Empty<string>();

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TAttribute attribute in assembly.GetCustomAttributes<TAttribute>())
            {
                string? guid = selector(attribute);
                string trimmed = (guid ?? "").Trim();
                if (trimmed.Length != 0 && seen.Add(trimmed))
                    result.Add(trimmed);
            }

            return result;
        }

        // 程序集文件的内容哈希；同一个路径只算一次。
        private static string HashOf(string path)
        {
            if (string.IsNullOrEmpty(path))
                return "";

            lock (HashLock)
            {
                if (HashCache.TryGetValue(path, out string? cached))
                    return cached;
            }

            string hash = "";
            try
            {
                using FileStream stream = File.OpenRead(path);
                using SHA256 sha = SHA256.Create();
                hash = Convert.ToHexString(sha.ComputeHash(stream));
            }
            catch (Exception)
            {
                // 读不到文件时留空，菜单退化为不显示哈希。
            }

            lock (HashLock)
            {
                HashCache[path] = hash;
            }

            return hash;
        }
    }
}
