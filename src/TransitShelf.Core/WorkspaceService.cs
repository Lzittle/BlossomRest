using System.Collections.ObjectModel;

namespace TransitShelf.Core;

/// <summary>
/// 工作区分组的增删改查（FR-60）。条目本体归 <see cref="ShelfService"/> 管，这里只管分组本身。
///
/// 拆成两个服务而不是合成一个：条目有"上限淘汰"这套临时区专属规则，分组没有，
/// 混在一起会让 <see cref="ShelfService.Trim"/> 之类的临时区逻辑被分组改动波及。
/// </summary>
public sealed class WorkspaceService
{
    private readonly ObservableCollection<ShelfGroup> _inner = [];

    public WorkspaceService()
    {
        Groups = new ReadOnlyObservableCollection<ShelfGroup>(_inner);
    }

    public ReadOnlyObservableCollection<ShelfGroup> Groups { get; }

    public event Action? Changed;

    /// <summary>分组数量上限。防止"建了一堆分组"的脏数据把列表撑爆；正常用法远达不到。</summary>
    public const int MaxGroups = 50;

    /// <summary>
    /// 新建分组。名称为空或全是空白时返回 null——不抛异常，因为调用方是"用户敲回车"，
    /// 静默忽略一次无效输入比弹错误框自然。
    /// </summary>
    public ShelfGroup? Create(string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || _inner.Count >= MaxGroups)
        {
            return null;
        }

        var group = new ShelfGroup
        {
            Id = Guid.NewGuid().ToString("n"),
            Name = UniqueName(trimmed),
            CreatedAt = DateTimeOffset.Now,
        };

        _inner.Add(group);
        Changed?.Invoke();
        return group;
    }

    /// <summary>重命名。空名返回 false（保持原名，不改成空串）。重名会自动消歧。</summary>
    public bool Rename(ShelfGroup group, string name)
    {
        var trimmed = name.Trim();
        if (trimmed.Length == 0 || string.Equals(group.Name, trimmed, StringComparison.Ordinal))
        {
            return false;
        }

        group.Name = UniqueName(trimmed, group.Id);
        Changed?.Invoke();
        return true;
    }

    /// <summary>
    /// 删除分组。<paramref name="items"/> 是全部条目；<paramref name="onOrphan"/> 决定组内条目去哪。
    ///
    /// 删除分组**不删条目**（红线R4 的精神：用户的东西不该因为一次整理动作消失）。
    /// 两条出路由调用方选：
    /// <list type="bullet">
    /// <item>传 null —— 组内条目移回临时区并钉住，避免整理分组时丢失条目；</item>
    /// <item>传一个回调自行处理（例如弹窗问用户"移到哪个分组"）。</item>
    /// </list>
    /// 返回受影响的条目数，便于告知用户。
    /// </summary>
    public int Delete(ShelfGroup group, IEnumerable<ShelfItem> items, Action<ShelfItem>? onOrphan = null)
    {
        if (!_inner.Contains(group))
        {
            return 0;
        }

        var members = items.Where(i => string.Equals(i.GroupId, group.Id, StringComparison.Ordinal)).ToList();
        foreach (var member in members)
        {
            if (onOrphan is null)
            {
                member.Pinned = true;
                member.GroupId = null;
            }
            else
            {
                onOrphan(member);
            }
        }

        _inner.Remove(group);
        Changed?.Invoke();
        return members.Count;
    }

    public ShelfGroup? Find(string? id) =>
        id is null ? null : _inner.FirstOrDefault(g => string.Equals(g.Id, id, StringComparison.Ordinal));

    /// <summary>启动时恢复分组。丢弃 Id 为空的记录，避免脏数据在界面上变成空白行。</summary>
    public void LoadFrom(IEnumerable<ShelfGroup> groups)
    {
        _inner.Clear();
        foreach (var group in groups.Where(g => !string.IsNullOrWhiteSpace(g.Id)))
        {
            _inner.Add(group);
        }

        Changed?.Invoke();
    }

    /// <summary>
    /// 分组名消歧：重名时追加 " (2)"。与条目显示名同一套做法——
    /// 两个同名分组在标签栏里是无法区分的。
    /// </summary>
    private string UniqueName(string baseName, string? selfId = null) =>
        !NameTaken(baseName, selfId)
            ? baseName
            : Enumerable.Range(2, 998)
                .Select(suffix => $"{baseName} ({suffix})")
                .FirstOrDefault(candidate => !NameTaken(candidate, selfId))
                ?? $"{baseName} ({Guid.NewGuid():n})";

    /// <summary>
    /// 重名比较忽略大小写，但**排除自己**——否则用户把分组名改成它原本的名字
    /// 也会被判成"重名"，凭空多出" (2)"，非常莫名其妙。
    /// </summary>
    private bool NameTaken(string name, string? selfId) =>
        _inner.Any(g => !string.Equals(g.Id, selfId, StringComparison.Ordinal)
            && string.Equals(g.Name, name, StringComparison.OrdinalIgnoreCase));
}
