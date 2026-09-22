using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using SprocketModAPI;

internal static class ModMenuTests
{
    internal static void Run()
    {
        string directory = Path.Combine(Path.GetTempPath(), "SprocketModAPI.ContractTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            CheckFileToggle(directory);
            CheckListModel(directory);
            CheckFiltering();
            CheckRestartTracking();
        }
        finally
        {
            Directory.Delete(directory, true);
        }
    }

    // 禁用/启用只做改名，任何冲突都必须拒绝且不改变磁盘状态。
    private static void CheckFileToggle(string directory)
    {
        string modPath = Path.Combine(directory, "ExampleMod.dll");
        File.WriteAllText(modPath, "not a real assembly");

        Check(ModFileToggle.IsLoadablePath(modPath) && !ModFileToggle.IsDisabledPath(modPath), "dll is loadable");
        Check(ModFileToggle.IsDisabledPath(modPath + ".disable"), "disable suffix is recognised");

        ModToggleResult disabled = ModFileToggle.Disable(modPath);
        Check(disabled.Succeeded && disabled.RestartRequired, "disable succeeds and requires a restart");
        Check(!File.Exists(modPath) && File.Exists(modPath + ".disable"), "disable renames the file");
        Check(disabled.Path == modPath + ".disable", "disable reports the new path");

        ModToggleResult enabling = ModFileToggle.Enable(modPath + ".disable");
        Check(enabling.Succeeded && File.Exists(modPath) && !File.Exists(modPath + ".disable"), "enable renames the file back");

        ModToggleResult wrongKind = ModFileToggle.Disable(modPath + ".disable");
        Check(!wrongKind.Succeeded && wrongKind.Message.Length != 0, "disabling an already disabled file is rejected");

        ModToggleResult missing = ModFileToggle.Enable(Path.Combine(directory, "Missing.dll.disable"));
        Check(!missing.Succeeded, "enabling a missing file is rejected");

        File.WriteAllText(modPath, "x");
        File.WriteAllText(modPath + ".disable", "y");
        ModToggleResult conflict = ModFileToggle.Disable(modPath);
        Check(!conflict.Succeeded && File.Exists(modPath) && File.Exists(modPath + ".disable"), "existing disable target is not overwritten");
        File.Delete(modPath + ".disable");

        File.WriteAllText(Path.Combine(directory, "Second.dll.disable"), "z");
        IReadOnlyList<string> disabledFiles = ModFileToggle.FindDisabledDlls(directory);
        Check(disabledFiles.Count == 1 && disabledFiles[0].EndsWith("Second.dll.disable", StringComparison.Ordinal),
            "disabled files are discovered");
        Check(ModFileToggle.FindDisabledDlls(Path.Combine(directory, "missing")).Count == 0, "missing directory yields no disabled files");
    }

    private static void CheckListModel(string directory)
    {
        var loaded = new List<ModMetadata>
        {
            new()
            {
                Id = "furryaxw.sprocket-thermal",
                DisplayName = "Sprocket Thermal Vision",
                Version = "2.8.0",
                Authors = new[] { "furryAxw" },
                Description = "热成像视觉",
                Kind = ModKind.Mod,
                Location = @"C:\Game\Mods\SprocketThermal.dll",
                AssemblyName = "SprocketThermal",
                AssemblyHash = "ABCDEF",
                Games = new[] { "HD/Sprocket" },
                OptionalDependencies = new[] { "SprocketModAPI" },
                RequiredDependencies = new[] { "SprocketDepth" },
                IncompatibleAssemblies = new[] { "LegacyOverhaul" }
            },
            new()
            {
                Id = "file:SharedLib",
                DisplayName = "SharedLib",
                AssemblyName = "SharedLib",
                Kind = ModKind.Unknown,
                Location = @"C:\Game\UserLibs\SharedLib.dll"
            }
        };

        var disabledPaths = new List<string>
        {
            @"C:\Game\Mods\OldMod.dll.disable",
            @"C:\Game\Mods\Unknown.dll.disable"
        };

        IReadOnlyList<ModMenuRow> rows = ModMenuListModel.Build(loaded, disabledPaths, new[] { "sprocket-thermal" });
        Check(rows.Count == 4, "loaded and disabled entries are merged");
        Check(rows[0].IsDisabled == false && rows[1].IsDisabled == false && rows[2].IsDisabled, "enabled mods sort before disabled ones");
        Check(rows[0].DisplayName == "SharedLib" && rows[1].DisplayName == "Sprocket Thermal Vision", "enabled mods sort by display name");

        ModMenuRow thermal = rows[1];
        Check(thermal.ConfigModId == "sprocket-thermal" && thermal.HasConfigPage, "config page is resolved through the mod id suffix");
        Check(thermal.KindLabel == "Mod" && thermal.Authors == "furryAxw", "row exposes kind and authors");
        Check(thermal.Games.Count == 1 && thermal.Games[0] == "HD/Sprocket"
            && thermal.OptionalDependencies.Count == 1
            && thermal.AssemblyName == "SprocketThermal" && thermal.AssemblyHash == "ABCDEF",
            "dependency and assembly details pass through to the menu row");
        Check(thermal.RequiredDependencies.Count == 1 && thermal.RequiredDependencies[0] == "SprocketDepth"
            && thermal.IncompatibleAssemblies.Count == 1 && thermal.IncompatibleAssemblies[0] == "LegacyOverhaul",
            "required and incompatible dependencies reach the menu row");

        ModMenuRow shared = rows[0];
        Check(!shared.HasConfigPage, "mods without a config page are flagged");

        ModMenuRow oldMod = rows[2];
        Check(oldMod.DisplayName == "OldMod", "a disabled entry is named after its file");

        ModMenuRow unknown = rows[3];
        Check(unknown.DisplayName == "Unknown" && unknown.KindLabel == "Disabled", "disabled entries without a record fall back to the file stem");

        IReadOnlyList<ModMenuRow> deduped = ModMenuListModel.Build(loaded,
            new List<string> { @"C:\Game\Mods\SprocketThermal.dll" },
            Array.Empty<string>());
        Check(deduped.Count == 2, "a path that is already loaded is not listed twice");

        Check(ModMenuListModel.Build(Array.Empty<ModMetadata>(),
            Array.Empty<string>(), Array.Empty<string>()).Count == 0, "empty inputs produce no rows");

        Check(ModMenuListModel.ResolveConfigModId(
            new ModMetadata { Id = "furryaxw.sprocket-thermal" }, new[] { "sprocket-thermal" }) == "sprocket-thermal",
            "config resolution falls back to the id suffix");
        Check(ModMenuListModel.ResolveConfigModId(
            new ModMetadata { Id = "plain", AssemblyName = "Plain" }, new[] { "Plain" }) == "Plain",
            "config resolution falls back to the assembly name");
        Check(ModMenuListModel.ResolveConfigModId(
            new ModMetadata { Id = "plain" }, new[] { "other" }).Length == 0,
            "config resolution returns empty when nothing matches");

        CheckMissingDependencyDetection();
        CheckAssemblyIndexConsolidatesStems(directory);
    }

    private static void CheckFiltering()
    {
        var loaded = new List<ModMetadata>
        {
            new() { Id = "a", DisplayName = "Thermal Vision", Authors = new[] { "furryAxw" } },
            new() { Id = "b", DisplayName = "Jitter Fix", Description = "fixes laying drive" }
        };
        IReadOnlyList<ModMenuRow> rows = ModMenuListModel.Build(loaded,
            Array.Empty<string>(), Array.Empty<string>());

        Check(ModMenuListModel.Filter(rows, "").Count == 2, "empty query keeps everything");
        Check(ModMenuListModel.Filter(rows, "thermal").Count == 1, "search matches the display name");
        Check(ModMenuListModel.Filter(rows, "FURRYAXW").Count == 0, "search no longer matches the author");
        Check(ModMenuListModel.Filter(rows, "a thermal").Count == 1, "search requires all tokens across the id and name");
        Check(ModMenuListModel.Filter(rows, "fixes drive").Count == 0, "search does not match the description");
        Check(ModMenuListModel.Filter(rows, "fixes missing").Count == 0, "all tokens must match");
        Check(ModMenuListModel.Filter(Array.Empty<ModMenuRow>(), "x").Count == 0, "filtering an empty list is safe");
    }

    private static void CheckMissingDependencyDetection()
    {
        var loaded = new List<ModMetadata>
        {
            new()
            {
                Id = "furryaxw.sprocket-laser-rangefinder",
                DisplayName = "Sprocket Laser Rangefinder",
                AssemblyName = "SprocketLaserRangefinder",
                RequiredDependencies = new[] { "SprocketModAPI", "SprocketDepth", "SprocketLaserRangefinder" },
                IncompatibleAssemblies = new[] { "LegacyOverhaul" }
            }
        };

        // 只把 API 当作已存在：SprocketDepth（UserLibs 里的库）应当被判为缺失。
        IReadOnlyList<ModMenuRow> rows = ModMenuListModel.Build(loaded,
            Array.Empty<string>(), Array.Empty<string>(),
            new[] { "SprocketModAPI" });

        Check(rows.Count == 1, "dependency test row is built");
        Check(rows[0].MissingDependencies.Count == 1 && rows[0].MissingDependencies[0] == "SprocketDepth",
            "only the genuinely absent dependency is reported (self reference ignored)");
        Check(!rows[0].HasIncompatiblePresent, "an absent incompatible assembly is not a conflict");

        IReadOnlyList<ModMenuRow> complete = ModMenuListModel.Build(loaded,
            Array.Empty<string>(), Array.Empty<string>(),
            new[] { "SprocketModAPI", "SprocketDepth", "LegacyOverhaul" });
        Check(complete[0].MissingDependencies.Count == 0, "nothing is reported missing when every dependency is present");
        Check(complete[0].HasIncompatiblePresent, "a present incompatible assembly is reported as a conflict");

        // 可选依赖缺失不算问题。
        IReadOnlyList<ModMenuRow> optional = ModMenuListModel.Build(new List<ModMetadata>
        {
            new() { Id = "a", DisplayName = "A", AssemblyName = "A", OptionalDependencies = new[] { "NotInstalled" } }
        }, Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>());
        Check(optional[0].MissingDependencies.Count == 0, "missing optional dependencies are not reported");

        // 禁用条目的文件名主干也算「存在」。
        IReadOnlyList<ModMenuRow> disabledKnown = ModMenuListModel.Build(new List<ModMetadata>
        {
            new() { Id = "a", DisplayName = "A", AssemblyName = "A", RequiredDependencies = new[] { "SprocketDepth" } }
        }, new[] { @"C:\Game\UserLibs\SprocketDepth.dll.disable" },
            Array.Empty<string>(), Array.Empty<string>());
        Check(disabledKnown[0].MissingDependencies.Count == 0, "a disabled file still counts as an available assembly");
    }

    // 目录索引把 `.dll` 与 `.dll.disable` 归一成同一个程序集名，并忽略无关文件。
    private static void CheckAssemblyIndexConsolidatesStems(string directory)
    {
        string root = Path.Combine(directory, "assembly-index");
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "SprocketDepth.dll"), "x");
        File.WriteAllText(Path.Combine(root, "Disabled.dll.disable"), "x");
        File.WriteAllText(Path.Combine(root, "readme.txt"), "x");

        IReadOnlyList<string> names = ModAssemblyIndex.Collect(root, Path.Combine(root, "missing"));
        Check(names.Count == 2, "only dll-like files contribute assembly names");
        Check(names.Contains("SprocketDepth") && names.Contains("Disabled"), "disable suffix is stripped");
        Check(ModAssemblyIndex.Stem(@"C:\Game\Mods\Example.dll.disable") == "Example", "stem strips both suffixes");
    }

    // 重启提示只看「当前磁盘状态是否偏离启动状态」：同一次运行里禁了又启回到原样就不该提示。
    private static void CheckRestartTracking()
    {
        const string mod = @"C:\Game\Mods\ExampleMod.dll";
        const string other = @"C:\Game\Mods\Other.dll";

        var reverted = new ModMenuRestartTracker();
        reverted.Record(mod, disabledBeforeClick: false);
        reverted.Update(mod, disabledAfterClick: true);
        Check(reverted.RestartPending, "disabling a loaded mod requires a restart");
        reverted.Record(mod, disabledBeforeClick: true);
        reverted.Update(mod, disabledAfterClick: false);
        Check(!reverted.RestartPending && reverted.PendingCount == 0,
            "disable followed by enable within one run needs no restart");

        var enabled = new ModMenuRestartTracker();
        enabled.Record(mod, disabledBeforeClick: true);
        enabled.Update(mod, disabledAfterClick: false);
        Check(enabled.RestartPending, "enabling a mod that was disabled at startup requires a restart");

        var mixed = new ModMenuRestartTracker();
        mixed.Record(mod, disabledBeforeClick: false);
        mixed.Update(mod, disabledAfterClick: true);
        mixed.Record(other, disabledBeforeClick: false);
        mixed.Update(other, disabledAfterClick: true);
        mixed.Record(other, disabledBeforeClick: true);
        mixed.Update(other, disabledAfterClick: false);
        Check(mixed.RestartPending && mixed.PendingCount == 1,
            "reverting one mod keeps the other mod's pending restart");

        var unknown = new ModMenuRestartTracker();
        unknown.Update(mod, disabledAfterClick: true);
        Check(unknown.RestartPending, "an unrecorded state change still counts as pending");
        unknown.Update("", disabledAfterClick: true);
        Check(unknown.PendingCount == 1, "an empty identity is ignored");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Contract failed: {name}");
    }
}
