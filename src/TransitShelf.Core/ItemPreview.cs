namespace TransitShelf.Core;

/// <summary>
/// 条目能否显示缩略图（FR-52）。这里只判断扩展名，真正的解码放在平台层；
/// 解码失败时界面回退到类型图标，因此集合宁可宽一点——系统缩略图能处理的格式比 WPF 多。
/// </summary>
public static class ItemPreview
{
    private static readonly HashSet<string> ImageExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".png", ".jpg", ".jpeg", ".bmp", ".gif", ".tif", ".tiff", ".ico", ".webp", ".heic", ".heif",
    };

    private static readonly HashSet<string> VideoExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".mp4", ".m4v", ".mov", ".avi", ".mkv", ".wmv", ".webm", ".mpg", ".mpeg", ".ts",
    };

    /// <summary>图片或视频才有缩略图；文字条目、文件夹与其余文件类型仍用矢量图标。</summary>
    public static bool SupportsThumbnail(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return false;
        }

        var extension = Path.GetExtension(path);
        return ImageExtensions.Contains(extension) || VideoExtensions.Contains(extension);
    }
}
