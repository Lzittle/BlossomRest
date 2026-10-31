using TransitShelf.Core;
using TransitShelf.Core.Persistence;
using TransitShelf.Core.Settings;
using Xunit;

namespace TransitShelf.Tests;

/// <summary>
/// 单击行为（FR-70 / ADR D33）。这里是纯逻辑：行为 → 动作的映射，以及持久化往返。
/// 真实单击是否按设置执行属于手工验收，不在这里假装测到。
/// </summary>
public class ClickBehaviorTests
{
    [Theory]
    [InlineData(ItemKind.Folder, ClickAction.Open)] // 文件夹没有"复制内容"语义
    [InlineData(ItemKind.File, ClickAction.Copy)]
    [InlineData(ItemKind.Text, ClickAction.Copy)]
    public void 按类型时文件夹打开其余复制(ItemKind kind, ClickAction expected)
        => Assert.Equal(expected, ClickActionResolver.Resolve(kind, ShelfClickBehavior.ByKind));

    [Theory]
    [InlineData(ItemKind.Folder)]
    [InlineData(ItemKind.File)]
    [InlineData(ItemKind.Text)]
    public void 统一打开对所有类型生效(ItemKind kind)
        => Assert.Equal(ClickAction.Open, ClickActionResolver.Resolve(kind, ShelfClickBehavior.AlwaysOpen));

    [Theory]
    [InlineData(ItemKind.Folder)]
    [InlineData(ItemKind.File)]
    [InlineData(ItemKind.Text)]
    public void 统一复制对所有类型生效(ItemKind kind)
        => Assert.Equal(ClickAction.Copy, ClickActionResolver.Resolve(kind, ShelfClickBehavior.AlwaysCopy));

    [Fact]
    public void 默认按类型()
    {
        var settings = new ShelfSettings();

        Assert.Equal(ShelfClickBehavior.ByKind, settings.ClickBehavior);
    }

    [Fact]
    public void 单击行为能往返序列化()
    {
        var settings = new ShelfSettings { ClickBehavior = ShelfClickBehavior.AlwaysCopy };

        var json = ShelfStateCodec.Serialize([], settings);
        var (_, _, loaded) = ShelfStateCodec.DeserializeSnapshot(json);

        Assert.Equal(ShelfClickBehavior.AlwaysCopy, loaded.ClickBehavior);
    }

    /// <summary>旧配置没有 clickBehavior 字段时，必须落到"按类型"而不是别的行为。</summary>
    [Fact]
    public void 旧配置缺少字段时默认按类型()
    {
        const string legacy = """
        {
          "items": [],
          "settings": {
            "capacity": 9,
            "timeToLiveHours": 24,
            "palette": "Frappe"
          }
        }
        """;

        var (_, _, settings) = ShelfStateCodec.DeserializeSnapshot(legacy);

        Assert.Equal(ShelfClickBehavior.ByKind, settings.ClickBehavior);
    }
}
