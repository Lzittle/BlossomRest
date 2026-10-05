using System.Text;
using TransitShelf.Core;

namespace TransitShelf.Tests;

public class NameRepairTests
{
    private static Encoding CodePage(int codePage)
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
        return Encoding.GetEncoding(codePage);
    }

    /// <summary>把正确名字的 UTF-8 字节按指定编码读出来，制造乱码。</summary>
    private static string MakeMojibake(string original, int asCodePage) =>
        CodePage(asCodePage).GetString(Encoding.UTF8.GetBytes(original));

    /// <summary>制造乱码，并告知这次转换是否无损（GBK 存在非法/未定义序列，可能丢字符）。</summary>
    private static string MakeMojibake(string original, int asCodePage, out bool lossless)
    {
        var encoding = CodePage(asCodePage);
        encoding = (Encoding)encoding.Clone();
        encoding.DecoderFallback = DecoderFallback.ExceptionFallback;
        encoding.EncoderFallback = EncoderFallback.ExceptionFallback;

        try
        {
            lossless = true;
            return encoding.GetString(Encoding.UTF8.GetBytes(original));
        }
        catch (DecoderFallbackException)
        {
            lossless = false;
            return CodePage(asCodePage).GetString(Encoding.UTF8.GetBytes(original));
        }
    }

    [Theory]
    [InlineData("文件.docx")]
    [InlineData("会议纪要-第一版.docx")]
    [InlineData("项目计划书.pptx")]
    public void 能修复被按GBK误读的UTF8文件名(string original)
    {
        var broken = MakeMojibake(original, 936, out var lossless);

        var suggestion = NameRepair.Suggest(broken);

        // GBK 存在非法序列：转换本身丢字符时，只要求不要给出错误的建议名
        if (!lossless)
        {
            Assert.True(suggestion is null || !suggestion.Repaired.Contains('?'));
            return;
        }

        Assert.NotNull(suggestion);
        Assert.Equal(original, suggestion!.Repaired);
    }

    [Theory]
    [InlineData("文件.docx")]
    [InlineData("设计说明.txt")]
    public void 能修复被按CP1252误读的UTF8文件名(string original)
    {
        var broken = MakeMojibake(original, 1252);

        var suggestion = NameRepair.Suggest(broken);

        Assert.NotNull(suggestion);
        Assert.Equal(original, suggestion!.Repaired);
    }

    [Theory]
    [InlineData("正常文件名.docx")]
    [InlineData("README.md")]
    [InlineData("2026-09-27 会议纪要.txt")]
    [InlineData("4a2f6bd6-2b45-44ed-8491-ce4ec269ea63_full.mp4")]
    public void 正常文件名不会被乱改(string name)
    {
        Assert.Null(NameRepair.Suggest(name));
    }

    [Fact]
    public void 不可逆乱码不给出建议()
    {
        var damaged = "锟斤拷" + "\uFFFD" + ".docx";

        Assert.Null(NameRepair.Suggest(damaged));
    }
}
