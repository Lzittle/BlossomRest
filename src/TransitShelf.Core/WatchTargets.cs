using TransitShelf.Core.Settings;

namespace TransitShelf.Core;

/// <summary>
/// 根据设置解析"要监听哪些目录"（FR-15）。
/// 纯路径计算，便于单元测试；目录是否存在也在这里判断，调用方拿到即可直接监听。
/// </summary>
public static class WatchTargets
{
    public static IReadOnlyList<string> Resolve(string userProfile, string documents, ShelfSettings settings)
    {
        var targets = new List<string>();

        if (settings.WatchDownloads)
        {
            Add(targets, System.IO.Path.Combine(userProfile, "Downloads"));
        }

        if (settings.WatchWeChat)
        {
            AddRange(targets, ResolveWeChat(userProfile, documents));
        }

        foreach (var extra in settings.ExtraWatchDirectories)
        {
            Add(targets, extra);
        }

        return targets
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// 微信接收文件的目录：
    /// 4.x 为 <c>&lt;profile&gt;\xwechat_files\&lt;wxid&gt;\msg\file</c>，
    /// 3.x 遗留为 <c>&lt;documents&gt;\WeChat Files\&lt;wxid&gt;\FileStorage\File</c>。
    /// 账号目录按 wxid 一层层枚举，找不到就返回空。
    /// </summary>
    public static IEnumerable<string> ResolveWeChat(string userProfile, string documents)
    {
        foreach (var account in EnumerateAccounts(System.IO.Path.Combine(userProfile, "xwechat_files")))
        {
            yield return System.IO.Path.Combine(account, "msg", "file");
        }

        foreach (var account in EnumerateAccounts(System.IO.Path.Combine(documents, "WeChat Files")))
        {
            yield return System.IO.Path.Combine(account, "FileStorage", "File");
        }
    }

    private static IEnumerable<string> EnumerateAccounts(string root)
    {
        if (!Directory.Exists(root))
        {
            return [];
        }

        try
        {
            return Directory.EnumerateDirectories(root).ToList();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static void Add(List<string> targets, string path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return;
        }

        try
        {
            if (Directory.Exists(path))
            {
                targets.Add(System.IO.Path.GetFullPath(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            // 路径非法则忽略
        }
    }

    private static void AddRange(List<string> targets, IEnumerable<string> paths)
    {
        foreach (var path in paths)
        {
            Add(targets, path);
        }
    }
}
