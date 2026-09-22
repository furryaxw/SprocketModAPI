using System;
using System.Reflection;

namespace SprocketModAPI
{
    // 模组身份解析：**键位与配置的命名空间一律用 `Sprocket.Mod.Id`**。
    //
    // 模组注册动作/配置时不必手传 `ModId`：服务从**调用方程序集**读 `Sprocket.Mod.Id`；
    // 没声明就退化为 `file:<程序集名>`——与元数据读取器的派生规则保持一致。
    // 显式传了 `ModId` 就用显式的（覆盖路径，给"经中间库转发调用"的情况）。
    //
    // 为什么放在 Core：它是**身份解析**这一共享能力，不属于任何模块的实现；键位模块与配置模块都要用，
    // 而模块之间不允许互相依赖内部类型。
    internal static class ModIdentity
    {
        internal const string IdKey = "Sprocket.Mod.Id";

        // 从调用方程序集解析 ModId。**调用点**必须自己传 `Assembly.GetCallingAssembly()`。
        internal static string ResolveModId(Assembly? assembly)
        {
            if (assembly == null)
                return "";

            try
            {
                foreach (AssemblyMetadataAttribute attribute in assembly.GetCustomAttributes<AssemblyMetadataAttribute>())
                {
                    if (!string.Equals(attribute.Key, IdKey, StringComparison.Ordinal))
                        continue;
                    string? value = attribute.Value?.Trim();
                    if (!string.IsNullOrEmpty(value))
                        return value;
                }
            }
            catch (Exception)
            {
                // 读不到属性（动态程序集/反射受限）不应该让注册失败：退化为程序集名派生。
            }

            string name = assembly.GetName().Name ?? "";
            // 退化值是**纯程序集名**，刻意不带 `file:` 前缀：这个字符串既当键位 StableId 的前缀
            // （`<modId>:<actionId>`，冒号是分隔符），又当 `modconfig/<modId>.json` 的文件名，
            // 含冒号会被 Validate 拒绝、在 Windows 上也是非法文件名。
            // （管理器展示用的元数据 Id 退化仍是 `file:<程序集名>`，那是另一个命名空间。）
            return name;
        }
    }
}
