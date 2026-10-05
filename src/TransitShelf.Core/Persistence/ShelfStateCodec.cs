using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TransitShelf.Core.Settings;

namespace TransitShelf.Core.Persistence;

/// <summary>条目与磁盘格式之间的序列化。</summary>
public static class ShelfStateCodec
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true,
        // 中文直接写入，便于用户肉眼检查 state.json
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        // 枚举写小写（file / folder / text），与文档中的示例 schema 保持一致
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    public static string Serialize(IEnumerable<ShelfItem> items)
        => Serialize(items, new ShelfSettings());

    public static string Serialize(IEnumerable<ShelfItem> items, ShelfSettings settings)
    {
        var state = new ShelfStateRecord
        {
            Items = items.Select(ToRecord).ToList(),
            Settings = settings,
        };

        return JsonSerializer.Serialize(state, Options);
    }

    public static IReadOnlyList<ShelfItem> Deserialize(string json)
        => DeserializeSnapshot(json).Items;

    /// <summary>读取条目与设置。</summary>
    public static (IReadOnlyList<ShelfItem> Items, ShelfSettings Settings) DeserializeSnapshot(string json)
    {
        var state = JsonSerializer.Deserialize<ShelfStateRecord>(json, Options);
        if (state?.Items is null)
        {
            return ([], new ShelfSettings());
        }

        var items = state.Items
            .Where(record => !string.IsNullOrWhiteSpace(record.Id))
            .Select(ToItem)
            .ToList();

        return (items, state.Settings ?? new ShelfSettings());
    }

    private static ShelfItemRecord ToRecord(ShelfItem item) => new()
    {
        Id = item.Id,
        Kind = item.Kind,
        DisplayName = item.DisplayName,
        Path = item.Path,
        Text = item.Text,
        CachePath = item.CachePath,
        CreatedAt = item.CreatedAt,
        Pinned = item.Pinned,
    };

    private static ShelfItem ToItem(ShelfItemRecord record) => new()
    {
        Id = record.Id,
        Kind = record.Kind,
        DisplayName = string.IsNullOrWhiteSpace(record.DisplayName)
            ? record.Path ?? record.Text ?? "（未命名）"
            : record.DisplayName,
        Path = record.Path,
        Text = record.Text,
        CachePath = record.CachePath,
        CreatedAt = record.CreatedAt,
        Pinned = record.Pinned,
    };
}
