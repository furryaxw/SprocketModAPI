using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SprocketModAPI;

internal static class ModConfigTests
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SprocketModAPI.ContractTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckRejections(directory);
            CheckDefaultsAndSnapshot(directory);
            CheckPersistenceAndRetainedKeys(directory);
            CheckCorruptFileRecovery(directory);
            CheckStoredValueIsolation(directory);
            CheckChangeNotification(directory);
            CheckRegistrationLifecycle(directory);
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // 接入错误必须在注册时立刻抛出，而不是留到菜单渲染。
    private static void CheckRejections(string directory)
    {
        var warnings = new List<string>();
        var service = new ModConfigService(directory, warnings.Add);

        ExpectReject(service, new ModConfigDefinition { ModId = "bad id!" }, "invalid mod id rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Sections = new[]
            {
                new ModConfigSectionDefinition { Id = "a", Title = "A" },
                new ModConfigSectionDefinition { Id = "a", Title = "Again" }
            }
        }, "duplicate section id rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Entries = new[]
            {
                ModConfigEntryDefinition.Toggle("same", "One", true),
                ModConfigEntryDefinition.Toggle("same", "Two", false)
            }
        }, "duplicate entry key rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Entries = new[] { ModConfigEntryDefinition.Toggle("flag", "Flag", true, sectionId: "missing") }
        }, "unknown section reference rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Entries = new[] { ModConfigEntryDefinition.Slider("gain", "Gain", 1.0, 2.0, 1.0) }
        }, "slider with inverted range rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Entries = new[] { ModConfigEntryDefinition.Slider("gain", "Gain", 1.0, 0.0, 2.0, 0.0) }
        }, "slider with non-positive step rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Entries = new[] { ModConfigEntryDefinition.Slider("gain", "Gain", 5.0, 0.0, 2.0) }
        }, "slider default outside range rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Entries = new[] { ModConfigEntryDefinition.Choice("mode", "Mode", "x", Array.Empty<string>()) }
        }, "choice without options rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Entries = new[] { ModConfigEntryDefinition.Choice("mode", "Mode", "x", new[] { "x", "x" }) }
        }, "choice with duplicate options rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Entries = new[] { ModConfigEntryDefinition.Choice("mode", "Mode", "y", new[] { "x" }) }
        }, "choice default outside options rejected");
        ExpectReject(service, new ModConfigDefinition
        {
            ModId = "demo",
            Entries = new[] { ModConfigEntryDefinition.Text("label", "Label", "abc", 2) }
        }, "text default exceeding MaxLength rejected");

        IModConfigRegistration first = service.Register(new ModConfigDefinition { ModId = "demo" });
        ExpectReject(service, new ModConfigDefinition { ModId = "demo" }, "duplicate registration rejected");
        Check(warnings.Count > 0, "rejections are logged through the warning sink");
        first.Dispose();
        service.Dispose();
    }

    private static void CheckDefaultsAndSnapshot(string directory)
    {
        var service = new ModConfigService(directory, _ => { });
        using IModConfigRegistration registration = service.Register(DemoDefinition(displayName: "Demo Config"));

        ModConfigSnapshot snapshot = registration.Snapshot;
        Check(snapshot.ModId == "demo.mod" && snapshot.DisplayName == "Demo Config", "snapshot exposes mod identity");
        Check(snapshot.Sections.Count == 1 && snapshot.Sections[0].Id == "general", "snapshot exposes declared sections");
        Check(snapshot.Entries.Count == 4, "snapshot exposes every declared entry");

        Check(registration.GetBool("enabled"), "toggle default is readable");
        Check(Math.Abs(registration.GetNumber("gain") - 1.0) < 0.0001, "slider default is readable");
        Check(registration.GetText("palette") == "white-hot", "choice default is readable");
        Check(registration.GetText("label") == "", "text default is readable");

        Check(snapshot.Entries[0].Value is true, "entry snapshot carries the typed value");
        Check(snapshot.Entries[1].Value is double, "numeric entry snapshot carries a double");

        Check(!File.Exists(Path.Combine(directory, "demo.mod.json")), "registering alone does not create a file");

        ModConfigSnapshot anonymous = service.Register(new ModConfigDefinition { ModId = "second.mod" }).Snapshot;
        Check(anonymous.DisplayName == "second.mod", "display name falls back to the mod id");
        service.Dispose();
    }

    private static void CheckPersistenceAndRetainedKeys(string directory)
    {
        var service = new ModConfigService(directory, _ => { });
        var registration = service.Register(DemoDefinition());
        registration.SetBool("enabled", false);
        registration.SetNumber("gain", 2.5);
        registration.SetText("palette", "black-hot");
        registration.SetText("label", "hello");
        Check(registration.GetBool("enabled") == false && registration.GetText("label") == "hello", "values are readable after writing");
        registration.Dispose();
        service.Dispose();

        string filePath = Path.Combine(directory, "demo.mod.json");
        Check(File.Exists(filePath), "mutating a value writes the config file");
        Check(!File.Exists(filePath + ".tmp"), "atomic write leaves no temporary file");

        // 当前定义里没有声明的键：必须原样保留，不能被本次保存抹掉。
        string raw = File.ReadAllText(filePath);
        using (JsonDocument document = JsonDocument.Parse(raw))
        {
            var merged = new Dictionary<string, object>();
            foreach (JsonProperty property in document.RootElement.GetProperty("Values").EnumerateObject())
            {
                merged[property.Name] = property.Value.ValueKind switch
                {
                    JsonValueKind.True => true,
                    JsonValueKind.False => false,
                    JsonValueKind.Number => property.Value.GetDouble(),
                    _ => property.Value.GetString() ?? ""
                };
            }
            merged["legacy.key"] = "keep-me";
            File.WriteAllText(filePath, JsonSerializer.Serialize(new
            {
                    ConfigVersion = SprocketApi.ApiVersion.ToString(2),
                ModId = "demo.mod",
                Values = merged
            }, new JsonSerializerOptions { WriteIndented = true }));
        }

        var reloaded = new ModConfigService(directory, _ => { });
        var second = reloaded.Register(DemoDefinition());
        Check(second.GetBool("enabled") == false, "boolean value round-trips");
        Check(Math.Abs(second.GetNumber("gain") - 2.5) < 0.0001, "numeric value round-trips");
        Check(second.GetText("palette") == "black-hot", "choice value round-trips");
        second.SetNumber("gain", 3.0);
        second.Dispose();
        reloaded.Dispose();

        using (JsonDocument document = JsonDocument.Parse(File.ReadAllText(filePath)))
        {
            JsonElement values = document.RootElement.GetProperty("Values");
            Check(values.TryGetProperty("legacy.key", out JsonElement retained) && retained.GetString() == "keep-me",
                "unknown stored keys are retained across saves");
            Check(Math.Abs(values.GetProperty("gain").GetDouble() - 3.0) < 0.0001, "later writes persist");
        }
    }

    private static void CheckCorruptFileRecovery(string directory)
    {
        string filePath = Path.Combine(directory, "demo.mod.json");
        File.WriteAllText(filePath, "{ this is not json");

        var warnings = new List<string>();
        var service = new ModConfigService(directory, warnings.Add);
        var registration = service.Register(DemoDefinition());
        Check(registration.GetBool("enabled"), "corrupt file falls back to the declared default");
        Check(warnings.Exists(message => message.Contains("invalid config")), "corrupt file is reported");
        int backups = Directory.GetFiles(directory, "demo.mod.json.corrupt-*.bak").Length;
        Check(backups == 1, "corrupt file is backed up once");
        Check(!File.Exists(filePath + ".tmp"), "recovery leaves no temporary file");

        // 版本比当前新（来自更新构建）走恢复路径
        File.WriteAllText(filePath, "{\"ConfigVersion\":\"3.0\",\"ModId\":\"demo.mod\",\"Values\":{}}");
        registration.Dispose();
        var second = new ModConfigService(directory, warnings.Add);
        using IModConfigRegistration secondRegistration = second.Register(DemoDefinition());
        Check(secondRegistration.GetBool("enabled"), "configuration from a newer version falls back to defaults");
        Check(Directory.GetFiles(directory, "demo.mod.json.corrupt-*.bak").Length == 2, "each corrupt file is backed up separately");
        second.Dispose();
        service.Dispose();
    }

    private static void CheckStoredValueIsolation(string directory)
    {
        string filePath = Path.Combine(directory, "demo.mod.json");
        File.WriteAllText(filePath, JsonSerializer.Serialize(new
        {
            ConfigVersion = SprocketApi.ApiVersion.ToString(2),
            ModId = "demo.mod",
            Values = new Dictionary<string, object>
            {
                ["enabled"] = "yes",     // 类型不符
                ["gain"] = 99.0,         // 超出范围
                ["palette"] = "nope",    // 不在选项内
                ["label"] = 42.0         // 类型不符
            }
        }, new JsonSerializerOptions { WriteIndented = true }));

        var warnings = new List<string>();
        var service = new ModConfigService(directory, warnings.Add);
        var registration = service.Register(DemoDefinition());

        Check(registration.GetBool("enabled"), "wrong-typed toggle falls back to its default");
        Check(Math.Abs(registration.GetNumber("gain") - 4.0) < 0.0001, "out-of-range number is clamped to the maximum");
        Check(registration.GetText("palette") == "white-hot", "choice outside its options falls back to the default");
        Check(registration.GetText("label") == "", "wrong-typed text falls back to its default");
        Check(warnings.Count >= 4, "every degraded item is reported");

        registration.Dispose();
        service.Dispose();
    }

    private static void CheckChangeNotification(string directory)
    {
        var service = new ModConfigService(directory, _ => { });
        var registration = service.Register(DemoDefinition());
        var changes = new List<string>();
        service.Changed += args => changes.Add($"{args.ModId}:{args.Key}");

        registration.SetBool("enabled", true);
        Check(changes.Count == 0, "writing an identical value raises nothing");

        registration.SetBool("enabled", false);
        Check(changes.Count == 1 && changes[0] == "demo.mod:enabled", "real change raises one event with mod and key");

        registration.SetNumber("gain", 2.0);
        registration.SetNumber("gain", 2.0);
        Check(changes.Count == 2 && changes[1] == "demo.mod:gain", "numeric change raises once and repeats are ignored");

        registration.SetText("palette", "black-hot");
        registration.SetText("label", "note");
        Check(changes.Count == 4, "text and choice changes raise events");

        registration.ResetToDefault("label");
        Check(changes.Count == 5 && registration.GetText("label") == "", "reset to default raises an event");
        registration.ResetToDefault("label");
        Check(changes.Count == 5, "reset when already default raises nothing");

        registration.Dispose();
        service.Dispose();
    }

    private static void CheckRegistrationLifecycle(string directory)
    {
        var service = new ModConfigService(directory, _ => { });
        int registrationChanges = 0;
        service.RegistrationsChanged += () => registrationChanges++;

        var first = service.Register(DemoDefinition(displayName: "Zeta"));
        var second = service.Register(new ModConfigDefinition { ModId = "alpha.mod", DisplayName = "alpha" });
        Check(registrationChanges == 2, "registration changes are published");

        IReadOnlyList<ModConfigSnapshot> snapshots = service.Snapshots;
        Check(snapshots.Count == 2 && snapshots[0].DisplayName == "alpha", "snapshots sort by display name ignoring case");
        Check(ReferenceEquals(service.Find("demo.mod"), first), "Find resolves the registration by mod id");
        Check(service.Find("missing.mod") == null, "Find returns null for unknown mods");

        ExpectFailure(() => first.SetNumber("enabled", 2.0), "kind mismatch is rejected");
        ExpectFailure(() => first.SetNumber("gain", 99.0), "out-of-range write is rejected");
        ExpectFailure(() => first.SetText("palette", "nope"), "choice write outside its options is rejected");
        ExpectFailure(() => first.SetText("label", new string('x', 200)), "text longer than MaxLength is rejected");
        ExpectFailure(() => first.GetBool("missing"), "unknown key read is rejected");

        first.Dispose();
        Check(service.Find("demo.mod") == null, "disposed registration is removed");
        Check(registrationChanges == 3, "unregistration publishes a change");
        ExpectFailure(() => first.GetBool("enabled"), "disposed registration rejects further reads");

        second.Dispose();
        service.Dispose();
        Check(service.Snapshots.Count == 0, "disposed service exposes no snapshots");
    }

    private static ModConfigDefinition DemoDefinition(string displayName = "Demo")
        => new()
        {
            ModId = "demo.mod",
            DisplayName = displayName,
            Sections = new[] { new ModConfigSectionDefinition { Id = "general", Title = "General" } },
            Entries = new[]
            {
                ModConfigEntryDefinition.Toggle("enabled", "Enable", true, sectionId: "general"),
                ModConfigEntryDefinition.Slider("gain", "Gain", 1.0, 0.5, 4.0, 0.1, sectionId: "general"),
                ModConfigEntryDefinition.Choice("palette", "Palette", "white-hot", new[] { "white-hot", "black-hot" }),
                ModConfigEntryDefinition.Text("label", "Label", "", 32)
            }
        };

    private static void ExpectReject(ModConfigService service, ModConfigDefinition definition, string name)
    {
        try
        {
            service.Register(definition).Dispose();
            throw new InvalidOperationException($"Contract failed: {name}");
        }
        catch (ArgumentException)
        {
        }
        catch (InvalidOperationException exception) when (exception.Message.Contains("duplicate mod config registration"))
        {
        }
    }

    private static void ExpectFailure(Action action, string name)
    {
        bool rejected = false;
        try
        {
            action();
        }
        catch (ArgumentException)
        {
            rejected = true;
        }
        catch (InvalidOperationException)
        {
            // ObjectDisposedException 也属于这一层；两者都表示接入方用错了。
            rejected = true;
        }

        Check(rejected, name);
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Contract failed: {name}");
    }
}
