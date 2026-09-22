using System;
using System.Collections.Generic;
using SprocketModAPI;

internal static class ModMetadataTests
{
    internal static void Run()
    {
        CheckDeclaredMetadataWins();
        CheckMelonInfoFallback();
        CheckAssemblyOnlyFallback();
        CheckFilenameFallback();
        CheckEmptyDescriptorDegrades();
        CheckServiceSnapshotAndChangeNotification();
    }

    // `Sprocket.Mod.*` 优先于 MelonInfo，且列表字段去重保序。
    private static void CheckDeclaredMetadataWins()
    {
        ModMetadata entry = ModMetadataReader.Read(new LoadedModDescriptor
        {
            MelonName = "Raw Melon Name",
            MelonVersion = "0.9.0",
            MelonAuthor = "rawAuthor",
            AdditionalCredits = "extra hands",
            Kind = ModKind.Mod,
            AssemblyName = "DemoAssembly",
            Location = @"C:\Game\Mods\DemoAssembly.dll",
            AssemblyHash = "ABCDEF",
            Games = new[] { "HD/Sprocket" },
            OptionalDependencies = new[] { "SprocketModAPI" },
            RequiredDependencies = new[] { "SprocketDepth" },
            IncompatibleAssemblies = new[] { "LegacyOverhaul" },
            MelonLoaderVersion = ">= 0.7.3",
            Metadata = new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["Sprocket.Mod.Id"] = "furryaxw.demo",
                ["Sprocket.Mod.DisplayName"] = "  Nice Demo  ",
                ["Sprocket.Mod.Description"] = "does demo things",
                ["Sprocket.Mod.Authors"] = "A, B , a",
                ["Sprocket.Mod.Repository"] = "furryaxw/Demo",
            }
        });

        Check(entry.Id == "furryaxw.demo" && entry.HasDeclaredId && entry.RegistryId == "furryaxw.demo", "declared id wins");
        Check(entry.DisplayName == "Nice Demo", "declared display name wins and is trimmed");
        Check(entry.Version == "0.9.0", "version still comes from MelonInfo");
        Check(entry.Authors.Count == 2 && entry.Authors[0] == "A" && entry.Authors[1] == "B", "authors split, trimmed and de-duplicated case-insensitively");
        Check(entry.Description == "does demo things" && entry.Repository == "furryaxw/Demo", "description and repository read");
        Check(entry.Credits == "extra hands", "additional credits pass through");
        Check(entry.Kind == ModKind.Mod && entry.AssemblyName == "DemoAssembly" && entry.AssemblyHash == "ABCDEF", "assembly identity passes through");
        Check(entry.Games.Count == 1 && entry.Games[0] == "HD/Sprocket", "declared game compatibility passes through");
        Check(entry.OptionalDependencies.Count == 1 && entry.OptionalDependencies[0] == "SprocketModAPI", "optional dependencies pass through");
        Check(entry.RequiredDependencies.Count == 1 && entry.RequiredDependencies[0] == "SprocketDepth", "required dependencies pass through");
        Check(entry.IncompatibleAssemblies.Count == 1 && entry.IncompatibleAssemblies[0] == "LegacyOverhaul", "incompatible assemblies pass through");
        Check(entry.MelonLoaderVersion == ">= 0.7.3", "MelonLoader version requirement passes through");
        Check(entry.RawMetadata.Count == 5, "raw metadata is retained (tags removed)");
        Check(!entry.IsDisabled, "loaded mods are never reported as disabled");
    }

    // 没有 `Sprocket.Mod.*` 时退回 MelonInfo，Id 派生为 `file:<程序集名>`。
    private static void CheckMelonInfoFallback()
    {
        ModMetadata entry = ModMetadataReader.Read(new LoadedModDescriptor
        {
            MelonName = "Legacy Mod",
            MelonVersion = "1.2.0",
            MelonAuthor = "furryAxw",
            Kind = ModKind.Plugin,
            AssemblyName = "LegacyAssembly",
            Location = @"C:\Game\Plugins\LegacyAssembly.dll"
        });

        Check(entry.Id == "file:LegacyAssembly" && !entry.HasDeclaredId, "id falls back to the assembly name");
        Check(entry.DisplayName == "Legacy Mod", "display name falls back to MelonInfo name");
        Check(entry.Version == "1.2.0", "version falls back to MelonInfo version");
        Check(entry.Authors.Count == 1 && entry.Authors[0] == "furryAxw", "authors fall back to MelonInfo author");
        Check(entry.Description.Length == 0 && entry.Category.Length == 0, "absent fields degrade to empty instead of throwing");
        Check(entry.Kind == ModKind.Plugin, "plugin kind is preserved");
    }

    // 纯类库（非 MelonMod/Plugin、无 MelonInfo）仍然能给出程序集身份。
    private static void CheckAssemblyOnlyFallback()
    {
        ModMetadata entry = ModMetadataReader.Read(new LoadedModDescriptor
        {
            Kind = ModKind.Unknown,
            AssemblyName = "SharedLib",
            InformationalVersion = "2.5.0-beta.1",
            AssemblyVersion = "2.5.0.0",
            Location = @"C:\Game\UserLibs\SharedLib.dll"
        });

        Check(entry.Id == "file:SharedLib", "library id derives from the assembly name");
        Check(entry.DisplayName == "SharedLib", "library display name falls back to the assembly name");
        Check(entry.Version == "2.5.0-beta.1", "informational version precedes assembly version");
        Check(entry.Authors.Count == 0, "library without an author reports no authors");
        Check(entry.Kind == ModKind.Unknown, "unknown kind is preserved");
    }

    // 只剩文件路径时用文件名主干，禁用后缀不进入展示名。
    private static void CheckFilenameFallback()
    {
        ModMetadata loaded = ModMetadataReader.Read(new LoadedModDescriptor
        {
            Location = @"C:\Game\Mods\OrphanMod.dll"
        });
        Check(loaded.DisplayName == "OrphanMod" && loaded.Id == "file:OrphanMod", "file name stem drives the fallback identity");

        ModMetadata disabled = ModMetadataReader.Read(new LoadedModDescriptor
        {
            Location = @"C:\Game\Mods\DisabledMod.dll.disable"
        });
        Check(disabled.DisplayName == "DisabledMod" && disabled.Id == "file:DisabledMod", "disable suffix is stripped from the file stem");
    }

    // 完全没有信息时给出稳定占位，不抛异常。
    private static void CheckEmptyDescriptorDegrades()
    {
        ModMetadata entry = ModMetadataReader.Read(new LoadedModDescriptor());
        Check(entry.DisplayName == "UNKNOWN", "missing name degrades to UNKNOWN");
        Check(entry.Id == "file:UNKNOWN", "missing identity degrades to a stable placeholder");
        Check(entry.Version.Length == 0 && entry.Authors.Count == 0, "missing fields stay empty");
    }

    // 快照服务：排序稳定、Find 精确匹配、内容不变不通知、内容变化才通知。
    private static void CheckServiceSnapshotAndChangeNotification()
    {
        var source = new FakeSource();
        source.Items.Add(new LoadedModDescriptor
        {
            MelonName = "Zeta Fix",
            AssemblyName = "ZetaFix",
            Location = @"C:\Game\Mods\ZetaFix.dll"
        });
        source.Items.Add(new LoadedModDescriptor
        {
            MelonName = "alpha tools",
            AssemblyName = "AlphaTools",
            Location = @"C:\Game\Mods\AlphaTools.dll"
        });

        var service = new ModMetadataService(source);
        int changedCount = 0;
        service.Changed += () => changedCount++;

        IReadOnlyList<ModMetadata> entries = service.Entries;
        Check(changedCount == 1, "first snapshot publishes one Changed notification");
        int baseline = changedCount;

        Check(entries.Count == 2, "snapshot contains every source entry");
        Check(entries[0].DisplayName == "alpha tools" && entries[1].DisplayName == "Zeta Fix", "snapshot sorts by display name ignoring case");

        ModMetadata? found = service.Find("file:AlphaTools");
        Check(found != null && found.DisplayName == "alpha tools", "Find resolves by exact id");
        Check(service.Find("file:Missing") == null, "Find returns null for unknown ids");
        Check(service.Find("") == null, "Find returns null for empty ids");

        service.Refresh();
        Check(changedCount == baseline, "unchanged content does not raise Changed again");

        source.Items.Add(new LoadedModDescriptor
        {
            MelonName = "Beta Patch",
            AssemblyName = "BetaPatch",
            Location = @"C:\Game\Mods\BetaPatch.dll"
        });
        service.Refresh();
        Check(changedCount == baseline + 1, "content change raises Changed once");
        Check(service.Entries.Count == 3, "snapshot picks up newly registered mods");

        service.Dispose();
        Check(service.Entries.Count == 0, "disposed service exposes no snapshot");
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Contract failed: {name}");
    }

    private sealed class FakeSource : ILoadedModSource
    {
        internal List<LoadedModDescriptor> Items { get; } = new();

        public IReadOnlyList<LoadedModDescriptor> Read() => Items;
    }
}
