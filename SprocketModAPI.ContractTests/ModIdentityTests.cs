using System;
using System.Reflection;
using System.Reflection.Emit;

namespace SprocketModAPI
{
    // 模组身份推断：注册动作/配置时从**调用方程序集**的 `Sprocket.Mod.Id` 解析 `ModId`。
    // 这里用运行时动态程序集验证真实解析逻辑（含没有声明时的 `file:<程序集名>` 退化）。
    internal static class ModIdentityTests
    {
        internal static void Run()
        {
            CheckDeclaredIdWins();
            CheckMissingDeclarationFallsBackToAssemblyName();
            CheckApiAssemblyResolvesToItsRegistryId();
            CheckMissingAssemblyIsSafe();
        }

        private static void CheckDeclaredIdWins()
        {
            Assembly assembly = BuildAssembly("Sprocket.Mod.Id", "  furryaxw.demo-mod  ");
            Check(ModIdentity.ResolveModId(assembly) == "furryaxw.demo-mod",
                "a declared Sprocket.Mod.Id is trimmed and used as the keybinding/config namespace");
        }

        private static void CheckMissingDeclarationFallsBackToAssemblyName()
        {
            Assembly assembly = BuildAssembly(null, null);
            string resolved = ModIdentity.ResolveModId(assembly);
            Check(resolved.Length > 0 && !resolved.Contains(':') && assembly.GetName().Name == resolved,
                "without a declaration the namespace falls back to the bare assembly name (no ':' allowed)");
        }

        private static void CheckApiAssemblyResolvesToItsRegistryId()
        {
            // API 自己的动作（mod menu 的 mod-menu）也走同一条推断：必须解析成它声明并已进 Registry 的 id。
            string resolved = ModIdentity.ResolveModId(typeof(ModIdentity).Assembly);
            Check(resolved == "furryaxw.sprocket-mod-api",
                $"the API assembly resolves to its registry id (got '{resolved}')");
        }

        private static void CheckMissingAssemblyIsSafe()
        {
            Check(ModIdentity.ResolveModId(null).Length == 0, "a null assembly resolves to an empty id instead of throwing");
        }

        private static Assembly BuildAssembly(string? key, string? value)
        {
            var name = new AssemblyName($"SprocketModAPI.ContractTests.Identity{Guid.NewGuid():N}");
            AssemblyBuilder builder = AssemblyBuilder.DefineDynamicAssembly(name, AssemblyBuilderAccess.Run);
            if (key != null && value != null)
            {
                builder.SetCustomAttribute(new CustomAttributeBuilder(
                    typeof(AssemblyMetadataAttribute).GetConstructor(new[] { typeof(string), typeof(string) })!,
                    new object[] { key, value }));
            }

            return builder;
        }

        private static void Check(bool condition, string name)
        {
            if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
        }
    }
}
