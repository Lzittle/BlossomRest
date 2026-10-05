using TransitShelf.Core;
using TransitShelf.Core.Settings;

namespace TransitShelf.Tests;

public class WatchTargetsTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"transitshelf-watch-{Guid.NewGuid():n}");

    public WatchTargetsTests()
    {
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }
    }

    private string MakeDir(params string[] parts)
    {
        var path = Path.Combine([_root, .. parts]);
        Directory.CreateDirectory(path);
        return path;
    }

    [Fact]
    public void 未开启任何开关时不监听任何目录()
    {
        MakeDir("Downloads");

        var targets = WatchTargets.Resolve(_root, _root, new ShelfSettings());

        Assert.Empty(targets);
    }

    [Fact]
    public void 开启下载目录时返回Downloads()
    {
        var downloads = MakeDir("Downloads");

        var targets = WatchTargets.Resolve(_root, _root, new ShelfSettings { WatchDownloads = true });

        Assert.Equal(downloads, Assert.Single(targets));
    }

    [Fact]
    public void 开启微信监听时枚举每个账号的接收目录()
    {
        var wechat4 = MakeDir("xwechat_files", "wxid_aaa", "msg", "file");
        MakeDir("xwechat_files", "wxid_bbb", "msg", "file");
        var wechat3 = MakeDir("Documents", "WeChat Files", "wxid_ccc", "FileStorage", "File");

        var targets = WatchTargets.Resolve(_root, Path.Combine(_root, "Documents"),
            new ShelfSettings { WatchWeChat = true });

        Assert.Contains(wechat4, targets);
        Assert.Contains(wechat3, targets);
        Assert.Equal(3, targets.Count);
    }

    [Fact]
    public void 额外监听目录会去重且忽略不存在的路径()
    {
        var downloads = MakeDir("Downloads");
        var extra = MakeDir("my-folder");

        var targets = WatchTargets.Resolve(_root, _root, new ShelfSettings
        {
            WatchDownloads = true,
            ExtraWatchDirectories = [extra, downloads, Path.Combine(_root, "not-exists")],
        });

        Assert.Equal(2, targets.Count);
        Assert.Contains(extra, targets);
        Assert.Contains(downloads, targets);
    }
}
