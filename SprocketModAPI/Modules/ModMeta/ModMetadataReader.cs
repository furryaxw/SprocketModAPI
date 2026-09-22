using System;
using System.Collections.Generic;
using System.IO;

namespace SprocketModAPI
{
    // 把 `LoadedModDescriptor` 合并为 `ModMetadata`。
    // 纯函数式实现：不触碰 Unity、MelonLoader 或文件系统，可在离线合约测试中直接覆盖。
    // 优先级见 `docs/mod-metadata.md`；任何缺失都降级为空值，不抛异常。
    internal static class ModMetadataReader
    {
        private const string KeyPrefix = "Sprocket.Mod.";

        internal static ModMetadata Read(LoadedModDescriptor descriptor)
        {
            if (descriptor == null)
                throw new ArgumentNullException(nameof(descriptor));

            IReadOnlyDictionary<string, string> metadata = descriptor.Metadata
                ?? new Dictionary<string, string>(StringComparer.Ordinal);

            string fileName = Path.GetFileName(descriptor.Location ?? "");
            string fileStem = Path.GetFileNameWithoutExtension(StripDisableSuffix(fileName));
            string declaredId = Value(metadata, "Id");

            string displayName = FirstNonEmpty(
                Value(metadata, "DisplayName"),
                descriptor.MelonName,
                descriptor.AssemblyName,
                fileStem);
            if (displayName.Length == 0)
                displayName = "UNKNOWN";

            string fallbackId = FirstNonEmpty(descriptor.AssemblyName, fileStem, displayName);

            return new ModMetadata
            {
                Id = declaredId.Length != 0 ? declaredId : $"file:{fallbackId}",
                RegistryId = declaredId,
                DisplayName = displayName,
                Version = FirstNonEmpty(descriptor.MelonVersion, descriptor.InformationalVersion, descriptor.AssemblyVersion),
                Authors = SplitList(FirstNonEmpty(Value(metadata, "Authors"), descriptor.MelonAuthor)),
                Credits = descriptor.AdditionalCredits ?? "",
                Description = Value(metadata, "Description"),
                Repository = Value(metadata, "Repository"),
                Homepage = Value(metadata, "Homepage"),
                Category = Value(metadata, "Category"),
                License = Value(metadata, "License"),
                Kind = descriptor.Kind,
                AssemblyName = descriptor.AssemblyName ?? "",
                Location = descriptor.Location ?? "",
                AssemblyHash = descriptor.AssemblyHash ?? "",
                Games = descriptor.Games ?? Array.Empty<string>(),
                OptionalDependencies = descriptor.OptionalDependencies ?? Array.Empty<string>(),
                RequiredDependencies = descriptor.RequiredDependencies ?? Array.Empty<string>(),
                IncompatibleAssemblies = descriptor.IncompatibleAssemblies ?? Array.Empty<string>(),
                MelonLoaderVersion = descriptor.MelonLoaderVersion ?? "",
                RawMetadata = metadata,
                IsDisabled = false
            };
        }

        // 读取 `Sprocket.Mod.<name>`；键名大小写敏感，值去空白后为空视为缺失。
        private static string Value(IReadOnlyDictionary<string, string> metadata, string name)
            => metadata.TryGetValue(KeyPrefix + name, out string? value) ? (value ?? "").Trim() : "";

        private static string FirstNonEmpty(params string?[] candidates)
        {
            foreach (string? candidate in candidates)
            {
                string trimmed = (candidate ?? "").Trim();
                if (trimmed.Length != 0)
                    return trimmed;
            }

            return "";
        }

        // 逗号分隔列表，去空白、去空项、保序去重。
        internal static IReadOnlyList<string> SplitList(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return Array.Empty<string>();

            var result = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in value.Split(','))
            {
                string trimmed = part.Trim();
                if (trimmed.Length != 0 && seen.Add(trimmed))
                    result.Add(trimmed);
            }

            return result;
        }

        private static string StripDisableSuffix(string fileName)
            => fileName.EndsWith(".disable", StringComparison.OrdinalIgnoreCase)
                ? fileName.Substring(0, fileName.Length - ".disable".Length)
                : fileName;
    }
}
