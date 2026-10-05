using System.Collections.Concurrent;
using System.IO;

namespace TransitShelf.Platform;

/// <summary>
/// 目录监听（FR-15）。新文件出现后先确认它"稳定"再上报：
/// 微信、浏览器、下载工具都是先写临时文件再改名/续写，过早收录会收到半成品。
/// 判定条件：大小连续两次非零且未被改写，并且能被独占打开（说明写入方已关闭句柄）。
/// </summary>
public sealed class SourceWatcher : IDisposable
{
    private const int DebounceMs = 700;
    private const int StableSampleMs = 400;
    private const int MaxStableChecks = 15;
    private const int RequiredStableStreak = 2;

    private static readonly string[] IgnoredExtensions =
    [
        ".tmp", ".temp", ".part", ".crdownload", ".download", ".filepart", ".partial", ".!ut",
    ];

    private readonly List<FileSystemWatcher> _watchers = [];
    private readonly ConcurrentDictionary<string, CancellationTokenSource> _pending =
        new(StringComparer.OrdinalIgnoreCase);

    private Action<string>? _onFile;
    private string? _excludeRoot;

    public IReadOnlyList<string> WatchedDirectories { get; private set; } = [];

    public void Restart(IReadOnlyList<string> directories, Action<string> onFile, string? excludeRoot = null)
    {
        Stop();
        _onFile = onFile;
        _excludeRoot = excludeRoot;
        WatchedDirectories = directories;

        foreach (var directory in directories)
        {
            if (!Directory.Exists(directory))
            {
                continue;
            }

            try
            {
                var watcher = new FileSystemWatcher(directory)
                {
                    IncludeSubdirectories = true,
                    NotifyFilter = NotifyFilters.FileName | NotifyFilters.LastWrite | NotifyFilters.Size,
                    InternalBufferSize = 64 * 1024,
                };

                watcher.Created += OnFileEvent;
                watcher.Changed += OnFileEvent;
                watcher.Renamed += OnRenamed;
                watcher.EnableRaisingEvents = true;
                _watchers.Add(watcher);
            }
            catch (Exception ex) when (ex is IOException or ArgumentException or UnauthorizedAccessException)
            {
                // 单个目录监听失败不影响其它目录
            }
        }
    }

    public void Stop()
    {
        foreach (var watcher in _watchers)
        {
            watcher.EnableRaisingEvents = false;
            watcher.Dispose();
        }

        _watchers.Clear();

        foreach (var pending in _pending.Values)
        {
            pending.Cancel();
            pending.Dispose();
        }

        _pending.Clear();
        WatchedDirectories = [];
    }

    public void Dispose() => Stop();

    private void OnFileEvent(object sender, FileSystemEventArgs e) => Schedule(e.FullPath);

    private void OnRenamed(object sender, RenamedEventArgs e) => Schedule(e.FullPath);

    private void Schedule(string path)
    {
        if (ShouldIgnore(path))
        {
            return;
        }

        var cancellation = new CancellationTokenSource();
        _pending.AddOrUpdate(
            path,
            cancellation,
            (_, previous) =>
            {
                previous.Cancel();
                previous.Dispose();
                return cancellation;
            });

        _ = WaitAndReportAsync(path, cancellation);
    }

    private async Task WaitAndReportAsync(string path, CancellationTokenSource cancellation)
    {
        try
        {
            await Task.Delay(DebounceMs, cancellation.Token).ConfigureAwait(false);

            var stableStreak = 0;
            for (var attempt = 0; attempt < MaxStableChecks; attempt++)
            {
                if (cancellation.IsCancellationRequested)
                {
                    return;
                }

                if (IsStable(path))
                {
                    stableStreak++;
                    if (stableStreak >= RequiredStableStreak)
                    {
                        if (_pending.TryRemove(path, out _))
                        {
                            _onFile?.Invoke(path);
                        }

                        return;
                    }
                }
                else
                {
                    stableStreak = 0;
                }

                await Task.Delay(StableSampleMs, cancellation.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException)
        {
            // 期间又有新事件，交给新的等待任务处理
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 文件消失或无权限：忽略
        }
    }

    private static bool IsStable(string path)
    {
        try
        {
            if (!File.Exists(path))
            {
                return false;
            }

            var info = new FileInfo(path);
            if (info.Length == 0)
            {
                return false;
            }

            // 能被独占打开，说明写入方已经释放句柄
            using var stream = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.None);
            return stream.Length > 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }

    private bool ShouldIgnore(string path)
    {
        var name = Path.GetFileName(path);
        if (string.IsNullOrEmpty(name) || name.StartsWith('~') || name.StartsWith('.'))
        {
            return true;
        }

        if (IgnoredExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
        {
            return true;
        }

        return _excludeRoot is not null
            && path.StartsWith(_excludeRoot, StringComparison.OrdinalIgnoreCase);
    }
}
