using System.Collections.ObjectModel;

namespace TransitShelf.Core;

/// <summary>
/// 临时区的内存模型：负责收录、去重、上限淘汰与清理。
/// demo 阶段不做持久化（见技术框架设计第 4 章）。
/// </summary>
public sealed class ShelfService
{
    private readonly ObservableCollection<ShelfItem> _inner = [];

    /// <summary>
    /// 排序不变式：钉住的永远在前（各自按时间倒序）。
    /// 否则新条目不断插到最前，会把钉住项挤下去——那就不叫"钉住"了。
    /// </summary>
    private void Reorder()
    {
        var desired = _inner
            .OrderByDescending(item => item.Pinned)
            .ThenByDescending(item => item.CreatedAt)
            .ToList();

        for (var index = 0; index < desired.Count; index++)
        {
            var current = _inner.IndexOf(desired[index]);
            if (current != index)
            {
                _inner.Move(current, index);
            }
        }
    }

    public ShelfService()
    {
        Items = new ReadOnlyObservableCollection<ShelfItem>(_inner);
    }

    private int _capacity = 9;

    /// <summary>临时区条目上限，超出后淘汰最旧的未钉住项。</summary>
    public int Capacity
    {
        get => _capacity;
        set
        {
            _capacity = Math.Max(1, value);
            Trim();
            Changed?.Invoke();
        }
    }

    /// <summary>只读视图；内部变更会通过 ObservableCollection 通知界面。</summary>
    public ReadOnlyObservableCollection<ShelfItem> Items { get; }

    public event Action? Changed;

    public ShelfItem AddFile(string path, string? groupId = null)
    {
        var full = System.IO.Path.GetFullPath(path);
        // 去重**在同一分组内**进行：同一个文件放临时区和工作区分组里是两种不同的用法，
        // 不该因为路径相同就把其中一份"复活"，那会让用户以为自己只存了一份。
        var existing = _inner.FirstOrDefault(i => i.Path is not null
            && i.GroupId == groupId
            && string.Equals(i.Path, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return Revive(existing);
        }

        var isFolder = Directory.Exists(full);
        var item = new ShelfItem
        {
            Id = Guid.NewGuid().ToString("n"),
            Kind = isFolder ? ItemKind.Folder : ItemKind.File,
            DisplayName = UniqueName(System.IO.Path.GetFileName(full.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : full),
            Path = full,
            GroupId = groupId,
            CreatedAt = DateTimeOffset.Now,
        };

        _inner.Insert(0, item);
        Reorder();
        Trim();
        Changed?.Invoke();
        return item;
    }

    public ShelfItem AddText(string text, string? groupId = null)
    {
        var trimmed = text.Trim();
        var existing = _inner.FirstOrDefault(i => i.Kind == ItemKind.Text && i.Text == trimmed && i.GroupId == groupId);
        if (existing is not null)
        {
            return Revive(existing);
        }

        var item = new ShelfItem
        {
            Id = Guid.NewGuid().ToString("n"),
            Kind = ItemKind.Text,
            // 长文本截前 40 字；不同内容完全可能截出同一个名字，所以必须过 UniqueName 消歧，
            // 否则界面上会出现几个长得一模一样的条目，用户分不清谁是谁。
            DisplayName = UniqueName(trimmed.Length > 40 ? trimmed[..40] + "…" : trimmed),
            Text = trimmed,
            GroupId = groupId,
            CreatedAt = DateTimeOffset.Now,
        };

        _inner.Insert(0, item);
        Reorder();
        Trim();
        Changed?.Invoke();
        return item;
    }

    /// <summary>收录无源图片：只保存缓存 PNG 的路径，不持有位图（红线 R8）。</summary>
    public ShelfItem AddCachedImage(string cachePath, string displayName, string? groupId = null)
    {
        var existing = _inner.FirstOrDefault(i => i.CachePath is not null
            && i.GroupId == groupId
            && string.Equals(i.CachePath, cachePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return Revive(existing);
        }

        var item = new ShelfItem
        {
            Id = Guid.NewGuid().ToString("n"),
            Kind = ItemKind.File,
            DisplayName = UniqueName(displayName),
            CachePath = cachePath,
            GroupId = groupId,
            CreatedAt = DateTimeOffset.Now,
        };

        _inner.Insert(0, item);
        Reorder();
        Trim();
        Changed?.Invoke();
        return item;
    }

    /// <summary>
    /// 重复收录同一内容时的处理：把时间推到"刚刚"并按新时间重排。
    ///
    /// 早先的实现是静默返回旧条目，于是用户第二次收录同一段文字，看到的仍是第一次的时间、
    /// 位置也没变——看上去像"没收录进去"。刷新时间既符合"最近用过"的直觉，也让卡片上的
    /// 相对时间重新对得上。
    ///
    /// 只调 <see cref="Reorder"/>就够：Touch() 把它变成最新的未钉住项，Reorder 会自然把它提到
    /// 该分组的第一位；钉住项仍按"钉住的在前"这条不变式留在最上面，不会被顶到未钉住项之上。
    /// </summary>
    private ShelfItem Revive(ShelfItem item)
    {
        item.Touch();
        Reorder();
        Changed?.Invoke();
        return item;
    }

    /// <summary>
    /// 显示名消歧：与现有条目重名时追加 " (2)"、" (3)"……
    /// 同名条目在列表里无法区分，而它们的路径/内容其实不同——必须让名字可区分。
    ///
    /// 用忽略大小写的比较：Windows 上"报告.docx"与"报告.DOCX"是两个文件，
    /// 但在列表里看起来一模一样，同样属于"名字一样"。
    /// </summary>
    private string UniqueName(string baseName)
    {
        if (!NameTaken(baseName))
        {
            return baseName;
        }

        for (var suffix = 2; suffix < 1000; suffix++)
        {
            var candidate = $"{baseName} ({suffix})";
            if (!NameTaken(candidate))
            {
                return candidate;
            }
        }

        return $"{baseName} ({Guid.NewGuid():n})";
    }

    private bool NameTaken(string name) =>
        _inner.Any(i => string.Equals(i.DisplayName, name, StringComparison.OrdinalIgnoreCase));

    /// <summary>
    /// 让所有条目重算相对时间。
    ///
    /// 相对时间是只读计算属性，不会自己发通知（见 <see cref="ShelfItem.NotifyRelativeTime"/>），
    /// 界面上的"刚刚 / N 分钟前"要靠外部定时踢才会走。调用频率由界面决定，条目量很小，开销可忽略。
    /// </summary>
    public void RefreshTimeLabels()
    {
        foreach (var item in _inner)
        {
            item.NotifyRelativeTime();
        }
    }

    /// <summary>切换钉住状态。</summary>
    public void TogglePin(ShelfItem item)
    {
        item.Pinned = !item.Pinned;
        Reorder();
        Trim();
        Changed?.Invoke();
    }

    /// <summary>
    /// 清空未钉住条目（FR-43），返回移除数量与保留的钉住数量。
    ///
    /// <paramref name="groupId"/> 是作用域：**传 null 表示只清临时区**，这样在工作区点「清空」
    /// 不会把临时区也一起清掉（反之亦然）。调用方应传当前正在看的那一区。
    /// </summary>
    public (int Removed, int Kept) ClearUnpinned(string? groupId = null)
    {
        var victims = _inner.Where(item => item.GroupId == groupId && !item.Pinned).ToList();
        foreach (var victim in victims)
        {
            _inner.Remove(victim);
        }

        if (victims.Count > 0)
        {
            Changed?.Invoke();
        }

        return (victims.Count, _inner.Count(i => i.GroupId == groupId && i.Pinned));
    }

    public bool Remove(ShelfItem item)
    {
        var removed = _inner.Remove(item);
        if (removed)
        {
            Changed?.Invoke();
        }

        return removed;
    }

    public void Clear()
    {
        _inner.Clear();
        Changed?.Invoke();
    }

    /// <summary>启动时从持久化状态恢复条目（新的在前）。</summary>
    public void LoadFrom(IEnumerable<ShelfItem> items)
    {
        _inner.Clear();
        foreach (var item in items.OrderByDescending(i => i.CreatedAt))
        {
            _inner.Add(item);
        }

        Trim();
        Reorder();
        Changed?.Invoke();
    }

    private void Trim()
    {
        // **只淘汰临时区条目**（GroupId == null）。工作区是长期存放（FR-60），
        // 若不排除在这里，工作区条目会被临时区的容量上限无声删掉——那是丢数据，不是清理。
        while (TemporaryCount > Capacity)
        {
            var victim = _inner.LastOrDefault(i => i.IsTemporary && !i.Pinned);
            if (victim is null)
            {
                return;
            }

            _inner.Remove(victim);
        }
    }

    /// <summary>临时区条目数（工作区条目不计入上限）。</summary>
    public int TemporaryCount => _inner.Count(i => i.IsTemporary);

    /// <summary>
    /// 把条目移到指定分组；<paramref name="groupId"/> 传 null 即移回临时区（FR-60）。
    ///
    /// 移回临时区**会**触发一次 <see cref="Trim"/>：临时区是有上限的，移回来可能立刻挤掉最旧的未钉住项。
    /// 这是"临时区"语义的一部分，不是 bug，但调用方最好先告知用户条数。
    /// </summary>
    public bool MoveToGroup(ShelfItem item, string? groupId)
    {
        if (item.GroupId == groupId)
        {
            return false;
        }

        item.GroupId = groupId;
        Trim();
        Changed?.Invoke();
        return true;
    }
}
