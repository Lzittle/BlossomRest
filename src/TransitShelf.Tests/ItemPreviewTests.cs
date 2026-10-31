using TransitShelf.Core;
using Xunit;

namespace TransitShelf.Tests;

/// <summary>
/// 缩略图资格判定（FR-52）：只有图片与视频走缩略图，其余类型继续用矢量图标。
/// 扩展名集合宁宽勿窄——解码失败时界面会自动回退，不会出现空图。
/// </summary>
public class ItemPreviewTests
{
    [Theory]
    [InlineData(@"C:\Users\<user>\Pictures\photo.png")]
    [InlineData(@"C:\Users\<user>\Pictures\photo.JPEG")]
    [InlineData(@"C:\Users\<user>\Videos\clip.mp4")]
    [InlineData(@"C:\Users\<user>\Videos\clip.MKV")]
    public void 图片与视频支持缩略图(string path) => Assert.True(ItemPreview.SupportsThumbnail(path));

    [Theory]
    [InlineData(@"C:\Users\<user>\Documents\report.pdf")]
    [InlineData(@"C:\Users\<user>\Documents\data.xlsx")]
    [InlineData(@"C:\Users\<user>\Downloads")]
    [InlineData("")]
    [InlineData(null)]
    public void 其他类型不支持缩略图(string? path) => Assert.False(ItemPreview.SupportsThumbnail(path));

    [Fact]
    public void 无源图片用缓存PNG作为缩略图路径()
    {
        var item = CreateItem(path: null, cachePath: @"C:\Users\<user>\AppData\Local\TransitShelf\cache\clip-1.png");

        Assert.True(item.HasThumbnail);
        Assert.Equal(item.CachePath, item.ThumbnailPath);
    }

    [Fact]
    public void 改名为视频后缩略图资格随之变化()
    {
        var item = CreateItem(path: @"C:\Users\<user>\Documents\report.pdf", cachePath: null);
        Assert.False(item.HasThumbnail);

        var changed = new List<string?>();
        item.PropertyChanged += (_, e) => changed.Add(e.PropertyName);
        item.Path = @"C:\Users\<user>\Videos\clip.mp4";

        Assert.True(item.HasThumbnail);
        Assert.Contains(nameof(ShelfItem.HasThumbnail), changed);
        Assert.Contains(nameof(ShelfItem.ThumbnailPath), changed);
    }

    private static ShelfItem CreateItem(string? path, string? cachePath) => new()
    {
        Id = Guid.NewGuid().ToString("n"),
        Kind = ItemKind.File,
        DisplayName = "示例",
        CreatedAt = DateTimeOffset.Now,
        Path = path,
        CachePath = cachePath,
    };
}
