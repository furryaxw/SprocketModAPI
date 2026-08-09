using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace SprocketModAPI
{
    internal sealed class KeybindingStore
    {
        private const int CurrentSchemaVersion = 1;
        private readonly string filePath;
        private readonly Action<string> warn;
        private readonly Func<DateTime> utcNow;

        internal KeybindingStore(string filePath, Action<string> warn, Func<DateTime>? utcNow = null)
        {
            this.filePath = filePath ?? throw new ArgumentNullException(nameof(filePath));
            this.warn = warn ?? throw new ArgumentNullException(nameof(warn));
            this.utcNow = utcNow ?? (() => DateTime.UtcNow);
        }

        internal Dictionary<string, Dictionary<string, string?>> Load()
        {
            if (!File.Exists(filePath))
                return new Dictionary<string, Dictionary<string, string?>>();

            try
            {
                using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
                JsonElement root = document.RootElement;
                ValidateRoot(root);

                JsonElement actionsElement = GetRequiredProperty(root, "Actions");
                if (actionsElement.ValueKind != JsonValueKind.Object)
                    throw new InvalidDataException("Actions must be a JSON object.");

                bool cleaned = false;
                var actions = new Dictionary<string, Dictionary<string, string?>>(StringComparer.Ordinal);
                foreach (JsonProperty actionProperty in actionsElement.EnumerateObject())
                {
                    if (!IsStableActionId(actionProperty.Name) || actionProperty.Value.ValueKind != JsonValueKind.Object)
                    {
                        cleaned = true;
                        warn($"[SMA] ignored invalid keybinding action entry: {actionProperty.Name}.");
                        continue;
                    }

                    var slots = new Dictionary<string, string?>(StringComparer.Ordinal);
                    foreach (JsonProperty slotProperty in actionProperty.Value.EnumerateObject())
                    {
                        string slot = slotProperty.Name.ToLowerInvariant();
                        if (slot != "primary" && slot != "secondary")
                        {
                            cleaned = true;
                            warn($"[SMA] ignored invalid keybinding slot {actionProperty.Name}:{slotProperty.Name}.");
                            continue;
                        }

                        if (slotProperty.Value.ValueKind == JsonValueKind.Null)
                        {
                            slots[slot] = null;
                            continue;
                        }

                        if (slotProperty.Value.ValueKind != JsonValueKind.String
                            || !KeyChordCodec.TryParse(slotProperty.Value.GetString(), out KeyChord chord))
                        {
                            cleaned = true;
                            warn($"[SMA] ignored invalid keybinding value {actionProperty.Name}:{slotProperty.Name}.");
                            continue;
                        }

                        slots[slot] = KeyChordCodec.Format(chord);
                    }

                    actions[actionProperty.Name] = slots;
                }

                if (cleaned)
                    Save(actions);
                return actions;
            }
            catch (Exception exception)
            {
                return RecoverInvalidFile(exception);
            }
        }

        internal bool Save(IReadOnlyDictionary<string, Dictionary<string, string?>> actions)
        {
            try
            {
                WriteAtomic(actions);
                return true;
            }
            catch (Exception exception)
            {
                warn($"[SMA] keybinding save failed: {exception.Message}");
                return false;
            }
        }

        private void ValidateRoot(JsonElement root)
        {
            if (root.ValueKind != JsonValueKind.Object)
                throw new InvalidDataException("The keybinding root must be a JSON object.");

            JsonElement schemaElement = GetRequiredProperty(root, "SchemaVersion");
            if (schemaElement.ValueKind != JsonValueKind.Number
                || !schemaElement.TryGetInt32(out int schemaVersion)
                || schemaVersion != CurrentSchemaVersion)
                throw new InvalidDataException($"Unsupported keybinding schema version; expected {CurrentSchemaVersion}.");

            JsonElement apiElement = GetRequiredProperty(root, "ApiVersion");
            if (apiElement.ValueKind != JsonValueKind.String
                || !Version.TryParse(apiElement.GetString(), out Version? apiVersion)
                || !SprocketApi.IsCompatible(apiVersion))
                throw new InvalidDataException($"Unsupported keybinding API version; runtime is {SprocketApi.ApiVersion}.");
        }

        private Dictionary<string, Dictionary<string, string?>> RecoverInvalidFile(Exception exception)
        {
            var empty = new Dictionary<string, Dictionary<string, string?>>();
            warn($"[SMA] invalid keybinding configuration detected: {exception.Message}");

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
                string backupPath = CreateBackupPath();
                File.Copy(filePath, backupPath, false);
                warn($"[SMA] invalid keybinding configuration backed up to {backupPath}.");
                WriteAtomic(empty);
            }
            catch (Exception recoveryException)
            {
                warn($"[SMA] keybinding configuration recovery failed: {recoveryException.Message}");
            }

            return empty;
        }

        private void WriteAtomic(IReadOnlyDictionary<string, Dictionary<string, string?>> actions)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(filePath)!);
            string temporaryPath = filePath + ".tmp";
            var root = new ConfigFile
            {
                Actions = new Dictionary<string, Dictionary<string, string?>>(actions, StringComparer.Ordinal)
            };
            File.WriteAllText(temporaryPath, JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));
            if (File.Exists(filePath)) File.Replace(temporaryPath, filePath, null);
            else File.Move(temporaryPath, filePath);
        }

        private string CreateBackupPath()
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
            throw new InvalidDataException($"Missing required keybinding property: {name}.");
        }

        private static bool IsStableActionId(string value)
        {
            if (string.IsNullOrWhiteSpace(value)) return false;
            int separator = value.IndexOf(':');
            return separator > 0 && separator < value.Length - 1
                && value.IndexOf(':', separator + 1) < 0
                && !string.IsNullOrWhiteSpace(value.Substring(0, separator))
                && !string.IsNullOrWhiteSpace(value.Substring(separator + 1));
        }

        private sealed class ConfigFile
        {
            public int SchemaVersion { get; set; } = CurrentSchemaVersion;
            public string ApiVersion { get; set; } = SprocketApi.ApiVersion.ToString(2);
            public Dictionary<string, Dictionary<string, string?>> Actions { get; set; } = new();
        }
    }

    internal static class KeyChordCodec
    {
        private const ModifierKeys AllModifiers = ModifierKeys.AnyShift | ModifierKeys.AnyCtrl | ModifierKeys.AnyAlt;

        internal static string? Format(KeyChord chord)
            => chord.IsEmpty ? null : $"{chord.RawControlPath}|{(int)chord.Modifiers}";

        internal static bool TryParse(string? value, out KeyChord chord)
        {
            chord = default;
            if (string.IsNullOrWhiteSpace(value))
                return false;

            try
            {
                int marker = value.LastIndexOf('|');
                if (marker < 0)
                {
                    chord = new KeyChord(value);
                    return true;
                }

                string path = value.Substring(0, marker);
                if (!int.TryParse(value.Substring(marker + 1), out int modifierValue))
                    return false;
                ModifierKeys modifiers = (ModifierKeys)modifierValue;
                if (modifierValue < 0 || (modifiers & ~AllModifiers) != 0)
                    return false;
                chord = new KeyChord(path, modifiers);
                return true;
            }
            catch (ArgumentException)
            {
                return false;
            }
        }
    }
}
