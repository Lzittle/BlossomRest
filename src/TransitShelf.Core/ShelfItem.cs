using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TransitShelf.Core;

/// <summary>
/// 中转站条目。文件与文件夹只保存路径引用（引用式暂存，零拷贝），不复制内容。
/// </summary>
public sealed class ShelfItem : INotifyPropertyChanged
{
    private string _displayName = string.Empty;
    private bool _pinned;
    private ItemState _state = ItemState.Ok;
    private string? _path;
    private DateTimeOffset _createdAt;
    private string? _groupId;

    public required string Id { get; init; }

    public required ItemKind Kind { get; init; }

    /// <summary>显示名，可被用户重命名。</summary>
    public required string DisplayName
    {
        get => _displayName;
        set
        {
            if (_displayName == value)
            {
                return;
            }

            _displayName = value;
            OnPropertyChanged();
        }
    }

    /// <summary>文件 / 文件夹的绝对路径；文字条目为 null。</summary>
    public string? Path
    {
        get => _path;
        set
        {
            if (_path == value)
            {
                return;
            }

            _path = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(ThumbnailPath));
            OnPropertyChanged(nameof(HasThumbnail));
        }
    }

    /// <summary>文字条目的内容；文件类条目为 null。</summary>
    public string? Text { get; init; }

    /// <summary>
    /// 收录时刻。**不是 init**：重复收录同一内容时要把它刷新成"刚刚"（见 ShelfService.AddText），
    /// 所以带 setter；但仍 required——漏给时间会退化成 0001 年，界面直接显示几万天前。
    /// </summary>
    public required DateTimeOffset CreatedAt
    {
        get => _createdAt;
        set
        {
            if (_createdAt == value)
            {
                return;
            }

            _createdAt = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(RelativeTime));
        }
    }

    /// <summary>重新收录：把时间推到当前，并让界面上的相对时间立刻重算。</summary>
    public void Touch() => CreatedAt = DateTimeOffset.Now;

    /// <summary>钉住后不参与上限淘汰与过期清理。</summary>
    public bool Pinned
    {
        get => _pinned;
        set
        {
            if (_pinned == value)
            {
                return;
            }

            _pinned = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(PinLabel));
            OnPropertyChanged(nameof(PinActionLabel));
        }
    }

    /// <summary>
    /// 所属分组（FR-60）。<c>null</c> 表示在临时区。
    ///
    /// **带 setter**：条目要能跨组移动（PRD FR-60「条目可跨组移动」），移动是常见的整理动作。
    /// </summary>
    public string? GroupId
    {
        get => _groupId;
        set
        {
            if (_groupId == value)
            {
                return;
            }

            _groupId = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsTemporary));
        }
    }

    /// <summary>是否在临时区（决定它是否受容量上限与过期清理约束）。</summary>
    public bool IsTemporary => GroupId is null;

    /// <summary>无源图片的缓存 PNG 路径；其余类型为 null。</summary>
    public string? CachePath { get; init; }

    /// <summary>缩略图读取的路径：无源图片取缓存 PNG，其余取源文件（FR-52）。</summary>
    public string? ThumbnailPath => CachePath ?? Path;

    /// <summary>是否可能显示缩略图（图片与视频）。实际解码失败时界面回退到类型图标。</summary>
    public bool HasThumbnail => ItemPreview.SupportsThumbnail(ThumbnailPath);

    /// <summary>源文件是否仍然存在（见 <see cref="RefreshState"/>）。</summary>
    public ItemState State
    {
        get => _state;
        set
        {
            if (_state == value)
            {
                return;
            }

            _state = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(IsMissing));
            OnPropertyChanged(nameof(StatusLabel));
        }
    }

    public bool IsMissing => State == ItemState.Missing;

    public string StatusLabel => IsMissing ? " · 已失效" : string.Empty;

    /// <summary>重新检查源文件是否还存在；失效的条目标记但不删除（FR-45）。</summary>
    public void RefreshState()
    {
        var target = Path ?? CachePath;
        if (target is null)
        {
            State = ItemState.Ok;
            return;
        }

        State = File.Exists(target) || Directory.Exists(target) ? ItemState.Ok : ItemState.Missing;
    }

    /// <summary>钉住标记（用于条目卡片上的提示）。</summary>
    public string PinLabel => Pinned ? " · 已钉住" : string.Empty;

    /// <summary>右键菜单里的动作名，随钉住状态变化。</summary>
    public string PinActionLabel => Pinned ? "取消钉住" : "钉住";

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));

    public string KindLabel => Kind switch
    {
        ItemKind.Folder => "文件夹",
        ItemKind.Text => "文字",
        _ => IsImage ? "图片" : "文件",
    };

    /// <summary>
    /// 无源图片（剪贴板位图落成的缓存 PNG，见 FR-12 / R8）。它和普通文件同为 <see cref="ItemKind.File"/>，
    /// 靠"有缓存路径"区分，界面据此换用图片图标、标签也显示"图片"。
    /// 这是领域事实而不是界面细节，所以放在 Core；具体画成什么图标由 App 决定。
    /// </summary>
    public bool IsImage => CachePath is not null;

    public string RelativeTime
    {
        get
        {
            var span = DateTimeOffset.Now - CreatedAt;
            if (span.TotalSeconds < 60) return "刚刚";
            if (span.TotalMinutes < 60) return $"{(int)span.TotalMinutes} 分钟前";
            if (span.TotalHours < 24) return $"{(int)span.TotalHours} 小时前";
            return $"{(int)span.TotalDays} 天前";
        }
    }

    /// <summary>
    /// 通知界面重算 <see cref="RelativeTime"/>。
    ///
    /// 这个值是**只读计算属性**：内容随 <see cref="DateTimeOffset.Now"/> 变，但没有任何属性本身的变化，
    /// 所以 WPF 的 OneWay 绑定只在第一次取值时就定住了，之后永远显示同一个词——
    /// 表现为"放了一小时还是刚刚"。必须由外部（ShelfService 的定时刷新）显式踢一脚。
    /// </summary>
    public void NotifyRelativeTime() => OnPropertyChanged(nameof(RelativeTime));
}
