using TransitShelf.Core;

namespace TransitShelf.Tests;

public class ShelfItemFilterTests
{
    private static ShelfItem File(string name, string path = @"C:\work\") => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.File,
        DisplayName = name,
        Path = Path.Combine(path, name),
        CreatedAt = DateTimeOffset.Now,
    };

    private static ShelfItem Folder(string name) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.Folder,
        DisplayName = name,
        Path = Path.Combine(@"C:\work", name),
        CreatedAt = DateTimeOffset.Now,
    };

    private static ShelfItem Image(string name) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.File,
        DisplayName = name,
        CachePath = Path.Combine(@"C:\cache", name),
        CreatedAt = DateTimeOffset.Now,
    };

    private static ShelfItem Text(string text) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.Text,
        DisplayName = "网页摘录",
        Text = text,
        CreatedAt = DateTimeOffset.Now,
    };

    [Fact]
    public void 空搜索匹配全部类型()
    {
        Assert.True(ShelfItemFilter.Matches(File("报告.docx"), "", ShelfFilterKind.All));
        Assert.True(ShelfItemFilter.Matches(Folder("项目"), "   ", ShelfFilterKind.All));
    }

    [Fact]
    public void 搜索忽略大小写并匹配显示名路径和文字内容()
    {
        Assert.True(ShelfItemFilter.Matches(File("Report.docx"), "report", ShelfFilterKind.All));
        Assert.True(ShelfItemFilter.Matches(File("报告.docx", @"C:\Reports"), "reports", ShelfFilterKind.All));
        Assert.True(ShelfItemFilter.Matches(Image("截图.png"), "cache", ShelfFilterKind.All));
        Assert.True(ShelfItemFilter.Matches(Text("含有关键字的长段落"), "关键字", ShelfFilterKind.All));
        Assert.False(ShelfItemFilter.Matches(File("报告.docx"), "不存在", ShelfFilterKind.All));
    }

    [Fact]
    public void 类型筛选把图片从普通文件中单独分出()
    {
        Assert.True(ShelfItemFilter.Matches(File("报告.docx"), null, ShelfFilterKind.File));
        Assert.False(ShelfItemFilter.Matches(Image("截图.png"), null, ShelfFilterKind.File));
        Assert.True(ShelfItemFilter.Matches(Image("截图.png"), null, ShelfFilterKind.Image));
        Assert.True(ShelfItemFilter.Matches(Folder("项目"), null, ShelfFilterKind.Folder));
        Assert.True(ShelfItemFilter.Matches(Text("文字"), null, ShelfFilterKind.Text));
    }

    [Fact]
    public void 搜索与类型筛选同时生效()
    {
        Assert.True(ShelfItemFilter.Matches(Image("旅行-海边.png"), "海边", ShelfFilterKind.Image));
        Assert.False(ShelfItemFilter.Matches(Image("旅行-海边.png"), "海边", ShelfFilterKind.File));
        Assert.False(ShelfItemFilter.Matches(File("旅行-海边.docx"), "海边", ShelfFilterKind.Image));
    }
}
