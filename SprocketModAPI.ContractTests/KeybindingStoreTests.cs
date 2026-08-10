using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SprocketModAPI;

internal static class KeybindingStoreTests
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SprocketModAPI.ContractTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            string filePath = Path.Combine(directory, "keybindings.json");
            var warnings = new List<string>();
            var fixedTime = new DateTime(2026, 8, 9, 12, 34, 56, 789, DateTimeKind.Utc);
            var store = new KeybindingStore(filePath, warnings.Add, () => fixedTime);

            CheckValidRoundTrip(store, filePath);
            CheckPartialRecovery(store, filePath, warnings);
            CheckWholeFileRecovery(store, filePath, directory, warnings);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    private static void CheckValidRoundTrip(KeybindingStore store, string filePath)
    {
        var expected = new Dictionary<string, Dictionary<string, string?>>
        {
            ["example:active"] = new()
            {
                ["primary"] = "<keyboard>/k|4",
                ["secondary"] = null
            },
            ["uninstalled:retained"] = new()
            {
                ["primary"] = "<mouse>/middlebutton|0"
            }
        };

        Check(store.Save(expected), "valid keybinding configuration saves");
        Dictionary<string, Dictionary<string, string?>> loaded = store.Load();
        Check(loaded["example:active"]["primary"] == "<keyboard>/k|4", "valid binding round-trips");
        Check(loaded["example:active"].ContainsKey("secondary") && loaded["example:active"]["secondary"] == null,
            "explicit unbind round-trips");
        Check(loaded.ContainsKey("uninstalled:retained"), "unregistered action configuration is retained");
        Check(!File.Exists(filePath + ".tmp"), "atomic save leaves no temporary file");
    }

    private static void CheckPartialRecovery(KeybindingStore store, string filePath, List<string> warnings)
    {
        warnings.Clear();
        File.WriteAllText(filePath,
            "{\"SchemaVersion\":1,\"ApiVersion\":\"1.0\",\"Actions\":{" +
            "\"valid:action\":{\"primary\":\"<Keyboard>/z|4\",\"secondary\":null,\"third\":\"<Keyboard>/x|0\"}," +
            "\"partial:action\":{\"primary\":42,\"secondary\":\"<Mouse>/rightButton|0\"}," +
            "\"invalid-id\":{\"primary\":\"<Keyboard>/q|0\"}}}");

        Dictionary<string, Dictionary<string, string?>> loaded = store.Load();
        Check(loaded.Count == 2, "invalid action entry is isolated");
        Check(loaded["valid:action"]["primary"] == "<keyboard>/z|4", "valid slot survives neighboring invalid slot");
        Check(loaded["valid:action"]["secondary"] == null, "explicit unbind survives partial recovery");
        Check(!loaded["valid:action"].ContainsKey("third"), "unknown slot is removed");
        Check(!loaded["partial:action"].ContainsKey("primary")
            && loaded["partial:action"]["secondary"] == "<mouse>/rightbutton|0", "invalid slot does not block valid sibling slot");
        Check(warnings.Any(message => message.Contains("invalid keybinding", StringComparison.OrdinalIgnoreCase)),
            "partial recovery logs rejected entries");

        using JsonDocument cleaned = JsonDocument.Parse(File.ReadAllText(filePath));
        Check(cleaned.RootElement.GetProperty("Actions").GetProperty("partial:action").TryGetProperty("secondary", out _),
            "partially recovered configuration is rewritten as valid JSON");
    }

    private static void CheckWholeFileRecovery(KeybindingStore store, string filePath, string directory, List<string> warnings)
    {
        int backupCount = Directory.GetFiles(directory, "keybindings.json.corrupt-*.bak").Length;
        File.WriteAllText(filePath, "{ definitely not json");
        warnings.Clear();
        Check(store.Load().Count == 0, "corrupt JSON recovers to empty configuration");
        string[] backups = Directory.GetFiles(directory, "keybindings.json.corrupt-*.bak");
        Check(backups.Length == backupCount + 1 && File.ReadAllText(backups[^1]) == "{ definitely not json",
            "corrupt JSON is preserved in diagnostic backup");
        CheckValidRoot(filePath, "corrupt JSON is replaced with writable valid configuration");

        File.WriteAllText(filePath, "{\"SchemaVersion\":2,\"ApiVersion\":\"1.0\",\"Actions\":{}}");
        Check(store.Load().Count == 0, "unsupported schema recovers safely");
        Check(Directory.GetFiles(directory, "keybindings.json.corrupt-*.bak").Length == backupCount + 2,
            "unsupported schema is backed up");

        File.WriteAllText(filePath, "{\"SchemaVersion\":1,\"ApiVersion\":\"2.0\",\"Actions\":{}}");
        Check(store.Load().Count == 0, "incompatible API version recovers safely");
        Check(Directory.GetFiles(directory, "keybindings.json.corrupt-*.bak").Length == backupCount + 3,
            "incompatible API configuration is backed up");
        Check(warnings.Any(message => message.Contains("configuration", StringComparison.OrdinalIgnoreCase)),
            "whole-file recovery is logged");
    }

    private static void CheckValidRoot(string filePath, string name)
    {
        using JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath));
        Check(document.RootElement.GetProperty("SchemaVersion").GetInt32() == 1
            && document.RootElement.GetProperty("ApiVersion").GetString() == "1.1"
            && document.RootElement.GetProperty("Actions").ValueKind == JsonValueKind.Object, name);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
    }
}
