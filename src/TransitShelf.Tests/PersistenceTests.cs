using TransitShelf.Core;
using TransitShelf.Core.Persistence;
using TransitShelf.Core.Settings;

namespace TransitShelf.Tests;

public class PersistenceTests
{
    private static ShelfItem Sample(string name, bool pinned = false) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.File,
        DisplayName = name,
        Path = @"C:\temp\" + name,
        CreatedAt = DateTimeOffset.Now,
        Pinned = pinned,
    };

    [Fact]
    public void 条目与设置能往返序列化()
    {
        var items = new[] { Sample("甲.docx", pinned: true), Sample("乙.txt") };
        var settings = new ShelfSettings { Capacity = 5, TimeToLiveHours = 48, WatchDownloads = true };

        var json = ShelfStateCodec.Serialize(items, settings);
        var (loaded, _, loadedSettings) = ShelfStateCodec.DeserializeSnapshot(json);

        Assert.Equal(2, loaded.Count);
        Assert.Equal("甲.docx", loaded[0].DisplayName);
        Assert.True(loaded[0].Pinned);
        Assert.Equal(5, loadedSettings.Capacity);
        Assert.Equal(48, loadedSettings.TimeToLiveHours);
        Assert.True(loadedSettings.WatchDownloads);
    }

    [Fact]
    public void 状态文件采用原子写入且不残留临时文件()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transitshelf-test-{Guid.NewGuid():n}.json");
        var store = new ItemStore(path);

        try
        {
            store.Save([Sample("甲.txt")]);
            Assert.True(File.Exists(path));
            Assert.False(File.Exists(path + ".tmp"));
            Assert.Single(store.Load());
        }
        finally
        {
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact]
    public void 状态文件损坏时不抛异常()
    {
        var path = Path.Combine(Path.GetTempPath(), $"transitshelf-broken-{Guid.NewGuid():n}.json");
        File.WriteAllText(path, "{ 这不是合法 JSON");
        var store = new ItemStore(path);

        try
        {
            var (items, _, settings) = store.LoadSnapshot();
            Assert.Empty(items);
            Assert.Equal(9, settings.Capacity);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 未钉住的过期条目会被清理而钉住的保留()
    {
        var now = DateTimeOffset.Now;
        var items = new[]
        {
            new ShelfItem { Id = "a", Kind = ItemKind.File, DisplayName = "新的", Path = @"C:\temp\a", CreatedAt = now.AddHours(-1) },
            new ShelfItem { Id = "b", Kind = ItemKind.File, DisplayName = "过期的", Path = @"C:\temp\b", CreatedAt = now.AddHours(-30) },
            new ShelfItem { Id = "c", Kind = ItemKind.File, DisplayName = "过期的钉住", Path = @"C:\temp\c", CreatedAt = now.AddHours(-30), Pinned = true },
        };

        var kept = ShelfRetention.RemoveExpired(items, TimeSpan.FromHours(24), now);

        Assert.Equal(2, kept.Count);
        Assert.DoesNotContain(kept, item => item.DisplayName == "过期的");
        Assert.Contains(kept, item => item.DisplayName == "过期的钉住");
    }
}
