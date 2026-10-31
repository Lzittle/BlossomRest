using System.IO;
using System.Text;
using System.Windows.Media.Imaging;
using TransitShelf.Core;

namespace TransitShelf.Platform;

/// <summary>
/// 图文混合复制时生成 RTF（FR-21）：文字保留原文，图片内嵌为 PNG。
/// Word / WPS / 邮件客户端支持 RTF 剪贴板，可以一次粘出图文；
/// 聊天类应用仍只取 CF_HDROP 或纯文本，需要配合「复制文字」分两次粘贴。
/// </summary>
public static class RichTextClipboardBuilder
{
    // 富文本会内嵌图片，剪贴板体积必须设上限：超过后该图退化为路径文本。
    private const long ImageByteBudget = 32 * 1024 * 1024;

    public static bool HasImage(IEnumerable<ShelfItem> items) => items.Any(IsImageItem);

    public static bool HasText(IEnumerable<ShelfItem> items) =>
        items.Any(item => item.Kind == ItemKind.Text && !string.IsNullOrWhiteSpace(item.Text));

    /// <summary>按列表顺序生成 RTF；无可用内容时返回空字符串。</summary>
    public static string Build(IReadOnlyList<ShelfItem> items)
    {
        var rtf = new StringBuilder();
        rtf.Append(@"{\rtf1\ansi\ansicpg936\deff0{\fonttbl{\f0\fnil\fcharset134 Microsoft YaHei;}}");
        rtf.Append(@"\viewkind4\uc1\pard\f0\fs20 ");

        var budget = ImageByteBudget;
        foreach (var item in items)
        {
            if (item.Kind == ItemKind.Text)
            {
                AppendText(rtf, item.Text ?? string.Empty);
                rtf.Append(@"\par").Append('\n');
                continue;
            }

            var location = PathListFormatter.PathOf(item);
            if (IsImageItem(item) && TryAppendImage(rtf, location, ref budget))
            {
                rtf.Append(@"\par").Append('\n');
                continue;
            }

            // 非图片、解码失败或超过预算：退化为路径文本，至少不丢内容。
            if (!string.IsNullOrWhiteSpace(location))
            {
                AppendText(rtf, location);
                rtf.Append(@"\par").Append('\n');
            }
        }

        rtf.Append('}');
        return rtf.ToString();
    }

    private static bool IsImageItem(ShelfItem item) =>
        item.CachePath is not null || ItemPreview.IsImage(item.Path);

    private static bool TryAppendImage(StringBuilder rtf, string? path, ref long budget)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
        {
            return false;
        }

        try
        {
            var frame = DecodeFirstFrame(path);
            var width = frame.PixelWidth;
            var height = frame.PixelHeight;

            var png = File.ReadAllBytes(path);
            if (!Path.GetExtension(path).Equals(".png", StringComparison.OrdinalIgnoreCase))
            {
                using var stream = new MemoryStream();
                var encoder = new PngBitmapEncoder();
                encoder.Frames.Add(BitmapFrame.Create(frame));
                encoder.Save(stream);
                png = stream.ToArray();
            }

            if (png.Length > budget)
            {
                return false;
            }

            budget -= png.Length;

            rtf.Append(@"{\pict\pngblip\picw").Append(width)
                .Append(@"\pich").Append(height)
                .Append(@"\picwgoal").Append(width * 15)
                .Append(@"\pichgoal").Append(height * 15)
                .Append(' ');
            rtf.Append(Convert.ToHexString(png).ToLowerInvariant());
            rtf.Append('}');
            return true;
        }
        catch (Exception)
        {
            // 格式解不了（如 HEIC/WebP 缺系统解码器）时退回路径文本，不让复制失败。
            return false;
        }
    }

    private static BitmapFrame DecodeFirstFrame(string path)
    {
        using var stream = File.OpenRead(path);
        var decoder = BitmapDecoder.Create(
            stream,
            BitmapCreateOptions.PreservePixelFormat,
            BitmapCacheOption.OnLoad);
        return decoder.Frames[0];
    }

    private static void AppendText(StringBuilder rtf, string text)
    {
        foreach (var ch in text)
        {
            switch (ch)
            {
                case '\\':
                    rtf.Append(@"\\");
                    break;
                case '{':
                    rtf.Append(@"\{");
                    break;
                case '}':
                    rtf.Append(@"\}");
                    break;
                case '\r':
                    break;
                case '\n':
                    rtf.Append(@"\par").Append('\n');
                    break;
                default:
                    if (ch < 128)
                    {
                        rtf.Append(ch);
                    }
                    else
                    {
                        // RTF 的 \uN 用有符号 16 位；中文等非 ASCII 一律转义，
                        // 这样整份 RTF 保持纯 ASCII，写剪贴板时不会有编码歧义。
                        rtf.Append(@"\u").Append(ch > 32767 ? ch - 65536 : (int)ch).Append('?');
                    }

                    break;
            }
        }
    }
}
