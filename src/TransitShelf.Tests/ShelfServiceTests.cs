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
}
