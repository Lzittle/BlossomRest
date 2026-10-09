using TransitShelf.Core;

namespace TransitShelf.Tests;

public class ShelfServiceTests
{
    private static ShelfItem Item(string name, bool pinned = false, int minutesAgo = 0) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.File,
        DisplayName = name,
        Path = Path.Combine(Path.GetTempPath(), name),
        CreatedAt = DateTimeOffset.Now.AddMinutes(-minutesAgo),
        Pinned = pinned,
    };

    [Fact]
    public void 钉住的条目永远排在未钉住的前面()
    {
        var shelf = new ShelfService();

        shelf.AddText("最先收录");
        var pinned = shelf.AddText("中间收录");
        shelf.TogglePin(pinned);
        shelf.AddText("最新收录");

        Assert.True(shelf.Items[0].Pinned);
        Assert.Equal("中间收录", shelf.Items[0].DisplayName);
        Assert.All(shelf.Items.Skip(1), item => Assert.False(item.Pinned));
    }

    [Fact]
    public void 新条目不会把钉住的挤下去()
    {
        var shelf = new ShelfService();
        var pinned = shelf.AddText("钉住的");
        shelf.TogglePin(pinned);

        for (var i = 0; i < 5; i++)
        {
            shelf.AddText($"新条目 {i}");
        }

        Assert.Equal("钉住的", shelf.Items[0].DisplayName);
    }

    [Fact]
    public void 清空只清未钉住的并保留钉住项()
    {
        var shelf = new ShelfService();
        shelf.AddText("普通一");
        var pinned = shelf.AddText("钉住的");
        shelf.TogglePin(pinned);
        shelf.AddText("普通二");

        var (removed, kept) = shelf.ClearUnpinned();

        Assert.Equal(2, removed);
        Assert.Equal(1, kept);
        Assert.Equal("钉住的", Assert.Single(shelf.Items).DisplayName);
    }

    [Fact]
    public void 超出上限时淘汰最旧的未钉住项而保留钉住项()
    {
        var shelf = new ShelfService { Capacity = 3 };
        shelf.AddText("普通一");
        var pinned = shelf.AddText("钉住的");
        shelf.TogglePin(pinned);
        shelf.AddText("普通二");
        shelf.AddText("普通三");

        Assert.Equal(3, shelf.Items.Count);
        Assert.Contains(shelf.Items, item => item.DisplayName == "钉住的");
    }

    [Fact]
    public void 缩减上限时不会淘汰钉住项()
    {
        var shelf = new ShelfService { Capacity = 2 };
        var pinned = shelf.AddText("钉住的");
        shelf.TogglePin(pinned);
        shelf.AddText("普通一");
        shelf.AddText("普通二");

        shelf.Capacity = 1;

        Assert.Single(shelf.Items);
        Assert.True(shelf.Items[0].Pinned);
    }

    [Fact]
    public void 取消钉住后恢复正常排序与淘汰()
    {
        var shelf = new ShelfService();
        var item = shelf.AddText("先钉住再取消");
        shelf.TogglePin(item);
        Assert.True(shelf.Items[0].Pinned);

        shelf.TogglePin(item);

        Assert.False(shelf.Items[0].Pinned);
    }

    [Fact]
    public void 从持久化状态恢复后仍然保持钉住在前的顺序()
    {
        var shelf = new ShelfService();
        shelf.LoadFrom(
        [
            Item("普通新", minutesAgo: 1),
            Item("钉住旧", pinned: true, minutesAgo: 100),
            Item("普通旧", minutesAgo: 200),
        ]);

        Assert.Equal("钉住旧", shelf.Items[0].DisplayName);
        Assert.Equal(3, shelf.Items.Count);
    }

    // ── 回归：显示名撞名与重复收录（2026-10-08 实机反馈） ──────────────────

    [Fact]
    public void 截图中不同内容的显示名不会重复()
    {
        var shelf = new ShelfService();

        var first = shelf.AddCachedImage(@"C:\cache\clip-1.png", "截图 1008-143012.png");
        var second = shelf.AddCachedImage(@"C:\cache\clip-2.png", "截图 1008-143012.png");

        Assert.Equal(2, shelf.Items.Count);
        Assert.NotEqual(first.DisplayName, second.DisplayName);
    }

    [Fact]
    public void 截图中三个同名条目会得到互不相同的名字()
    {
        var shelf = new ShelfService();

        var names = new[]
        {
            shelf.AddCachedImage(@"C:\cache\a.png", "截图 1008-143012.png").DisplayName,
            shelf.AddCachedImage(@"C:\cache\b.png", "截图 1008-143012.png").DisplayName,
            shelf.AddCachedImage(@"C:\cache\c.png", "截图 1008-143012.png").DisplayName,
        };

        Assert.Equal(3, names.Distinct().Count());
    }

    [Fact]
    public void 名字相同但路径不同的文件会得到不同的显示名()
    {
        var shelf = new ShelfService();
        var root = Path.Combine(Path.GetTempPath(), $"transitshelf-name-{Guid.NewGuid():n}");
        var left = Path.Combine(root, "左");
        var right = Path.Combine(root, "右");
        Directory.CreateDirectory(left);
        Directory.CreateDirectory(right);

        try
        {
            var a = shelf.AddFile(Path.Combine(left, "笔记.md"));
            var b = shelf.AddFile(Path.Combine(right, "笔记.md"));

            Assert.Equal(2, shelf.Items.Count);
            Assert.NotEqual(a.DisplayName, b.DisplayName);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    [Fact]
    public void 长文本截断后同名也会被消歧()
    {
        var shelf = new ShelfService();

        var first = shelf.AddText(new string('甲', 60) + "尾巴一");
        var second = shelf.AddText(new string('甲', 60) + "尾巴二");

        Assert.Equal(2, shelf.Items.Count);
        Assert.NotEqual(first.DisplayName, second.DisplayName);
    }

    [Fact]
    public void 重复收录同一段文字会刷新时间并提到最前()
    {
        var shelf = new ShelfService();
        var original = shelf.AddText("重复收录的内容");

        // 插一条别的，把原条目挤到后面
        shelf.AddText("后来的内容");
        Assert.NotSame(original, shelf.Items[0]);
        original.CreatedAt = DateTimeOffset.Now.AddHours(-3);

        var again = shelf.AddText("重复收录的内容");

        Assert.Same(original, again);
        Assert.Same(original, shelf.Items[0]);
        Assert.Equal("刚刚", original.RelativeTime);
    }

    [Fact]
    public void 重复收录不会让条目总数变多()
    {
        var shelf = new ShelfService();

        shelf.AddText("甲");
        shelf.AddText("甲");
        shelf.AddText("甲");

        Assert.Single(shelf.Items);
    }

    [Fact]
    public void 重复收录钉住的条目仍然排在最前()
    {
        var shelf = new ShelfService();
        var pinned = shelf.AddText("钉住的内容");
        shelf.TogglePin(pinned);
        shelf.AddText("普通内容");

        shelf.AddText("钉住的内容");

        Assert.True(shelf.Items[0].Pinned);
    }

    [Fact]
    public void 重复收录同一个文件会刷新时间()
    {
        var shelf = new ShelfService();
        var path = Path.Combine(Path.GetTempPath(), $"transitshelf-{Guid.NewGuid():n}.txt");
        File.WriteAllText(path, "x");

        try
        {
            var first = shelf.AddFile(path);
            first.CreatedAt = DateTimeOffset.Now.AddHours(-5);

            var again = shelf.AddFile(path);

            Assert.Same(first, again);
            Assert.Equal("刚刚", again.RelativeTime);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void 刷新时间标签会通知相对时间重新计算()
    {
        var shelf = new ShelfService();
        var item = shelf.AddText("时间标签");
        var raised = 0;
        item.PropertyChanged += (_, args) =>
        {
            if (args.PropertyName == nameof(ShelfItem.RelativeTime))
            {
                raised++;
            }
        };

        shelf.RefreshTimeLabels();

        Assert.True(raised > 0, "RefreshTimeLabels 必须让相对时间发通知，否则界面上的时间不会走动");
    }
}
