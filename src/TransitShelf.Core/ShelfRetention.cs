namespace TransitShelf.Core;

/// <summary>启动时的条目校验与清理规则（见技术框架设计 5.5）。</summary>
public static class ShelfRetention
{
    /// <summary>刷新所有条目的失效状态。</summary>
    public static void RefreshStates(IEnumerable<ShelfItem> items)
    {
        foreach (var item in items)
        {
            item.RefreshState();
        }
    }

    /// <summary>只清理临时区过期的未钉住条目；工作区和钉住项长期保留。</summary>
    public static IReadOnlyList<ShelfItem> RemoveExpired(
        IEnumerable<ShelfItem> items,
        TimeSpan timeToLive,
        DateTimeOffset now)
    {
        return items
            .Where(item => !item.IsTemporary || item.Pinned || now - item.CreatedAt < timeToLive)
            .ToList();
    }
}
