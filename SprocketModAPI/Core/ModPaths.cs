using System.IO;
using BepInEx;

namespace SprocketModAPI
{
    // 模组数据与加载目录。BepInEx 只提供加载器自身的路径，模组侧的布局在这里统一定义。
    internal static class ModPaths
    {
        // 键位、模组配置与每个模组自己的数据都住在这里。
        internal static string ModDataRoot => Path.Combine(Paths.ConfigPath, "SprocketModAPI");

        // 插件目录：已加载插件的程序集与 `.dll.disable` 条目都住在这里。
        internal static string PluginsRoot => Paths.PluginPath;
    }
}
