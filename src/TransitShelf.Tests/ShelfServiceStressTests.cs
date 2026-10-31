using System.Diagnostics;
using TransitShelf.Core;
using TransitShelf.Core.Persistence;
using Xunit;

namespace TransitShelf.Tests;

/// <summary>
/// 压力测试：条目数量远超日常上限时，容量控制、过滤、序列化仍然正确且不退化。
/// 阈值故意放宽到秒级/百毫秒级——只用于兜住 O(n²)、无上限增长之类的性能回归，
/// 不做精确基准（CI 机器差异太大）。
/// </summary>
public class ShelfServiceStressTests
{
    [Fact]
    public void 连续收录五千个文件_始终保持上限()
    {
        var shelf = new ShelfService { Capacity = 99 };
        var stopwatch = Stopwatch.StartNew();
        for (var i = 0; i < 5000; i++)
        {
            shelf.AddFile($@"C:\Users\<user>\stress\file-{i}.txt");
        }

        stopwatch.Stop();

        Assert.Equal(99, shelf.Items.Count);
        Assert.Equal(99, shelf.TemporaryCount);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(10),
            $"5000 次收录耗时 {stopwatch.Elapsed}");
    }

    [Fact]
    public void 一千个条目过滤_单次低于两百毫秒()
    {
        var shelf = new ShelfService { Capacity = 1000 };
        for (var i = 0; i < 1000; i++)
        {
            shelf.AddText($"entry-{i:D4}-payload");
        }

        var items = shelf.Items.ToList();
        var stopwatch = Stopwatch.StartNew();
        var matched = items.Count(item => ShelfItemFilter.Matches(item, "payload", ShelfFilterKind.Text));
        stopwatch.Stop();

        Assert.Equal(1000, matched);
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromMilliseconds(200),
            $"1000 条过滤耗时 {stopwatch.Elapsed}");
    }

    [Fact]
    public void 一千个条目序列化往返_内容不丢失()
    {
        var shelf = new ShelfService { Capacity = 1000 };
        for (var i = 0; i < 1000; i++)
        {
            shelf.AddText($"entry-{i:D4}");
        }

        var stopwatch = Stopwatch.StartNew();
        var json = ShelfStateCodec.Serialize(shelf.Items);
        var restored = ShelfStateCodec.Deserialize(json);
        stopwatch.Stop();

        Assert.Equal(1000, restored.Count);
        Assert.Equal(
            shelf.Items.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal),
            restored.Select(item => item.Id).OrderBy(id => id, StringComparer.Ordinal));
        Assert.True(
            stopwatch.Elapsed < TimeSpan.FromSeconds(2),
            $"1000 条序列化往返耗时 {stopwatch.Elapsed}");
    }

    [Fact]
    public void 反复钉住清空一百轮_计数正确且不抛出()
    {
        var shelf = new ShelfService { Capacity = 99 };
        for (var round = 0; round < 100; round++)
        {
            for (var i = 0; i < 50; i++)
            {
                shelf.AddText($"round-{round}-item-{i}");
            }

            foreach (var item in shelf.Items.Take(10).ToList())
            {
                shelf.TogglePin(item);
            }

            var (_, kept) = shelf.ClearUnpinned();
            Assert.Equal(10, kept);

            foreach (var item in shelf.Items.ToList())
            {
                shelf.TogglePin(item);
            }

            shelf.Clear();
        }

        Assert.Empty(shelf.Items);
    }
}
