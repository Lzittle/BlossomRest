namespace TransitShelf.Core;

/// <summary>
/// 把选中的条目格式化成可直接粘进 AI 会话的绝对路径清单（FR-21）。
/// 不依赖 UI，FR-61「整组复制路径清单」复用同一段逻辑。
/// </summary>
public static class PathListFormatter
{
    /// <summary>
    /// 条目对应的磁盘位置：文件与文件夹取 <see cref="ShelfItem.Path"/>，
    /// 无源图片取缓存 PNG 的 <see cref="ShelfItem.CachePath"/>；纯文字条目没有位置，返回 null。
    /// </summary>
    public static string? PathOf(ShelfItem item) =>
        !string.IsNullOrWhiteSpace(item.Path) ? item.Path
        : !string.IsNullOrWhiteSpace(item.CachePath) ? item.CachePath
        : null;

    /// <summary>
    /// 按给定顺序取路径，跳过没有磁盘位置的条目（纯文字）。
    /// 顺序即清单顺序：调用方传列表顺序（钉住项在前），用户看到的顺序就是贴出去的顺序。
    /// </summary>
    public static IReadOnlyList<string> Paths(IEnumerable<ShelfItem> items) =>
        items.Select(PathOf).OfType<string>().ToArray();

    /// <summary>换行分隔的绝对路径清单；无可用条目时返回空字符串。</summary>
    public static string Format(IEnumerable<ShelfItem> items) =>
        string.Join(Environment.NewLine, Paths(items));

    /// <summary>
    /// 按列表顺序保留所有可复制内容：有磁盘位置的条目输出路径，文字条目输出原文。
    /// 这是多选复制的纯文本视图；文件条目同时仍会以 CF_HDROP 提供给资源管理器和微信。
    /// </summary>
    public static string FormatContents(IEnumerable<ShelfItem> items) =>
        string.Join(
            Environment.NewLine,
            items.Select(item => PathOf(item) ?? item.Text).Where(value => !string.IsNullOrEmpty(value)));
}
