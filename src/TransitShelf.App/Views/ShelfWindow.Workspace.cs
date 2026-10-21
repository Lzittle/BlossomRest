using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using TransitShelf.Core;
using TransitShelf.Platform.Interop;

namespace TransitShelf.App.Views;

/// <summary>工作区交互（FR-01 / FR-60 / FR-61 / FR-75），与浮窗和投递代码分开维护。</summary>
public partial class ShelfWindow
{
    private readonly WorkspaceService _workspace = new();
    private readonly ICollectionView _itemView;
    private bool _workspaceActive;
    private string? _selectedGroupId;

    private string? CurrentGroupId => _workspaceActive ? _selectedGroupId : null;
    private IEnumerable<ShelfItem> CurrentItems => _shelf.Items.Where(IsCurrentItem);
    private bool IsCurrentItem(ShelfItem item) =>
        (!_workspaceActive || _selectedGroupId is not null) && item.GroupId == CurrentGroupId;

    private void OnWorkspaceChanged()
    {
        RefreshWorkspace();
        ScheduleSave();
    }

    private void RefreshWorkspace()
    {
        if (_workspace.Find(_selectedGroupId) is null)
        {
            _selectedGroupId = _workspace.Groups.FirstOrDefault()?.Id;
        }

        _itemView.Refresh();
        WorkspaceTabs.Visibility = _settings.EnableWorkspace ? Visibility.Visible : Visibility.Collapsed;
        WorkspaceBar.Visibility = _workspaceActive ? Visibility.Visible : Visibility.Collapsed;
        MarkTab(TemporaryTab, !_workspaceActive);
        MarkTab(WorkspaceTab, _workspaceActive);
        var group = _workspace.Find(_selectedGroupId);
        PanelTitle.Text = _workspaceActive ? "工作区" : "临时区";
        PanelContext.ToolTip = _workspaceActive
            ? $"当前查看：工作区 · {group?.Name ?? "未选择分组"}"
            : "当前查看：临时区";
        GroupNameText.Text = group?.Name ?? "选择分组";
        GroupPicker.ToolTip = group?.Name ?? "先新建一个分组";
        GroupPicker.IsEnabled = _workspace.Groups.Count > 0;
        GroupManageButton.IsEnabled = group is not null;
        EmptyHint.Text = _workspaceActive
            ? group is null ? "把常用资料放在工作区，长期保留。\n先新建一个分组。"
                : "这个分组还是空的。\n拖入文件或收录剪贴板，条目将长期保留。"
            : "把文件、文件夹或文字拖进来。\n单击：文件夹打开 / 其余复制；双击打开；按住条目可拖到其他应用。";
        EmptyCreateGroupButton.Visibility = _workspaceActive && group is null
            ? Visibility.Visible : Visibility.Collapsed;
        RefreshFooter();
        UpdateBatchBar();
    }

    private static void MarkTab(Button button, bool selected)
    {
        button.FontWeight = selected ? FontWeights.SemiBold : FontWeights.Normal;
        button.SetResourceReference(Control.ForegroundProperty, selected ? "AccentBrush" : "SecondaryTextBrush");
        button.SetValue(System.Windows.Automation.AutomationProperties.HelpTextProperty,
            selected ? "当前区域" : "切换区域");
    }

    private void ResetScopeActions()
    {
        ExitMultiSelectSilently();
        _clearTimer.Stop();
        _clearArmed = false;
        _clearArmedForWorkspace = false;
        _clearArmedGroupId = null;
        UpdateClearButton();
        _dragCandidate = null;
        _dragOrigin = null;
    }

    private void SelectScope(bool workspace, string? groupId = null)
    {
        ResetScopeActions();
        _workspaceActive = workspace && _settings.EnableWorkspace;
        if (groupId is not null) _selectedGroupId = groupId;
        RefreshWorkspace();
        ApplyLayout();
    }

    private void OnTemporaryTabClick(object sender, RoutedEventArgs e) => SelectScope(false);
    private void OnWorkspaceTabClick(object sender, RoutedEventArgs e) => SelectScope(true);

    private bool CanCollectInCurrentScope()
    {
        if (!_workspaceActive || _workspace.Find(CurrentGroupId) is not null) return true;
        Status("请先新建一个工作区分组");
        return false;
    }

    private static void ShowMenu(ContextMenu menu, FrameworkElement target)
    {
        menu.PlacementTarget = target;
        menu.Placement = PlacementMode.Bottom;
        menu.IsOpen = true;
    }

    private void OnGroupPickerClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var group in _workspace.Groups)
        {
            var choice = new MenuItem { Header = group.Name, IsCheckable = true, IsChecked = group.Id == _selectedGroupId };
            choice.Click += (_, _) => SelectScope(true, group.Id);
            menu.Items.Add(choice);
        }
        ShowMenu(menu, GroupPicker);
    }

    private void OnCreateGroupClick(object sender, RoutedEventArgs e) => CreateGroup();

    private ShelfGroup? CreateGroup()
    {
        if (_workspace.Groups.Count >= WorkspaceService.MaxGroups)
        {
            Status($"最多创建 {WorkspaceService.MaxGroups} 个分组");
            return null;
        }
        var name = AskGroupName("新建分组", string.Empty);
        if (name is null) return null;
        var group = _workspace.Create(name);
        if (group is not null)
        {
            SelectScope(true, group.Id);
            Status($"已创建分组：{group.Name}");
        }
        return group;
    }

    private string? AskGroupName(string title, string initialName)
    {
        NativeMethods.SetActivatable(_hwnd, true);
        NativeMethods.BringToForeground(_hwnd);
        try
        {
            var dialog = new GroupNameWindow(title, initialName) { Owner = this };
            return dialog.ShowDialog() == true ? dialog.GroupName : null;
        }
        finally
        {
            NativeMethods.SetActivatable(_hwnd, false);
            NativeMethods.ReassertTopmost(_hwnd);
        }
    }

    private void OnManageGroupClick(object sender, RoutedEventArgs e)
    {
        if (_workspace.Find(_selectedGroupId) is not { } group) return;
        var menu = new ContextMenu();
        var copy = new MenuItem { Header = "复制整组路径清单" };
        copy.Click += (_, _) => CopyGroupPaths(group);
        menu.Items.Add(copy);
        var rename = new MenuItem { Header = "重命名分组…" };
        rename.Click += (_, _) =>
        {
            var name = AskGroupName("重命名分组", group.Name);
            if (name is not null && _workspace.Rename(group, name)) Status($"分组已改名：{group.Name}");
        };
        menu.Items.Add(rename);
        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = "删除分组…" };
        delete.Click += (_, _) => DeleteGroup(group);
        menu.Items.Add(delete);
        ShowMenu(menu, GroupManageButton);
    }

    private void CopyGroupPaths(ShelfGroup group)
    {
        var items = _shelf.Items.Where(item => item.GroupId == group.Id).ToList();
        var paths = PathListFormatter.Paths(items);
        if (paths.Count == 0)
        {
            Status(items.Count == 0 ? "这个分组还是空的" : "组内都是文字，没有可复制的路径");
            return;
        }
        var skipped = items.Count - paths.Count;
        Status(_clipboard.CopyText(PathListFormatter.Format(items))
            ? $"已复制 {paths.Count} 条路径" + (skipped > 0 ? $"，跳过 {skipped} 条文字" : string.Empty)
            : "复制失败，请重试（剪贴板可能被其他程序占用）");
    }

    private void DeleteGroup(ShelfGroup group)
    {
        if (!Confirm($"删除分组「{group.Name}」？\n\n组内条目将移回临时区并钉住，不参与自动清理。原始文件不受影响。")) return;
        ResetScopeActions();
        var count = _workspace.Delete(group, _shelf.Items);
        // 批量改变了所属区域与钉住状态，重新应用排序及临时区上限。
        _shelf.LoadFrom(_shelf.Items.ToList());
        Status($"已删除分组，{count} 条移回临时区并钉住");
    }

    private void OnItemContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var entry in menu.Items.OfType<MenuItem>().Where(item => item.Tag as string == "WorkspaceMove"))
            entry.Visibility = _settings.EnableWorkspace ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnMoveMenuOpened(object sender, RoutedEventArgs e)
    {
        if (!ReferenceEquals(e.Source, sender) || sender is not MenuItem menu || ItemOf(sender) is not { } item) return;
        menu.Items.Clear();
        foreach (var group in _workspace.Groups)
        {
            var choice = new MenuItem { Header = group.Name, IsEnabled = group.Id != item.GroupId };
            choice.Click += (_, _) => MoveItem(item, group.Id);
            menu.Items.Add(choice);
        }
        var create = new MenuItem { Header = "新建分组并移入…" };
        create.Click += (_, _) => { if (CreateGroup() is { } group) MoveItem(item, group.Id); };
        menu.Items.Add(create);
        if (!item.IsTemporary)
        {
            menu.Items.Add(new Separator());
            var temporary = new MenuItem { Header = "移回临时区…" };
            temporary.Click += (_, _) => MoveItem(item, null);
            menu.Items.Add(temporary);
        }
    }

    private void MoveItem(ShelfItem item, string? groupId)
    {
        if (groupId is null && !Confirm("移回临时区后，将恢复上限与过期清理规则，可能立即淘汰最旧的未钉住条目。继续吗？")) return;
        var before = _shelf.Items.Count;
        if (!_shelf.MoveToGroup(item, groupId)) return;
        var evicted = before - _shelf.Items.Count;
        Status((groupId is null ? "已移回临时区" : "已移入工作区分组，长期保留")
            + (evicted > 0 ? $"，淘汰 {evicted} 条，源文件未受影响" : string.Empty));
    }
}
