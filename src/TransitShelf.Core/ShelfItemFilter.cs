namespace TransitShelf.Core;

/// <summary>条目列表的类型筛选。</summary>
public enum ShelfFilterKind
{
    All,
    File,
    Folder,
    Image,
    Text,
}

/// <summary>
/// 当前列表的内存过滤规则（FR-44）。不建立索引，也不改变条目本身的顺序或持久化状态。
/// </summary>
public static class ShelfItemFilter
{
    public static bool Matches(ShelfItem item, string? query, ShelfFilterKind kind)
    {
        if (!MatchesKind(item, kind))
        {
            return false;
        }

        var needle = query?.Trim();
        if (string.IsNullOrEmpty(needle))
        {
            return true;
        }

        return Contains(item.DisplayName, needle)
            || Contains(item.Path, needle)
            || Contains(item.CachePath, needle)
            || Contains(item.Text, needle);
    }

    private static bool MatchesKind(ShelfItem item, ShelfFilterKind kind) => kind switch
    {
        ShelfFilterKind.All => true,
        ShelfFilterKind.File => item.Kind == ItemKind.File && !item.IsImage,
        ShelfFilterKind.Folder => item.Kind == ItemKind.Folder,
        ShelfFilterKind.Image => item.IsImage,
        ShelfFilterKind.Text => item.Kind == ItemKind.Text,
        _ => true,
    };

    private static bool Contains(string? value, string needle) =>
        value?.Contains(needle, StringComparison.OrdinalIgnoreCase) == true;
}
