using System;
using System.Collections.Generic;
using System.Reflection;
using MelonLoader;

namespace SprocketModAPI
{
    // 从一个已加载模组读出的原始信息。它只描述“读到了什么”，不做优先级合并，
    // 因此可以在不启动游戏的情况下构造并测试 `ModMetadataReader`。
    internal sealed class LoadedModDescriptor
    {
        internal string MelonName { get; init; } = "";
        internal string MelonVersion { get; init; } = "";
        internal string MelonAuthor { get; init; } = "";
        internal string MelonDownloadLink { get; init; } = "";
        internal string AdditionalCredits { get; init; } = "";
        internal ModKind Kind { get; init; }
        internal string AssemblyName { get; init; } = "";
        internal string AssemblyVersion { get; init; } = "";
        internal string InformationalVersion { get; init; } = "";
        internal string Location { get; init; } = "";
        internal string AssemblyHash { get; init; } = "";
        internal IReadOnlyList<string> Games { get; init; } = Array.Empty<string>();
        internal IReadOnlyList<string> OptionalDependencies { get; init; } = Array.Empty<string>();
        internal IReadOnlyList<string> RequiredDependencies { get; init; } = Array.Empty<string>();
        internal IReadOnlyList<string> IncompatibleAssemblies { get; init; } = Array.Empty<string>();
        internal string MelonLoaderVersion { get; init; } = "";
        internal IReadOnlyDictionary<string, string> Metadata { get; init; } =
            new Dictionary<string, string>(StringComparer.Ordinal);
    }

    internal interface ILoadedModSource
    {
        IReadOnlyList<LoadedModDescriptor> Read();
    }

    // 生产实现：从 `MelonBase.RegisteredMelons` 读取已加载模组。
    // 单个模组读取失败只记录并跳过，不得中断其他模组的元数据读取。
    internal sealed class MelonLoaderModSource : ILoadedModSource
    {
        private readonly Action<string> warn;

        internal MelonLoaderModSource(Action<string> warn)
        {
            this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
        }

        public IReadOnlyList<LoadedModDescriptor> Read()
        {
            var result = new List<LoadedModDescriptor>();
            foreach (MelonBase melon in MelonBase.RegisteredMelons)
            {
                try
                {
                    result.Add(Describe(melon));
                }
                catch (Exception exception)
                {
                    warn($"[SMA-META] Failed to read metadata for a registered melon: {exception}");
                }
            }

            return result;
        }

        private static LoadedModDescriptor Describe(MelonBase melon)
        {
            MelonInfoAttribute? info = melon.Info;
            MelonAssembly? assembly = melon.MelonAssembly;
            Assembly? managed = assembly?.Assembly;

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

            return new LoadedModDescriptor
            {
                MelonName = info?.Name ?? "",
                MelonVersion = info?.Version ?? "",
                MelonAuthor = info?.Author ?? "",
                MelonDownloadLink = info?.DownloadLink ?? "",
                AdditionalCredits = melon.AdditionalCredits?.Credits ?? "",
                Kind = melon is MelonPlugin ? ModKind.Plugin : melon is MelonMod ? ModKind.Mod : ModKind.Unknown,
                AssemblyName = managed?.GetName().Name ?? "",
                AssemblyVersion = managed?.GetName().Version?.ToString() ?? "",
                InformationalVersion = managed?.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion ?? "",
                Location = assembly?.Location ?? "",
                AssemblyHash = assembly?.Hash ?? "",
                Games = DescribeGames(melon),
                OptionalDependencies = melon.OptionalDependencies?.AssemblyNames ?? Array.Empty<string>(),
                // MelonBase 不暴露必需的依赖与不兼容清单，只能直接读程序集特性。
                RequiredDependencies = CollectAssemblyNames<MelonAdditionalDependenciesAttribute>(managed, attribute => attribute.AssemblyNames),
                IncompatibleAssemblies = CollectAssemblyNames<MelonIncompatibleAssembliesAttribute>(managed, attribute => attribute.AssemblyNames),
                MelonLoaderVersion = DescribeLoaderVersion(melon),
                Metadata = metadata
            };
        }

        // 把某个程序集特性里的程序集名收集为去重、去空白后的列表。
        internal static IReadOnlyList<string> CollectAssemblyNames<TAttribute>(Assembly? assembly, Func<TAttribute, string[]> selector)
            where TAttribute : Attribute
        {
            if (assembly == null)
                return Array.Empty<string>();

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (TAttribute attribute in assembly.GetCustomAttributes<TAttribute>())
            {
                string[]? names = selector(attribute);
                if (names == null)
                    continue;

                foreach (string name in names)
                {
                    string trimmed = (name ?? "").Trim();
                    if (trimmed.Length != 0 && seen.Add(trimmed))
                        result.Add(trimmed);
                }
            }

            return result;
        }

        private static IReadOnlyList<string> DescribeGames(MelonBase melon)
        {
            MelonGameAttribute[]? games = melon.Games;
            if (games == null || games.Length == 0)
                return Array.Empty<string>();

            var result = new List<string>(games.Length);
            foreach (MelonGameAttribute game in games)
            {
                if (game == null || game.Universal)
                    continue;
                result.Add($"{game.Developer}/{game.Name}");
            }

            return result;
        }

        private static string DescribeLoaderVersion(MelonBase melon)
        {
            VerifyLoaderVersionAttribute? requirement = melon.SupportedMLVersion;
            if (requirement?.SemVer == null)
                return "";

            return requirement.IsMinimum ? $">= {requirement.SemVer}" : requirement.SemVer.ToString();
        }
    }
}
