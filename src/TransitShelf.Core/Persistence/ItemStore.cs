using System.Text;
using System.Text.Json;
using TransitShelf.Core.Settings;

namespace TransitShelf.Core.Persistence;

/// <summary>
/// state.json 的读写。写入采用"先写临时文件再替换"，避免进程被强杀时留下半个文件。
/// 读取失败（文件损坏 / 无权限）时返回空集合，绝不因此阻止程序启动。
/// </summary>
public sealed class ItemStore
{
    private readonly string _filePath;

    public ItemStore(string filePath)
    {
        _filePath = filePath;
    }

    public string FilePath => _filePath;

    public IReadOnlyList<ShelfItem> Load()
        => LoadSnapshot().Items;

    /// <summary>读取条目、分组与设置；文件缺失或损坏时返回空集合与默认设置。</summary>
    public (IReadOnlyList<ShelfItem> Items, IReadOnlyList<ShelfGroup> Groups, ShelfSettings Settings) LoadSnapshot()
    {
        try
        {
            if (!File.Exists(_filePath))
            {
                return ([], [], new ShelfSettings());
            }

            return ShelfStateCodec.DeserializeSnapshot(File.ReadAllText(_filePath, Encoding.UTF8));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException)
        {
            return ([], [], new ShelfSettings());
        }
    }

    public void Save(IEnumerable<ShelfItem> items) => Save(items, [], new ShelfSettings());

    public void Save(IEnumerable<ShelfItem> items, ShelfSettings settings)
        => Save(items, [], settings);

    public void Save(IEnumerable<ShelfItem> items, IEnumerable<ShelfGroup> groups, ShelfSettings settings)
    {
        var directory = Path.GetDirectoryName(_filePath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = _filePath + ".tmp";
        File.WriteAllText(
            temporaryPath,
            ShelfStateCodec.SerializeSnapshot(items, groups, settings),
            Encoding.UTF8);
        File.Move(temporaryPath, _filePath, overwrite: true);
    }
}