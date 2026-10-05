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

    /// <summary>过滤掉已过期的未钉住条目；置顶项永不过期。</summary>
    public static IReadOnlyList<ShelfItem> RemoveExpired(
        IEnumerable<ShelfItem> items,
        TimeSpan timeToLive,
        DateTimeOffset now)
    {
        return items
            .Where(item => item.Pinned || now - item.CreatedAt < timeToLive)
            .ToList();
    }
}
