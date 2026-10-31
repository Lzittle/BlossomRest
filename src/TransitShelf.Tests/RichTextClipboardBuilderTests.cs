using TransitShelf.Core;
using TransitShelf.Platform;

namespace TransitShelf.Tests;

public class RichTextClipboardBuilderTests
{
    private const string OnePixelPng =
        "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJAAAADUlEQVR42mP8z8BQDwAEhQGAhKmMIQAAAABJRU5ErkJggg==";

    [Fact]
    public void IsImage_OnlyAcceptsImageExtensions()
    {
        Assert.True(ItemPreview.IsImage(@"C:\temp\photo.PNG"));
        Assert.False(ItemPreview.IsImage(@"C:\temp\movie.mp4"));
        Assert.False(ItemPreview.IsImage(null));
    }

    [Fact]
    public void HasImage_DetectsCachedImagesAndImageFilesOnly()
    {
        var cached = Item("a", ItemKind.File, "截图", cachePath: @"C:\cache\a.png");
        var photo = Item("b", ItemKind.File, "photo.jpg", path: @"C:\temp\photo.jpg");
        var video = Item("c", ItemKind.File, "video.mp4", path: @"C:\temp\video.mp4");

        Assert.True(RichTextClipboardBuilder.HasImage([cached, photo]));
        Assert.False(RichTextClipboardBuilder.HasImage([video]));
    }

    [Fact]
    public void Build_EscapesRtfAndKeepsOutputAscii()
    {
        var text = Item("t", ItemKind.Text, "文字", text: "第一行\n{A}\\B");

        var rtf = RichTextClipboardBuilder.Build([text]);

        Assert.Contains(@"\{A\}", rtf);
        Assert.Contains(@"\\B", rtf);
        Assert.Contains(@"\par", rtf);
        Assert.Contains(@"\u31532?", rtf); // 第 = U+7B2C
        Assert.DoesNotContain("第", rtf);   // 中文必须转义成纯 ASCII
        Assert.EndsWith("}", rtf);
    }

    [Fact]
    public void Build_KeepsFilePathForNonImageItems()
    {
        var file = Item("f", ItemKind.File, "a.txt", path: @"C:\temp\a.txt");

        var rtf = RichTextClipboardBuilder.Build([file]);

        Assert.Contains(@"C:\\temp\\a.txt", rtf);
        Assert.DoesNotContain(@"\pict", rtf);
    }

    [Fact]
    public void Build_EmbedsCachedPngAsPicture()
    {
        var cachePath = Path.Combine(Path.GetTempPath(), "ts-rtf-" + Guid.NewGuid().ToString("n") + ".png");
        File.WriteAllBytes(cachePath, Convert.FromBase64String(OnePixelPng));

        try
        {
            var image = Item("img", ItemKind.File, "截图", cachePath: cachePath);

            var rtf = RichTextClipboardBuilder.Build([image]);

            Assert.Contains(@"{\pict\pngblip\picw1\pich1\picwgoal15\pichgoal15 ", rtf);
            Assert.Contains("89504e470d0a1a0a", rtf); // PNG 文件头，确认内嵌的是图片而不是路径
            Assert.DoesNotContain(cachePath, rtf);
        }
        finally
        {
            File.Delete(cachePath);
        }
    }

    private static ShelfItem Item(
        string id,
        ItemKind kind,
        string displayName,
        string? path = null,
        string? text = null,
        string? cachePath = null) =>
        new()
        {
            Id = id,
            Kind = kind,
            DisplayName = displayName,
            CreatedAt = DateTimeOffset.Now,
            Path = path,
            Text = text,
            CachePath = cachePath,
        };
}
