namespace TransitShelf.Core.Persistence;

using TransitShelf.Core.Settings;

/// <summary>state.json 的根对象。磁盘格式与领域模型解耦，便于后续迁移。</summary>
public sealed class ShelfStateRecord
{
    public int SchemaVersion { get; set; } = 2;

    public List<ShelfItemRecord> Items { get; set; } = [];

    public ShelfSettings Settings { get; set; } = new();
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
}
