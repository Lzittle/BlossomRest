using System.Globalization;
using System.IO;

namespace TransitShelf.Core;

/// <summary>
/// 语义建议名（FR-51，见技术框架设计 6.15 / ADR D38）。
///
/// 只识别"名字明显是机器生成"的条目，再用现有线索给出一个人能读的名字：
/// 父文件夹名优先，父目录是泛化目录时退回扩展名类别词，最后拼上修改日期与原扩展名。
/// 纯函数：路径与时间都由调用方传入，不读文件系统、不碰 UI；无建议时返回 <c>null</c>。
///
/// **只建议，不执行**：应用时只改 <see cref="ShelfItem.DisplayName"/>，磁盘文件名仍由 FR-40 单独处理。
/// </summary>
public static class NameSuggester
{
    /// <summary>建议名与一句给用户看的理由。</summary>
    public sealed record Suggestion(string Name, string Reason);

    /// <summary>
    /// 这些目录名本身不携带用途信息，不能当作线索。
    /// 例如下载目录里的哈希文件，用「Downloads」命名等于没命名。
    /// </summary>
    private static readonly HashSet<string> GenericParentNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Downloads", "Download", "Desktop", "Temp", "Tmp", "Documents", "Files", "Uploads",
        "下载", "下载文件", "桌面", "临时", "临时文件", "文档", "文件", "上传",
        "新建文件夹", "未命名", "未命名文件夹",
    };

    /// <summary>菜单可见性判断：只检查名字本身，不需要读取修改时间。</summary>
    public static bool HasSuggestion(ItemKind kind, string? displayName) =>
        StemOf(kind, displayName) is { } stem && IsMachineGenerated(stem);

    /// <summary>
    /// 生成建议名。路径用于取父文件夹名，<paramref name="modifiedAt"/> 用于拼接日期；
    /// 文字条目、正常名称、无法生成不同名称时返回 <c>null</c>。
    /// </summary>
    public static Suggestion? Suggest(ItemKind kind, string? displayName, string? path, DateTimeOffset modifiedAt, string language = LanguagePreference.Chinese)
    {
        var name = displayName?.Trim();
        if (StemOf(kind, name) is not { } stem || !IsMachineGenerated(stem))
        {
            return null;
        }

        var extension = kind == ItemKind.File ? Path.GetExtension(name) ?? string.Empty : string.Empty;
        var parent = ParentFolderName(path);

        string baseName;
        string reason;
        if (parent is not null)
        {
            baseName = parent;
            reason = Loc.FormatFor(language, "TAF762C776B58", parent);
        }
        else if (kind == ItemKind.Folder)
        {
            baseName = Loc.TextFor(language, "T7C7802D8ADAE");
            reason = Loc.TextFor(language, "T03BB3D7C08D5");
        }
        else
        {
            baseName = CategoryOf(extension, language);
            reason = Loc.FormatFor(language, "TB765C67BF909", baseName);
        }

        var date = modifiedAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var suggested = $"{baseName} {date}{extension}";

        return string.Equals(suggested, name, StringComparison.OrdinalIgnoreCase)
            ? null
            : new Suggestion(suggested, reason);
    }

    /// <summary>取用于判定的名称主干：文件去掉扩展名，文件夹与文字按原名。</summary>
    private static string? StemOf(ItemKind kind, string? displayName)
    {
        if (kind == ItemKind.Text || string.IsNullOrWhiteSpace(displayName))
        {
            return null;
        }

        var name = displayName.Trim();
        if (kind == ItemKind.Folder)
        {
            return name;
        }

        var stem = Path.GetFileNameWithoutExtension(name);
        return string.IsNullOrWhiteSpace(stem) ? name : stem;
    }

    /// <summary>
    /// 机器生成名的判定：GUID、长度 ≥ 12 的纯十六进制、长度 ≥ 8 的纯数字。
    /// 正常中文名、带语义的西文名不会命中。
    /// </summary>
    private static bool IsMachineGenerated(string stem)
    {
        var value = stem.Trim();
        if (value.Length == 0)
        {
            return false;
        }

        if (Guid.TryParse(value, out _))
        {
            return true;
        }

        if (value.Length >= 8 && value.All(char.IsAsciiDigit))
        {
            return true;
        }

        return value.Length >= 12 && value.All(Uri.IsHexDigit);
    }

    /// <summary>父文件夹名；路径为空、位于盘符根目录或父目录是泛化目录时返回 <c>null</c>。</summary>
    private static string? ParentFolderName(string? path)
    {
        if (string.IsNullOrWhiteSpace(path))
        {
            return null;
        }

        var trimmed = path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var directory = Path.GetDirectoryName(trimmed);
        if (string.IsNullOrEmpty(directory))
        {
            return null;
        }

        var name = Path.GetFileName(directory);
        return string.IsNullOrWhiteSpace(name) || GenericParentNames.Contains(name) ? null : name;
    }

    /// <summary>扩展名类别词。未知扩展名统一归为「文件」，不猜测内容。</summary>
    private static string CategoryOf(string extension, string language) => extension.ToLowerInvariant() switch
    {
        ".png" or ".jpg" or ".jpeg" or ".gif" or ".bmp" or ".webp" or ".tif" or ".tiff"
            or ".svg" or ".ico" or ".heic" or ".heif" or ".raw" or ".psd" => Loc.TextFor(language, "TD24C10D37DB0"),
        ".doc" or ".docx" or ".pdf" or ".txt" or ".md" or ".markdown" or ".rtf"
            or ".odt" or ".pages" or ".epub" or ".mobi" or ".wps" or ".tex" => Loc.TextFor(language, "T2687CCDBB1D2"),
        ".xls" or ".xlsx" or ".xlsm" or ".csv" or ".tsv" or ".ods" or ".numbers" or ".et" => Loc.TextFor(language, "T22D9E7C54B88"),
        ".ppt" or ".pptx" or ".pptm" or ".odp" or ".key" or ".dps" => Loc.TextFor(language, "T54F982631040"),
        ".zip" or ".rar" or ".7z" or ".tar" or ".gz" or ".tgz" or ".bz2" or ".xz"
            or ".cab" or ".iso" => Loc.TextFor(language, "T8F10DC147500"),
        ".mp3" or ".wav" or ".flac" or ".aac" or ".m4a" or ".ogg" or ".wma" or ".opus"
            or ".mp4" or ".mov" or ".avi" or ".mkv" or ".wmv" or ".webm" or ".flv"
            or ".m4v" or ".mpeg" or ".mpg" or ".3gp" => Loc.TextFor(language, "TB89B44A5346D"),
        _ => Loc.TextFor(language, "T39932F24FE11"),
    };
}
