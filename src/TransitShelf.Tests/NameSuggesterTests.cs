using TransitShelf.Core;
using Xunit;

namespace TransitShelf.Tests;

/// <summary>
/// 语义建议名（FR-51）：只对机器生成的名字给出建议，线索来自父文件夹与扩展名类别，
/// 日期取调用方传入的修改时间。正常名称必须不触发，避免把用户的命名习惯当成问题。
/// </summary>
public class NameSuggesterTests
{
    private static readonly DateTimeOffset ModifiedAt = new(2026, 10, 31, 9, 30, 0, TimeSpan.FromHours(8));

    private const string Hash = "4a2f6bd6a91c4de8b71f";
    private const string GuidName = "4a2f6bd6-3c1e-4f2a-8b7d-6f2b9c1a4e83";
    private const string Numeric = "20261031123";

    [Fact]
    public void 十六进制哈希名用父文件夹名作为线索()
    {
        var fileName = $"{Hash}.pdf";
        var suggestion = NameSuggester.Suggest(
            ItemKind.File, fileName, $@"C:\Users\<user>\Documents\项目A\{fileName}", ModifiedAt);

        Assert.NotNull(suggestion);
        Assert.Equal("项目A 2026-10-31.pdf", suggestion!.Name);
        Assert.Contains("项目A", suggestion.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void GUID名用父文件夹名作为线索()
    {
        var fileName = $"{GuidName}.pdf";
        var suggestion = NameSuggester.Suggest(
            ItemKind.File, fileName, $@"C:\Users\<user>\Documents\发票\{fileName}", ModifiedAt);

        Assert.NotNull(suggestion);
        Assert.Equal("发票 2026-10-31.pdf", suggestion!.Name);
    }

    [Fact]
    public void 纯数字名在泛化父目录下回退到扩展名类别()
    {
        var fileName = $"{Numeric}.png";
        var suggestion = NameSuggester.Suggest(
            ItemKind.File, fileName, $@"C:\Users\<user>\Downloads\{fileName}", ModifiedAt);

        Assert.NotNull(suggestion);
        Assert.Equal("图片 2026-10-31.png", suggestion!.Name);
    }

    [Theory]
    [InlineData(ItemKind.File, "报告.docx")]
    [InlineData(ItemKind.File, "会议纪要-第一版.docx")]
    [InlineData(ItemKind.File, "2026 年度总结.pdf")]
    [InlineData(ItemKind.Folder, "项目资料")]
    [InlineData(ItemKind.Text, Hash)]
    public void 正常名称不触发建议(ItemKind kind, string displayName)
    {
        Assert.Null(NameSuggester.Suggest(kind, displayName, null, ModifiedAt));
        Assert.False(NameSuggester.HasSuggestion(kind, displayName));
    }

    [Theory]
    [InlineData("png", "图片")]
    [InlineData("docx", "文档")]
    [InlineData("xlsx", "表格")]
    [InlineData("pptx", "演示文稿")]
    [InlineData("zip", "压缩包")]
    [InlineData("mp4", "音视频")]
    [InlineData("bin", "文件")]
    public void 泛化父目录按扩展名类别词命名(string extension, string category)
    {
        var fileName = $"{Hash}.{extension}";
        var suggestion = NameSuggester.Suggest(
            ItemKind.File, fileName, $@"C:\Users\<user>\Downloads\{fileName}", ModifiedAt);

        Assert.NotNull(suggestion);
        Assert.Equal($"{category} 2026-10-31.{extension}", suggestion!.Name);
        Assert.Contains($"「{category}」", suggestion.Reason, StringComparison.Ordinal);
    }

    [Fact]
    public void 文件夹条目按父文件夹命名且不追加扩展名()
    {
        var suggestion = NameSuggester.Suggest(
            ItemKind.Folder, GuidName, $@"C:\Users\<user>\Documents\项目资料\{GuidName}", ModifiedAt);

        Assert.NotNull(suggestion);
        Assert.Equal("项目资料 2026-10-31", suggestion!.Name);
    }

    [Fact]
    public void 泛化父目录下的文件夹回退为文件夹类型词()
    {
        var suggestion = NameSuggester.Suggest(
            ItemKind.Folder, GuidName, $@"C:\Users\<user>\Desktop\{GuidName}", ModifiedAt);

        Assert.NotNull(suggestion);
        Assert.Equal("文件夹 2026-10-31", suggestion!.Name);
    }

    [Fact]
    public void 保留原扩展名的大小写()
    {
        var fileName = $"{Hash}.PNG";
        var suggestion = NameSuggester.Suggest(
            ItemKind.File, fileName, $@"C:\Users\<user>\Downloads\{fileName}", ModifiedAt);

        Assert.NotNull(suggestion);
        Assert.Equal("图片 2026-10-31.PNG", suggestion!.Name);
    }

    [Fact]
    public void 日期取传入的修改时间而不是当前时间()
    {
        var fileName = $"{Hash}.png";
        var modified = new DateTimeOffset(2024, 3, 5, 8, 0, 0, TimeSpan.FromHours(8));
        var suggestion = NameSuggester.Suggest(
            ItemKind.File, fileName, $@"C:\Users\<user>\Downloads\{fileName}", modified);

        Assert.NotNull(suggestion);
        Assert.Equal("图片 2024-03-05.png", suggestion!.Name);
    }

    [Theory]
    [InlineData("abc123")]
    [InlineData("1234567")]
    [InlineData("abc-deadbeef1234")]
    public void 长度不足或混入非十六进制字符的名字不触发(string stem)
    {
        Assert.Null(NameSuggester.Suggest(ItemKind.File, $"{stem}.txt", null, ModifiedAt));
        Assert.False(NameSuggester.HasSuggestion(ItemKind.File, $"{stem}.txt"));
    }
}
