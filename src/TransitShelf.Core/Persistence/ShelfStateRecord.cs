namespace TransitShelf.Core.Persistence;

using TransitShelf.Core.Settings;

/// <summary>state.json 的根对象。磁盘格式与领域模型解耦，便于后续迁移。</summary>
public sealed class ShelfStateRecord
{
    /// <summary>v3 起新增 <see cref="Groups"/>（FR-60）。旧文件没有该字段，反序列化得到空列表。</summary>
    public int SchemaVersion { get; set; } = 3;

    public List<ShelfItemRecord> Items { get; set; } = [];

    public List<ShelfGroupRecord> Groups { get; set; } = [];

    public ShelfSettings Settings { get; set; } = new();
}

public sealed class ShelfGroupRecord
{
    public string Id { get; set; } = string.Empty;

    public string Name { get; set; } = string.Empty;

    public DateTimeOffset CreatedAt { get; set; }
}

public sealed class ShelfItemRecord
{
    public string Id { get; set; } = string.Empty;

    public ItemKind Kind { get; set; }

    public string DisplayName { get; set; } = string.Empty;

    public string? Path { get; set; }

    public string? Text { get; set; }

    public string? CachePath { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public bool Pinned { get; set; }

    /// <summary>所属分组（FR-60）；null / 缺失即临时区。旧文件没有这个字段，反序列化为 null。</summary>
    public string? GroupId { get; set; }
}
