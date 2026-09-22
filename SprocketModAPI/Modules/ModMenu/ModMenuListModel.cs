using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace SprocketModAPI
{
    // 菜单列表里的一行（已把元数据、禁用状态和配置页合并好）。
    internal sealed class ModMenuRow
    {
        internal string Id { get; init; } = "";
        internal string DisplayName { get; init; } = "";
        internal string Version { get; init; } = "";
        internal string Authors { get; init; } = "";
        internal string Credits { get; init; } = "";
        internal string Description { get; init; } = "";
        internal string Repository { get; init; } = "";
        internal string Homepage { get; init; } = "";
        internal string Category { get; init; } = "";        internal string License { get; init; } = "";
        internal ModKind Kind { get; init; }
        internal string AssemblyName { get; init; } = "";
        internal string Location { get; init; } = "";
        internal string AssemblyHash { get; init; } = "";
        internal IReadOnlyList<string> Games { get; init; } = Array.Empty<string>();
        internal IReadOnlyList<string> OptionalDependencies { get; init; } = Array.Empty<string>();
        internal IReadOnlyList<string> RequiredDependencies { get; init; } = Array.Empty<string>();
        internal IReadOnlyList<string> IncompatibleAssemblies { get; init; } = Array.Empty<string>();

        // 必需依赖里本机找不到的程序集；非空表示这个模组可能加载异常。
        internal IReadOnlyList<string> MissingDependencies { get; init; } = Array.Empty<string>();

        // 声明的「不兼容程序集」里，本机确实存在的那一个（存在即冲突）。
        internal bool HasIncompatiblePresent { get; init; }
        internal bool IsDisabled { get; init; }

        // 该模组注册的配置页 ID；空表示没有配置页。
        internal string ConfigModId { get; init; } = "";

        internal bool HasConfigPage => ConfigModId.Length != 0;

        // 预拼的小写搜索文本，供 `ModMenuListModel.Filter` 使用。
        internal string SearchText { get; init; } = "";

        internal string KindLabel => IsDisabled
            ? "Disabled"
            : Kind == ModKind.Plugin ? "Plugin" : Kind == ModKind.Mod ? "Mod" : "Unknown";
    }

    // 菜单列表的纯逻辑：合并「已加载元数据 + 禁用文件 + 配置页注册」，并提供搜索过滤。
    // 不接触 Unity，可离线测试。
    internal static class ModMenuListModel
    {
        internal static IReadOnlyList<ModMenuRow> Build(
            IReadOnlyList<ModMetadata> loaded,
            IReadOnlyList<string> disabledPaths,
            IReadOnlyCollection<string> configModIds,
            IReadOnlyCollection<string>? knownAssemblies = null)
        {
            var configIds = new HashSet<string>(configModIds ?? Array.Empty<string>(), StringComparer.Ordinal);
            var rows = new List<ModMenuRow>();
            var loadedLocations = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var known = new HashSet<string>(knownAssemblies ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);

            foreach (ModMetadata metadata in loaded ?? Array.Empty<ModMetadata>())
            {
                if (metadata == null)
                    continue;

                if (!string.IsNullOrEmpty(metadata.Location))
                {
                    loadedLocations.Add(metadata.Location);
                    loadedLocations.Add(ModFileToggle.ActualPathFor(metadata.Location));
                    AddKnown(known, ModAssemblyIndex.Stem(metadata.Location));
                }

                AddKnown(known, metadata.AssemblyName);
                AddKnown(known, metadata.DisplayName);
            }

            foreach (string path in disabledPaths ?? Array.Empty<string>())
                AddKnown(known, ModAssemblyIndex.Stem(path));

            foreach (ModMetadata metadata in loaded ?? Array.Empty<ModMetadata>())
            {
                if (metadata == null)
                    continue;
                rows.Add(BuildLoadedRow(metadata, configIds, known,
                    ModFileToggle.IsDisabledPath(ModFileToggle.ActualPathFor(metadata.Location))));
            }

            foreach (string path in disabledPaths ?? Array.Empty<string>())
            {
                if (string.IsNullOrEmpty(path) || loadedLocations.Contains(path))
                    continue;

                rows.Add(BuildDisabledRow(path));
            }

            return Sort(rows);
        }

        // 空白查询返回全部；否则要求每个空白分隔的词都命中（忽略大小写）。
        internal static IReadOnlyList<ModMenuRow> Filter(IReadOnlyList<ModMenuRow> rows, string? query)
        {
            if (rows == null || rows.Count == 0)
                return Array.Empty<ModMenuRow>();
            if (string.IsNullOrWhiteSpace(query))
                return rows;

            string[] tokens = query!.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (tokens.Length == 0)
                return rows;

            var result = new List<ModMenuRow>();
            foreach (ModMenuRow row in rows)
            {
                bool matches = true;
                foreach (string token in tokens)
                {
                    if (row.SearchText.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        matches = false;
                        break;
                    }
                }

                if (matches)
                    result.Add(row);
            }

            return result;
        }

        // 已启用的模组排在前面，各自再按显示名（忽略大小写）和 ID 排序。
        internal static IReadOnlyList<ModMenuRow> Sort(IReadOnlyList<ModMenuRow> rows)
        {
            var ordered = rows.ToList();
            ordered.Sort(static (left, right) =>
            {
                if (left.IsDisabled != right.IsDisabled)
                    return left.IsDisabled ? 1 : -1;

                int byName = string.Compare(left.DisplayName, right.DisplayName, StringComparison.OrdinalIgnoreCase);
                return byName != 0 ? byName : string.Compare(left.Id, right.Id, StringComparison.Ordinal);
            });

            return ordered;
        }

        private static ModMenuRow BuildLoadedRow(ModMetadata metadata, HashSet<string> configIds, HashSet<string> knownAssemblies, bool isDisabled)
        {
            string authors = string.Join(", ", metadata.Authors);
            string configModId = ResolveConfigModId(metadata, configIds);
            string kindLabel = metadata.Kind == ModKind.Plugin ? "Plugin" : metadata.Kind == ModKind.Mod ? "Mod" : "Unknown";
            IReadOnlyList<string> missing = FindMissingDependencies(metadata, knownAssemblies);

            return new ModMenuRow
            {
                Id = metadata.Id,
                DisplayName = metadata.DisplayName,
                Version = metadata.Version,
                Authors = authors,
                Credits = metadata.Credits,
                Description = metadata.Description,
                Repository = metadata.Repository,
                Homepage = metadata.Homepage,
                Category = metadata.Category,
                License = metadata.License,
                Kind = metadata.Kind,
                AssemblyName = metadata.AssemblyName,
                Location = metadata.Location,
                AssemblyHash = metadata.AssemblyHash,
                Games = metadata.Games,
                OptionalDependencies = metadata.OptionalDependencies,
                RequiredDependencies = metadata.RequiredDependencies,
                IncompatibleAssemblies = metadata.IncompatibleAssemblies,
                MissingDependencies = missing,
                HasIncompatiblePresent = metadata.IncompatibleAssemblies.Any(name => knownAssemblies.Contains(name)),
                IsDisabled = isDisabled,
                ConfigModId = configModId,
                SearchText = Join(metadata.Id, metadata.DisplayName)
            };
        }

        // 必需依赖里本机找不到的程序集；自身名字与可选依赖都不参与判断。
        internal static IReadOnlyList<string> FindMissingDependencies(ModMetadata metadata, IReadOnlyCollection<string> knownAssemblies)
        {
            if (metadata.RequiredDependencies.Count == 0)
                return Array.Empty<string>();

            var known = knownAssemblies as HashSet<string> ?? new HashSet<string>(knownAssemblies ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
            var missing = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string dependency in metadata.RequiredDependencies)
            {
                if (string.IsNullOrWhiteSpace(dependency))
                    continue;
                string name = dependency.Trim();
                if (name.Equals(metadata.AssemblyName, StringComparison.OrdinalIgnoreCase)
                    || name.Equals(metadata.DisplayName, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!known.Contains(name) && seen.Add(name))
                    missing.Add(name);
            }

            return missing;
        }

        private static void AddKnown(HashSet<string> known, string? name)
        {
            string trimmed = (name ?? "").Trim();
            if (trimmed.Length != 0)
                known.Add(trimmed);
        }

        // 禁用行：状态来自磁盘（`.dll.disable`），名字就是文件名 —— 不另存任何记录。
        private static ModMenuRow BuildDisabledRow(string path)
        {
            string stem = Path.GetFileNameWithoutExtension(ModFileToggle.EnabledPathFor(path));

            return new ModMenuRow
            {
                Id = $"file:{stem}",
                DisplayName = stem,
                Version = "",
                Authors = "",
                Kind = ModKind.Unknown,
                Location = path,
                IsDisabled = true,
                ConfigModId = "",
                SearchText = Join($"file:{stem}", stem)
            };
        }

        internal static string ResolveConfigModId(ModMetadata metadata, IReadOnlyCollection<string> configModIds)
        {
            if (configModIds == null || configModIds.Count == 0)
                return "";

            var candidates = new List<string>();
            AddCandidate(candidates, metadata.Id);
            AddCandidate(candidates, Suffix(metadata.Id));
            AddCandidate(candidates, metadata.AssemblyName);

            foreach (string candidate in candidates)
            {
                foreach (string configId in configModIds)
                {
                    if (string.Equals(configId, candidate, StringComparison.Ordinal))
                        return configId;
                }
            }

            return "";
        }

        private static void AddCandidate(List<string> candidates, string? value)
        {
            if (!string.IsNullOrEmpty(value) && !candidates.Contains(value))
                candidates.Add(value);
        }

        private static string Suffix(string id)
        {
            int separator = id.LastIndexOf('.');
            return separator >= 0 && separator < id.Length - 1 ? id.Substring(separator + 1) : id;
        }

        private static string Join(params string?[] parts)
            => string.Join("\u0001", parts.Where(part => !string.IsNullOrEmpty(part))).ToLowerInvariant();
    }
}
