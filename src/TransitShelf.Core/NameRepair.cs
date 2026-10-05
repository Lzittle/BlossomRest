using System.Text;

namespace TransitShelf.Core;

/// <summary>
/// 文件名乱码修复（见技术框架设计 6.6）。
///
/// 判据的核心不是"含多少汉字"——乱码本身往往也是合法汉字（例如"鏂囦欢"），
/// 而是**能否干净地往返解码**：真正的乱码是"UTF-8 字节被按 GBK/CP1252 读"造成的，
/// 所以把它按字节还原再按 UTF-8 解码，会得到一段完全合法、不含替换字符的文本；
/// 反过来对正常中文名做同样操作，几乎必然解出非法序列。用这个信号区分两者。
/// </summary>
public static class NameRepair
{
    private static readonly Encoding Gbk = CreateGbk();
    private static readonly Encoding WesternEuropean = CreateCodePage(1252) ?? Encoding.Latin1;
    /// <summary>ISO-8859-1，用严格编码器：无法表示的字符会让整个方向作废，而不是变成问号。</summary>
    private static readonly Encoding WesternEuropeanStrict = CreateCodePage(28591) ?? Encoding.Latin1;

    /// <summary>修复建议。</summary>
    public sealed record Suggestion(string Original, string Repaired, double Gain);

    public static Suggestion? Suggest(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || IsIrreversible(fileName))
        {
            return null;
        }

        var baseline = Score(fileName);
        string? best = null;
        var bestScore = baseline;

        foreach (var candidate in Candidates(fileName))
        {
            if (string.IsNullOrWhiteSpace(candidate.Text)
                || string.Equals(candidate.Text, fileName, StringComparison.Ordinal))
            {
                continue;
            }

            // 修复后的名字不得凭空多出问号/替换字符，否则等于毁掉文件名
            if (IntroducesNoise(fileName, candidate.Text))
            {
                continue;
            }

            var score = Score(candidate.Text) + (candidate.Clean ? 1.0 : 0.0);
            if (score > bestScore + 0.4)
            {
                bestScore = score;
                best = candidate.Text;
            }
        }

        return best is null ? null : new Suggestion(fileName, best, bestScore - baseline);
    }

    /// <summary>候选文本 + 是否为"干净解码"（无非法序列、无替换字符）。</summary>
    private readonly record struct Candidate(string Text, bool Clean);

    private static IEnumerable<Candidate> Candidates(string fileName)
    {
        // 最常见的两类：UTF-8 字节被当成 CP1252（西文）/ GBK（中文）读取
        yield return Reencode(fileName, WesternEuropean, Encoding.UTF8);
        yield return Reencode(fileName, Gbk, Encoding.UTF8);
        yield return Reencode(fileName, WesternEuropeanStrict, Encoding.UTF8);

        // 注意：**不要**加入"UTF-8 字节 → 按 GBK 解释"这个反向候选。
        // GBK 覆盖面很宽，正常中文名的 UTF-8 字节往往也能被 GBK 干净解码，
        // 于是正常文件名会被"修复"成一段乱码——实测出现过这个误判。
    }

    private static Candidate Reencode(string value, Encoding from, Encoding to)
    {
        byte[] bytes;
        try
        {
            bytes = from.GetBytes(value);
        }
        catch (EncoderFallbackException)
        {
            // 存在无法用源编码表示的字符（例如 CP1252 里没有的汉字），直接放弃该方向
            return new Candidate(string.Empty, false);
        }

        try
        {
            var strict = (Encoding)to.Clone();
            strict.DecoderFallback = DecoderFallback.ExceptionFallback;
            return new Candidate(strict.GetString(bytes), true);
        }
        catch (DecoderFallbackException)
        {
            return new Candidate(to.GetString(bytes), false);
        }
    }

    /// <summary>经典的不可逆乱码：替换字符，以及"锟斤拷"这类信息已丢失的形态。</summary>
    private static bool IsIrreversible(string value) =>
        value.Contains('\uFFFD')
        || value.Contains('\u9500') && value.Contains('\u65A4') && value.Contains('\u62F7');

    private static bool IntroducesNoise(string original, string candidate) =>
        candidate.Count(ch => ch == '?') > original.Count(ch => ch == '?')
        || candidate.Count(ch => ch == '\uFFFD') > original.Count(ch => ch == '\uFFFD');

    /// <summary>文字本身的合理性评分（不含"干净解码"加成）。</summary>
    private static double Score(string value)
    {
        if (value.Length == 0)
        {
            return double.NegativeInfinity;
        }

        var cjk = 0;
        var suspicious = 0;

        foreach (var ch in value)
        {
            if (ch >= '\u4E00' && ch <= '\u9FFF')
            {
                cjk++;
            }
            else if (ch == '\uFFFD' || (ch > '\u00FF' && ch < '\u4E00'))
            {
                suspicious++;
            }
        }

        return ((double)cjk / value.Length * 0.5) - ((double)suspicious / value.Length * 0.8);
    }

    private static Encoding CreateGbk()
        => CreateCodePage(936) ?? Encoding.UTF8;

    private static Encoding? CreateCodePage(int codePage)
    {
        try
        {
            Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
            // 编码器严格（无法表示就抛错，避免悄悄变成问号），解码器宽松（用于产出可读的退化结果）
            return Encoding.GetEncoding(codePage, EncoderFallback.ExceptionFallback, DecoderFallback.ReplacementFallback);
        }
        catch (Exception ex) when (ex is ArgumentException or NotSupportedException)
        {
            return null;
        }
    }
}
