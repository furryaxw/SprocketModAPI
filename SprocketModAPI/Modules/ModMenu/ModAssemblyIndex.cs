using System;
using System.Collections.Generic;
using System.IO;

namespace SprocketModAPI
{
    // 目录里可用的程序集名索引（按文件名主干）。
    //
    // 依赖检查不能只看已注册的 melon：`MelonBase.RegisteredMelons` 不含 `UserLibs` 里的库
    // （例如 `SprocketDepth`），而模组可以合法地依赖它们。所以这里把三个根目录下的
    // `*.dll` / `*.dll.disable` 文件名主干都算作「本机存在该程序集」。
    internal static class ModAssemblyIndex
    {
        internal static IReadOnlyList<string> Collect(params string[] directories)
            => Collect((IEnumerable<string>)directories);

        internal static IReadOnlyList<string> Collect(IEnumerable<string>? directories)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (directories == null)
                return names;

            foreach (string directory in directories)
            {
                if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                    continue;

                string[] files;
                try
                {
                    files = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly);
                }
                catch (Exception)
                {
                    continue;
                }

                foreach (string file in files)
                {
                    if (!ModFileToggle.IsLoadablePath(file) && !ModFileToggle.IsDisabledPath(file))
                        continue;

                    string stem = Stem(file);
                    if (stem.Length != 0 && seen.Add(stem))
                        names.Add(stem);
                }
            }

            return names;
        }

        // `X.dll` 与 `X.dll.disable` 都算作程序集 `X`。
        internal static string Stem(string path)
        {
            string name = Path.GetFileName(path);
            if (name.EndsWith(ModFileToggle.DisableSuffix, StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ModFileToggle.DisableSuffix.Length);
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
                name = name.Substring(0, name.Length - ".dll".Length);
            return name;
        }
    }
}
