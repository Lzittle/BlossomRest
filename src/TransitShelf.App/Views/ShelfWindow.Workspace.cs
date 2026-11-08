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
        PanelTitle.Text = _workspaceActive ? Loc.Text("T37979829426B") : Loc.Text("T8B9E89F157E4");
        PanelContext.ToolTip = _workspaceActive
            ? Loc.Format("T62EF217337E4", group?.Name ?? Loc.Text("TCDB8BBE5DB23"))
            : Loc.Text("T7D9A119A4CCD");
        GroupNameText.Text = group?.Name ?? Loc.Text("T71B4A91E0593");
        GroupPicker.ToolTip = group?.Name ?? Loc.Text("T3ABE206FAA92");
        GroupPicker.IsEnabled = _workspace.Groups.Count > 0;
        GroupManageButton.IsEnabled = group is not null;
        EmptyHint.Text = _workspaceActive
            ? group is null ? Loc.Text("TFE99B3AEDC74")
                : Loc.Text("T735E8099B3C6")
            : Loc.Text("TC97FB6E5E317");
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
            selected ? Loc.Text("T7C27592242B8") : Loc.Text("T2A1B11A5D52A"));
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
        Status(Loc.Text("T239DC2012944"));
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
            Status(Loc.Format("T0F7B958F6CA9", WorkspaceService.MaxGroups));
            return null;
        }
        var name = AskGroupName(Loc.Text("T3D49D9FCB6D1"), string.Empty);
        if (name is null) return null;
        var group = _workspace.Create(name);
        if (group is not null)
        {
            SelectScope(true, group.Id);
            Status(Loc.Format("T5F117A854064", group.Name));
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
        var copy = new MenuItem { Header = Loc.Text("TB3DAE8320E66") };
        copy.Click += (_, _) => CopyGroupPaths(group);
        menu.Items.Add(copy);
        var rename = new MenuItem { Header = Loc.Text("T13888A54EDAA") };
        rename.Click += (_, _) =>
        {
            var name = AskGroupName(Loc.Text("T5218CD8D059E"), group.Name);
            if (name is not null && _workspace.Rename(group, name)) Status(Loc.Format("T96B1E25597B9", group.Name));
        };
        menu.Items.Add(rename);
        menu.Items.Add(new Separator());
        var delete = new MenuItem { Header = Loc.Text("T2FCE1D553FDA") };
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
            Status(items.Count == 0 ? Loc.Text("T923BBEAD8E0E") : Loc.Text("T5868EB4D2DD9"));
            return;
        }
        var skipped = items.Count - paths.Count;
        Status(_clipboard.CopyText(PathListFormatter.Format(items))
            ? Loc.Format("TE42E343CCCB4", paths.Count) + (skipped > 0 ? Loc.Format("TC6BCB3A08D35", skipped) : string.Empty)
            : Loc.Text("T8F41785DBB7D"));
    }

    private void DeleteGroup(ShelfGroup group)
    {
        if (!Confirm(Loc.Format("T70416297388B", group.Name))) return;
        ResetScopeActions();
        var count = _workspace.Delete(group, _shelf.Items);
        // 批量改变了所属区域与钉住状态，重新应用排序及临时区上限。
        _shelf.LoadFrom(_shelf.Items.ToList());
        Status(Loc.Format("T78475E8F4A05", count));
    }

    private void OnItemContextMenuOpened(object sender, RoutedEventArgs e)
    {
        if (sender is not ContextMenu menu) return;
        foreach (var entry in menu.Items.OfType<MenuItem>().Where(item => item.Tag as string == "WorkspaceMove"))
            entry.Visibility = _settings.EnableWorkspace ? Visibility.Visible : Visibility.Collapsed;

        // 「移动到文件夹…」只对仍存在的文件 / 文件夹条目有意义（FR-23）。
        foreach (var entry in menu.Items.OfType<MenuItem>().Where(item => item.Tag as string == "MoveToFolder"))
            entry.Visibility = menu.DataContext is ShelfItem { Path: not null, IsMissing: false }
                ? Visibility.Visible
                : Visibility.Collapsed;

        // 「建议名称」只在显示名明显是机器生成时出现（FR-51）。
        foreach (var entry in menu.Items.OfType<MenuItem>().Where(item => item.Tag as string == "NameSuggestion"))
            entry.Visibility = menu.DataContext is ShelfItem candidate
                               && NameSuggester.HasSuggestion(candidate.Kind, candidate.DisplayName)
                ? Visibility.Visible
                : Visibility.Collapsed;
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
        var create = new MenuItem { Header = Loc.Text("TAD8CA4718EE9") };
        create.Click += (_, _) => { if (CreateGroup() is { } group) MoveItem(item, group.Id); };
        menu.Items.Add(create);
        if (!item.IsTemporary)
        {
            menu.Items.Add(new Separator());
            var temporary = new MenuItem { Header = Loc.Text("T02F92D7764F5") };
            temporary.Click += (_, _) => MoveItem(item, null);
            menu.Items.Add(temporary);
        }
    }

    private void MoveItem(ShelfItem item, string? groupId)
    {
        if (groupId is null && !Confirm(Loc.Text("T143822348704"))) return;
        var before = _shelf.Items.Count;
        if (!_shelf.MoveToGroup(item, groupId)) return;
        var evicted = before - _shelf.Items.Count;
        Status((groupId is null ? Loc.Text("TEE1B789562CF") : Loc.Text("T7C0AF31FF026"))
            + (evicted > 0 ? Loc.Format("T67D72596AF2F", evicted) : string.Empty));
    }
}
