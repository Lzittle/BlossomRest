using System.Diagnostics;
using System.IO;

namespace TransitShelf.Platform;

/// <summary>
/// 慢操作日志：只记录耗时超过阈值的操作，用于事后定位"卡一下 / 卡死"是哪一步造成的。
/// 常驻小工具的界面线程一旦被阻塞，用户看到的就是"程序没反应"，没有记录就无从查起。
/// </summary>
public static class Diagnostics
{
    private const int SlowThresholdMs = 800;

    public static void LogSlow(string operation, TimeSpan elapsed)
    {
        if (elapsed.TotalMilliseconds < SlowThresholdMs)
        {
            return;
        }

        try
        {
            AppPaths.EnsureCreated();
            File.AppendAllText(
                Path.Combine(AppPaths.Root, "slow.log"),
                $"[{DateTimeOffset.Now:O}] {operation} 用时 {elapsed.TotalMilliseconds:F0} ms{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 记录失败不影响功能
        }
    }

    public static T Measure<T>(string operation, Func<T> action)
    {
        var watch = Stopwatch.StartNew();
        try
        {
            return action();
        }
        finally
        {
            LogSlow(operation, watch.Elapsed);
        }
    }
}
