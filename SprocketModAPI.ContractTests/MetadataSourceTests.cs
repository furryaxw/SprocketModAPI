using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using MelonLoader;
using SprocketModAPI;

// 依赖清单来自程序集特性（`MelonAdditionalDependencies` / `MelonIncompatibleAssemblies`），
// `MelonBase` 不暴露它们。这里用运行时动态程序集验证真实的特性读取与去重逻辑，
// 而不是只验证字段透传。
internal static class MetadataSourceTests
{
    internal static void Run()
    {
        CheckRequiredDependenciesAreRead();
        CheckIncompatibleAssembliesAreRead();
        CheckMissingAssemblyIsSafe();
    }

    private static void CheckRequiredDependenciesAreRead()
    {
        Assembly assembly = BuildAssembly(
            new MelonAdditionalDependenciesAttribute("SprocketDepth", " SprocketModAPI ", "SprocketDepth"));

        IReadOnlyList<string> names = MelonLoaderModSource.CollectAssemblyNames<MelonAdditionalDependenciesAttribute>(
            assembly, attribute => attribute.AssemblyNames);

        Check(names.Count == 2, "duplicate dependency names are collapsed");
        Check(names[0] == "SprocketDepth" && names[1] == "SprocketModAPI", "dependency names are trimmed and ordered");
    }

    private static void CheckIncompatibleAssembliesAreRead()
    {
        Assembly assembly = BuildAssembly(new MelonIncompatibleAssembliesAttribute("LegacyOverhaul"));

        IReadOnlyList<string> names = MelonLoaderModSource.CollectAssemblyNames<MelonIncompatibleAssembliesAttribute>(
            assembly, attribute => attribute.AssemblyNames);

        Check(names.Count == 1 && names[0] == "LegacyOverhaul", "incompatible assemblies are read from the attribute");
    }

    private static void CheckMissingAssemblyIsSafe()
    {
        Check(MelonLoaderModSource.CollectAssemblyNames<MelonAdditionalDependenciesAttribute>(
            null, attribute => attribute.AssemblyNames).Count == 0, "a missing assembly yields an empty list");
    }

    private static Assembly BuildAssembly(Attribute attribute)
    {
        var name = new AssemblyName($"SprocketModAPI.ContractTests.Metadata{Guid.NewGuid():N}");
        AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
        Type attributeType = attribute.GetType();
        string[] names = (string[])attributeType.GetProperty("AssemblyNames")!.GetValue(attribute)!;
        builder.SetCustomAttribute(new CustomAttributeBuilder(
            attributeType.GetConstructor(new[] { typeof(string[]) })!,
            new object[] { names }));
        return builder;
    }

    private static void Check(bool condition, string name)
    {
        if (!condition)
            throw new InvalidOperationException($"Contract failed: {name}");
    }
}
