using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace TransitShelf.Core;

/// <summary>
/// 工作区分组（FR-60）。分组是**长期存放**的容器：条目进了工作区就不受临时区的
/// 容量上限与过期时长约束（那是"临时"的定义，见 PRD FR-41/ FR-43）。
///
/// 临时区不建模成分组——它是"没有分组的条目"（<see cref="ShelfItem.GroupId"/> 为 null），
/// 这样新增条目落哪儿只由一个字段决定，不必先建一个叫"临时区"的壳分组。
/// </summary>
public sealed class ShelfGroup : INotifyPropertyChanged
{
    private string _name = string.Empty;

    public required string Id { get; init; }

    /// <summary>分组名，可被用户重命名。空名会在 <see cref="WorkspaceService"/> 里被拒。</summary>
    public required string Name
    {
        get => _name;
        set
        {
            if (_name == value)
            {
                return;
            }

            _name = value;
            OnPropertyChanged();
        }
    }

    public required DateTimeOffset CreatedAt { get; init; }

    public event PropertyChangedEventHandler? PropertyChanged;

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null) =>
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
}