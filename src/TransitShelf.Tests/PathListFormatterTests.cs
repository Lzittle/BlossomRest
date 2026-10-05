using TransitShelf.Core;

namespace TransitShelf.Tests;

public class PathListFormatterTests
{
    private static ShelfItem File(string path) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.File,
        DisplayName = Path.GetFileName(path),
        Path = path,
        CreatedAt = DateTimeOffset.Now,
    };

    private static ShelfItem CachedImage(string cachePath, int minutesAgo = 0) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.File,
        DisplayName = "截图.png",
        CachePath = cachePath,
        CreatedAt = DateTimeOffset.Now.AddMinutes(-minutesAgo),
    };

    private static ShelfItem Text(string text, int minutesAgo = 0) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.Text,
        DisplayName = text,
        Text = text,
        CreatedAt = DateTimeOffset.Now.AddMinutes(-minutesAgo),
    };

    [Fact]
    public void 路径清单用换行分隔且保持传入顺序()
    {
        var list = PathListFormatter.Format(
        [
            File(@"C:\Users\<user>\a.txt"),
            File(@"C:\Users\<user>\b.txt"),
        ]);

        Assert.Equal(@"C:\Users\<user>\a.txt" + Environment.NewLine + @"C:\Users\<user>\b.txt", list);
    }

    [Fact]
    public void 无源图片取缓存Png路径()
    {
        var list = PathListFormatter.Format([CachedImage(@"C:\Users\<user>\cache\截图.png")]);

        Assert.Equal(@"C:\Users\<user>\cache\截图.png", list);
    }

    [Fact]
    public void 纯文字条目被跳过且不计入路径数()
    {
        ShelfItem[] items =
        [
            File(@"C:\Users\<user>\a.txt"),
            Text("一段没有路径的文字"),
            CachedImage(@"C:\Users\<user>\cache\截图.png"),
        ];

        Assert.Equal(2, PathListFormatter.Paths(items).Count);
        Assert.DoesNotContain("一段没有路径的文字", PathListFormatter.Format(items));
    }

    [Fact]
    public void 没有可复制条目时清单为空字符串()
    {
        Assert.Equal(string.Empty, PathListFormatter.Format([Text("只有文字")]));
        Assert.Equal(string.Empty, PathListFormatter.Format([]));
    }
}
