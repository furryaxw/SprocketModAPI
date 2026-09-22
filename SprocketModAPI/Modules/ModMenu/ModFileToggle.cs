using System;
using System.Collections.Generic;
using System.IO;

namespace SprocketModAPI
{
    // 启用/禁用磁盘上模组 DLL 的结果。禁用只是改名，因此必须重启才生效。
    internal sealed class ModToggleResult
    {
        internal ModToggleResult(bool succeeded, string path, string message, bool restartRequired)
        {
            Succeeded = succeeded;
            Path = path;
            Message = message;
            RestartRequired = restartRequired;
        }

        internal bool Succeeded { get; }
        internal string Path { get; }
        internal string Message { get; }
        internal bool RestartRequired { get; }

        internal static ModToggleResult Ok(string path) => new(true, path, "", true);
        internal static ModToggleResult Failed(string message) => new(false, "", message, false);
    }

    // 启用/禁用约定（见 `docs/mod-metadata.md`）：把 `<Name>.dll` 改名为
    // `<Name>.dll.disable`。MelonLoader 只加载 `*.dll`，所以下一轮启动才会生效。
    // 本类只做文件改名，不认识编译器、不加载程序集。
    internal static class ModFileToggle
    {
        internal const string DisableSuffix = ".disable";
        private const string DllSuffix = ".dll";

        internal static bool IsDisabledPath(string path)
            => !string.IsNullOrEmpty(path)
                && path.EndsWith(DllSuffix + DisableSuffix, StringComparison.OrdinalIgnoreCase);

        internal static bool IsLoadablePath(string path)
            => !string.IsNullOrEmpty(path)
                && path.EndsWith(DllSuffix, StringComparison.OrdinalIgnoreCase)
                && !IsDisabledPath(path);

        internal static string DisabledPathFor(string path) => path + DisableSuffix;

        // 磁盘上的实际路径：文件已被改名为 `.dll.disable` 时返回禁用路径，否则返回可加载路径。
        internal static string ActualPathFor(string path)
        {
            if (string.IsNullOrEmpty(path) || IsDisabledPath(path))
                return path;
            string disabled = DisabledPathFor(path);
            return File.Exists(disabled) && !File.Exists(path) ? disabled : path;
        }

        internal static string EnabledPathFor(string path)
            => IsDisabledPath(path) ? path.Substring(0, path.Length - DisableSuffix.Length) : path;

        // 禁用：`X.dll` → `X.dll.disable`。目标已存在或源文件不存在时拒绝。
        internal static ModToggleResult Disable(string path)
            => Rename(path, disabled: true);

        // 启用：`X.dll.disable` → `X.dll`。目标已存在或源文件不存在时拒绝。
        internal static ModToggleResult Enable(string path)
            => Rename(path, disabled: false);

        private static ModToggleResult Rename(string path, bool disabled)
        {
            if (string.IsNullOrWhiteSpace(path))
                return ModToggleResult.Failed("Path is empty.");

            if (disabled && !IsLoadablePath(path))
                return ModToggleResult.Failed("Only .dll files can be disabled.");
            if (!disabled && !IsDisabledPath(path))
                return ModToggleResult.Failed("Only .dll.disable files can be enabled.");
            if (!File.Exists(path))
                return ModToggleResult.Failed("File does not exist.");

            string target = disabled ? DisabledPathFor(path) : EnabledPathFor(path);
            if (File.Exists(target))
                return ModToggleResult.Failed($"Target file already exists: {Path.GetFileName(target)}");

            try
            {
                File.Move(path, target);
                return ModToggleResult.Ok(target);
            }
            catch (Exception exception)
            {
                return ModToggleResult.Failed(exception.Message);
            }
        }

        // 枚举目录顶层的 `*.dll.disable`，用于把禁用条目放进菜单。
        internal static IReadOnlyList<string> FindDisabledDlls(string directory)
        {
            if (string.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return Array.Empty<string>();

            var result = new List<string>();
            try
            {
                foreach (string path in Directory.GetFiles(directory, "*" + DllSuffix + DisableSuffix))
                    result.Add(path);
            }
            catch (Exception)
            {
                return Array.Empty<string>();
            }

            result.Sort(StringComparer.OrdinalIgnoreCase);
            return result;
        }
    }
}
