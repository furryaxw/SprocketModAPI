using System;
using System.IO;
using System.Text;

// 文本完整性契约：整个仓库的文本文件必须是合法 UTF-8，且不能含私用区字符。
//
// 为什么需要它：PowerShell 的 `Get-Content`/`Set-Content` 来回读写（`-replace` 就地改文件）
// 会在非 UTF-8 的默认代码页下把中文做一次"UTF-8 → GBK → UTF-8"双编码：多数汉字变成乱码，
// 少数位置直接丢字节（包括吃掉换行），文件看起来还"像那么回事"，只有读的时候才会发现。
// 这类损坏必须在构建阶段就炸掉，而不是等某次 grep 才偶然发现。
internal static class TextIntegrityTests
{
    private static readonly UTF8Encoding StrictUtf8 = new(encoderShouldEmitUTF8Identifier: false, throwOnInvalidBytes: true);

    internal static void Run()
    {
        string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
        int inspected = 0;

        foreach (string path in Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories))
        {
            if (!IsTextFile(path) || IsGenerated(path))
                continue;

            inspected++;
            string text;
            try
            {
                text = StrictUtf8.GetString(File.ReadAllBytes(path));
            }
            catch (DecoderFallbackException)
            {
                throw new InvalidOperationException(
                    $"Text integrity failed: {Relative(root, path)} is not valid UTF-8 (a mangled text round-trip?).");
            }

            foreach (char character in text)
            {
                if (character >= '\uE000' && character <= '\uF8FF')
                {
                    throw new InvalidOperationException(
                        $"Text integrity failed: {Relative(root, path)} contains private-use character U+{(int)character:X4} "
                        + "(a DBCS decoder replaced lost bytes; re-read the file before trusting it).");
                }
            }
        }

        Check(inspected > 40, $"text integrity inspected {inspected} files");
    }

    private static bool IsTextFile(string path)
    {
        string extension = Path.GetExtension(path);
        return extension is ".cs" or ".md" or ".ps1" or ".csproj" or ".json" or ".props" or ".txt";
    }

    private static bool IsGenerated(string path)
    {
        string separator = Path.DirectorySeparatorChar.ToString();
        return path.Contains($"{separator}bin{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}obj{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}.git{separator}", StringComparison.Ordinal)
            || path.Contains($"{separator}snapshots{separator}", StringComparison.Ordinal);
    }

    private static string Relative(string root, string path) => Path.GetRelativePath(root, path);

    private static void Check(bool condition, string name)
    {
        if (!condition) throw new InvalidOperationException($"Contract failed: {name}");
    }
}
