using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Serialization;
using TransitShelf.Core.Settings;

namespace TransitShelf.Core.Persistence;

/// <summary>条目、分组与磁盘格式之间的序列化。</summary>
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
        => SerializeSnapshot(items, [], settings);

    public static string SerializeSnapshot(
        IEnumerable<ShelfItem> items,
        IEnumerable<ShelfGroup> groups,
        ShelfSettings settings)
    {
        var state = new ShelfStateRecord
        {
            Items = items.Select(ToRecord).ToList(),
            Groups = groups.Select(ToGroupRecord).ToList(),
            Settings = settings,
        };

        return JsonSerializer.Serialize(state, Options);
    }

    public static IReadOnlyList<ShelfItem> Deserialize(string json)
        => DeserializeSnapshot(json).Items;

    /// <summary>
    /// 读取条目、分组与设置。返回三元组，与 <see cref="SerializeSnapshot"/> 对称。
    /// </summary>
    public static (IReadOnlyList<ShelfItem> Items, IReadOnlyList<ShelfGroup> Groups, ShelfSettings Settings)
        DeserializeSnapshot(string json)
    {
        var state = JsonSerializer.Deserialize<ShelfStateRecord>(json, Options);
        if (state?.Items is null)
        {
            return ([], [], new ShelfSettings());
        }

        var items = state.Items
            .Where(record => !string.IsNullOrWhiteSpace(record.Id))
            .Select(ToItem)
            .ToList();

        // v2 的state.json 没有 groups 字段，GetOrDefault 后 Groups 是空列表——旧文件照常能读。
        var groups = (state.Groups ?? [])
            .Where(record => !string.IsNullOrWhiteSpace(record.Id))
            .Select(ToGroup)
            .ToList();

        return (items, groups, state.Settings ?? new ShelfSettings());
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
        GroupId = item.GroupId,
    };

    private static ShelfGroupRecord ToGroupRecord(ShelfGroup group) => new()
    {
        Id = group.Id,
        Name = group.Name,
        CreatedAt = group.CreatedAt,
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
        GroupId = record.GroupId,
    };

    private static ShelfGroup ToGroup(ShelfGroupRecord record) => new()
    {
        Id = record.Id,
        // 落盘分组名为空时不进界面：回退到「未命名分组」，而不是显示一条空标签。
        Name = string.IsNullOrWhiteSpace(record.Name) ? "未命名分组" : record.Name,
        CreatedAt = record.CreatedAt,
    };
}