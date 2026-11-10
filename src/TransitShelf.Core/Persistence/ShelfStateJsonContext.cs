using System.Text.Json.Serialization;

namespace TransitShelf.Core.Persistence;

/// <summary>编译期生成状态格式元数据，避免首次恢复时反射构建整个对象图。</summary>
[JsonSerializable(typeof(ShelfStateRecord))]
internal partial class ShelfStateJsonContext : JsonSerializerContext;
