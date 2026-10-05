using System;
using System.IO;
using BepInEx;

namespace SprocketModAPI
{
    // 模组数据与加载目录。BepInEx 只提供加载器自身的路径，模组侧的布局在这里统一定义。
    internal static class ModPaths
    {
        // 与 MelonLoader 时代的 `UserData` 目录保持同一位置，键位与配置不需要搬迁。
        internal static string UserDataRoot => Path.Combine(Paths.GameRootPath, "UserData");

        internal static string ModDataRoot => Path.Combine(UserDataRoot, "SprocketModAPI");

        // 插件目录：已加载插件的程序集与 `.dll.disable` 条目都住在这里。
        internal static string PluginsRoot => Paths.PluginPath;

        // 共享库目录：`MLLoader` 时代由 `UserLibs` 承担，BepInEx 下依赖由插件目录解析。
        internal static string SharedLibraryRoot => Path.Combine(Paths.GameRootPath, "UserLibs");
    }
}
