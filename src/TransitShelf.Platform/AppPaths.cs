using System.IO;

namespace TransitShelf.Platform;

/// <summary>
/// 本程序在用户目录下的落盘位置（见技术框架设计 4.2）。
/// 全部位于 %LOCALAPPDATA% 下的 TransitShelf 目录，删除该目录不影响任何用户原始文件。
/// </summary>
public static class AppPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "TransitShelf");

    public static string StateFile { get; } = Path.Combine(Root, "state.json");

    public static string CacheDirectory { get; } = Path.Combine(Root, "cache");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Root);
        Directory.CreateDirectory(CacheDirectory);
    }

    /// <summary>删除不被任何条目引用的孤儿缓存文件（崩溃或强杀后遗留）。</summary>
    public static int CleanOrphanCache(IEnumerable<string?> referencedPaths)
    {
        if (!Directory.Exists(CacheDirectory))
        {
            return 0;
        }

        var referenced = referencedPaths
            .Where(p => !string.IsNullOrWhiteSpace(p))
            .Select(p => Path.GetFullPath(p!))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var removed = 0;
        foreach (var file in Directory.EnumerateFiles(CacheDirectory, "*", SearchOption.TopDirectoryOnly))
        {
            if (referenced.Contains(Path.GetFullPath(file)))
            {
                continue;
            }

            try
            {
                File.Delete(file);
                removed++;
            }
            catch (IOException)
            {
                // 被占用则留到下次启动再清理
            }
        }

        return removed;
    }
}
