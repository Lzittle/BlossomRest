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

    public ShelfItem AddFile(string path)
    {
        var full = System.IO.Path.GetFullPath(path);
        var existing = _inner.FirstOrDefault(i => i.Path is not null
            && string.Equals(i.Path, full, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var isFolder = Directory.Exists(full);
        var item = new ShelfItem
        {
            Id = Guid.NewGuid().ToString("n"),
            Kind = isFolder ? ItemKind.Folder : ItemKind.File,
            DisplayName = System.IO.Path.GetFileName(full.TrimEnd('\\', '/')) is { Length: > 0 } name ? name : full,
            Path = full,
            CreatedAt = DateTimeOffset.Now,
        };

        _inner.Insert(0, item);
        Reorder();
        Trim();
        Changed?.Invoke();
        return item;
    }

    public ShelfItem AddText(string text)
    {
        var trimmed = text.Trim();
        var existing = _inner.FirstOrDefault(i => i.Kind == ItemKind.Text && i.Text == trimmed);
        if (existing is not null)
        {
            return existing;
        }

        var item = new ShelfItem
        {
            Id = Guid.NewGuid().ToString("n"),
            Kind = ItemKind.Text,
            DisplayName = trimmed.Length > 40 ? trimmed[..40] + "…" : trimmed,
            Text = trimmed,
            CreatedAt = DateTimeOffset.Now,
        };

        _inner.Insert(0, item);
        Reorder();
        Trim();
        Changed?.Invoke();
        return item;
    }

    /// <summary>收录无源图片：只保存缓存 PNG 的路径，不持有位图（红线 R8）。</summary>
    public ShelfItem AddCachedImage(string cachePath, string displayName)
    {
        var existing = _inner.FirstOrDefault(i => i.CachePath is not null
            && string.Equals(i.CachePath, cachePath, StringComparison.OrdinalIgnoreCase));
        if (existing is not null)
        {
            return existing;
        }

        var item = new ShelfItem
        {
            Id = Guid.NewGuid().ToString("n"),
            Kind = ItemKind.File,
            DisplayName = displayName,
            CachePath = cachePath,
            CreatedAt = DateTimeOffset.Now,
        };

        _inner.Insert(0, item);
        Reorder();
        Trim();
        Changed?.Invoke();
        return item;
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
    /// 清空临时区，但**保留钉住项**（FR-43）。
    /// 返回移除数量与保留的钉住数量。
    /// </summary>
    public (int Removed, int Kept) ClearUnpinned()
    {
        var victims = _inner.Where(item => !item.Pinned).ToList();
        foreach (var victim in victims)
        {
            _inner.Remove(victim);
        }

        if (victims.Count > 0)
        {
            Changed?.Invoke();
        }

        return (victims.Count, _inner.Count);
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
        while (_inner.Count > Capacity)
        {
            var victim = _inner.LastOrDefault(i => !i.Pinned);
            if (victim is null)
            {
                return;
            }

            _inner.Remove(victim);
        }
    }
}
