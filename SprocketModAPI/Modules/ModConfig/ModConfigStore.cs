using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SprocketModAPI
{
    // 每个模组一个配置文件：`<root>/<modId>.json`。
    // 与 `KeybindingStore` 相同的约定：schema/API 版本校验、损坏文件先备份再重置、
    // 逐项降级（单个值非法只丢弃该项）、原子写入。
    internal sealed class ModConfigStore
    {
        private readonly string directory;
        private readonly Action<string> warn;
        private readonly Func<DateTime> utcNow;

        internal ModConfigStore(string directory, Action<string> warn, Func<DateTime>? utcNow = null)
        {
            this.directory = directory ?? throw new ArgumentNullException(nameof(directory));
            this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        internal string FilePathFor(string modId) => Path.Combine(directory, Sanitize(modId) + ".json");

        // 读取全部键值（含本模组已不再声明的键），失败时返回空集合并保证文件可写。
        internal Dictionary<string, object> Load(string modId)
        {
            string filePath = FilePathFor(modId);
            if (!File.Exists(filePath))
                return new Dictionary<string, object>(StringComparer.Ordinal);

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
                JsonElement root = document.RootElement;
                Version fileVersion = ValidateRoot(root, filePath);

                JsonElement valuesElement = GetRequiredProperty(root, "Values");
                if (valuesElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Values must be a JSON object.");

                bool cleaned = false;
                var values = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (JsonProperty property in valuesElement.EnumerateObject())
                {
                    if (TryReadValue(property.Value, out object? value))
                    {
                        values[property.Name] = value!;
                        continue;
                    }

                    cleaned = true;
                    warn($"[SMA-CONFIG] ignored unsupported value {modId}:{property.Name} ({property.Value.ValueKind}).");
                }

                bool hasLegacyFields = false;
                foreach (JsonProperty property in root.EnumerateObject())
                {
                    if (string.Equals(property.Name, "SchemaVersion", StringComparison.OrdinalIgnoreCase)
                        || string.Equals(property.Name, "ApiVersion", StringComparison.OrdinalIgnoreCase))
                        hasLegacyFields = true;
                }

                bool migrated = fileVersion < SprocketApi.ApiVersion || hasLegacyFields;
                if (cleaned || migrated)
                    Save(modId, values);
                if (migrated)
                    warn($"[SMA-CONFIG] config for {modId} migrated from version {fileVersion} to {SprocketApi.ApiVersion}.");
                return values;
            }
            catch (Exception exception)
            {
                RecoverInvalidFile(modId, filePath, exception);
                return new Dictionary<string, object>(StringComparer.Ordinal);
            }
        }

        internal bool Save(string modId, IReadOnlyDictionary<string, object> values)
        {
            try
            {
                WriteAtomic(modId, values);
                return true;
            }
            catch (Exception exception)
            {
                warn($"[SMA-CONFIG] config save failed for {modId}: {exception.Message}");
                return false;
            }
        }

        private static bool TryReadValue(JsonElement element, out object? value)
        {
            switch (element.ValueKind)
            {
                case JsonValueKind.True:
                    value = true;
                    return true;
                case JsonValueKind.False:
                    value = false;
                    return true;
                case JsonValueKind.Number:
                    value = element.GetDouble();
                    return true;
                case JsonValueKind.String:
                    value = element.GetString() ?? "";
                    return true;
                default:
                    value = null;
                    return false;
            }
        }

        private Version ValidateRoot(JsonElement root, string filePath)
        {
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The mod config root must be a JSON object.");

            // 配置文件里的 ApiVersion 是这份配置自己的版本：旧版本走迁移，只有更新的版本才拒绝。
            JsonElement apiElement = default;
            bool hasVersion = false;
            foreach (JsonProperty property in root.EnumerateObject())
            {
                if (string.Equals(property.Name, "ConfigVersion", StringComparison.OrdinalIgnoreCase)
                    || string.Equals(property.Name, "ApiVersion", StringComparison.OrdinalIgnoreCase))
                {
                    apiElement = property.Value;
                    hasVersion = true;
                    break;
                }
            }

            if (!hasVersion || apiElement.ValueKind != JsonValueKind.String
                || !Version.TryParse(apiElement.GetString(), out Version? fileVersion))
                throw new InvalidDataException($"Mod config has no readable version in {filePath}; runtime is {SprocketApi.ApiVersion}.");
            if (fileVersion > SprocketApi.ApiVersion)
                throw new InvalidDataException($"Mod config was written by a newer version ({fileVersion}) in {filePath}; runtime is {SprocketApi.ApiVersion}.");
            return fileVersion;
        }

        private void RecoverInvalidFile(string modId, string filePath, Exception exception)
        {
            warn($"[SMA-CONFIG] invalid config detected for {modId}: {exception.Message}");
            try
            {
                Directory.CreateDirectory(directory);
                string backupPath = CreateBackupPath(filePath);
                File.Copy(filePath, backupPath, false);
                warn($"[SMA-CONFIG] invalid config backed up to {backupPath}.");
                WriteAtomic(modId, new Dictionary<string, object>(StringComparer.Ordinal));
            }
            catch (Exception recoveryException)
            {
                warn($"[SMA-CONFIG] config recovery failed for {modId}: {recoveryException.Message}");
            }
        }

        private void WriteAtomic(string modId, IReadOnlyDictionary<string, object> values)
        {
            Directory.CreateDirectory(directory);
            string filePath = FilePathFor(modId);
            string temporaryPath = filePath + ".tmp";
            var root = new ConfigFile
            {
                ModId = modId,
                Values = new Dictionary<string, object>(values, StringComparer.Ordinal)
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(filePath)) File.Replace(temporaryPath, filePath, null);
            else File.Move(temporaryPath, filePath);
        }

        private string CreateBackupPath(string filePath)
        {
            string timestamp = utcNow().ToUniversalTime().ToString("yyyyMMdd-HHmmss-fff");
            string candidate = $"{filePath}.corrupt-{timestamp}.bak";
            int suffix = 1;
            while (File.Exists(candidate))
            {
                candidate = $"{filePath}.corrupt-{timestamp}-{suffix}.bak";
                suffix++;
            }

            return candidate;
        }

        private static JsonElement GetRequiredProperty(JsonElement element, string name)
        {
            foreach (JsonProperty property in element.EnumerateObject())
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
                    return property.Value;
            throw new InvalidDataException($"Missing required mod config property: {name}.");
        }

        private static string Sanitize(string modId)
        {
            char[] buffer = modId.ToCharArray();
            for (int index = 0; index < buffer.Length; index++)
            {
                char value = buffer[index];
                bool safe = char.IsLetterOrDigit(value) || value == '-' || value == '_' || value == '.';
                if (!safe)
                    buffer[index] = '_';
            }

            return new string(buffer);
        }

        private sealed class ConfigFile
        {
            public string ConfigVersion { get; set; } = SprocketApi.ApiVersion.ToString(2);
            public string ModId { get; set; } = "";
            public Dictionary<string, object> Values { get; set; } = new();
        }
    }
}
