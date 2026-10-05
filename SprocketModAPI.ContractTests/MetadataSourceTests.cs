using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using BepInEx;
using SprocketModAPI;

// 依赖清单来自程序集特性（`BepInDependency` / `BepInIncompatibility`），已加载插件表不暴露它们。
// 这里用运行时动态程序集验证真实的特性读取与去重逻辑，而不是只验证字段透传。
internal static class MetadataSourceTests
{
    internal static void Run()
    {
        CheckRequiredDependenciesAreRead();
        CheckIncompatiblePluginsAreRead();
        CheckMissingAssemblyIsSafe();
    }

    private static void CheckRequiredDependenciesAreRead()
    {
        var name = new AssemblyName($"SprocketModAPI.ContractTests.Metadata{Guid.NewGuid():N}");
        AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        builder.SetCustomAttribute(CreateDependency("furryaxw.sprocket-depth"));
        builder.SetCustomAttribute(CreateDependency(" furryaxw.sprocket-depth "));
        Assembly assembly = builder;

        IReadOnlyList<string> guids = BepInExPluginSource.CollectGuids<BepInDependency>(
            assembly, attribute => attribute.DependencyGUID);

        Check(guids.Count == 1, "duplicate dependency GUIDs are collapsed");
        Check(guids[0] == "furryaxw.sprocket-depth", "dependency GUIDs are trimmed");
    }

    private static void CheckIncompatiblePluginsAreRead()
    {
        Assembly assembly = BuildAssembly(
            typeof(BepInIncompatibility),
            new[] { typeof(string) },
            new object[] { "legacy.overhaul" });

        IReadOnlyList<string> guids = BepInExPluginSource.CollectGuids<BepInIncompatibility>(
            assembly, attribute => attribute.IncompatibilityGUID);

        Check(guids.Count == 1 && guids[0] == "legacy.overhaul", "incompatible plugin GUIDs are read from the attribute");
    }

    private static void CheckMissingAssemblyIsSafe()
    {
        Check(BepInExPluginSource.CollectGuids<BepInDependency>(
            null, attribute => attribute.DependencyGUID).Count == 0, "a missing assembly yields an empty list");
    }

    private static CustomAttributeBuilder CreateDependency(string guid)
        => new(
            typeof(BepInDependency).GetConstructor(new[] { typeof(string), typeof(BepInDependency.DependencyFlags) })!,
            new object[] { guid, BepInDependency.DependencyFlags.HardDependency });

    private static Assembly BuildAssembly(Type attributeType, Type[] constructorTypes, object[] arguments)
    {
        var name = new AssemblyName($"SprocketModAPI.ContractTests.Metadata{Guid.NewGuid():N}");
        AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        builder.SetCustomAttribute(new CustomAttributeBuilder(
            attributeType.GetConstructor(constructorTypes)!,
            arguments));
        return builder;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Contract failed: {name}");
    }
}
