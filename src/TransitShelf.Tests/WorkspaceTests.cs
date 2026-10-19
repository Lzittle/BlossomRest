using System.Security.Cryptography;
using TransitShelf.Core;
using TransitShelf.Core.Persistence;
using TransitShelf.Core.Settings;

namespace TransitShelf.Tests;

public class WorkspaceTests
{
    [Fact]
    public void CreateAndRename_DisambiguatesGroupNames()
    {
        var workspace = new WorkspaceService();
        var first = workspace.Create(" 项目 ")!;
        var second = workspace.Create("项目")!;
        Assert.Equal("项目", first.Name);
        Assert.Equal("项目 (2)", second.Name);
        Assert.False(workspace.Rename(first, "项目"));
        Assert.True(workspace.Rename(second, "资料"));
        Assert.Equal("资料", second.Name);
        Assert.True(workspace.Rename(second, "项目"));
        Assert.Equal("项目 (2)", second.Name);
    }

    [Fact]
    public void BlankGroupName_IsRejectedWithoutChange()
    {
        var workspace = new WorkspaceService();
        var group = workspace.Create("资料")!;
        var changed = 0;
        workspace.Changed += () => changed++;
        Assert.Null(workspace.Create("  "));
        Assert.False(workspace.Rename(group, "\t"));
        Assert.Equal(0, changed);
        Assert.Equal("资料", group.Name);
    }

    [Fact]
    public void Create_RejectsGroupsBeyondLimit()
    {
        var workspace = new WorkspaceService();
        for (var i = 0; i < WorkspaceService.MaxGroups; i++) Assert.NotNull(workspace.Create($"组{i}"));
        Assert.Null(workspace.Create("超出"));
        Assert.Equal(WorkspaceService.MaxGroups, workspace.Groups.Count);
    }

    [Fact]
    public void Rename_CaseOnlyChangeDoesNotConflictWithSelf()
    {
        var workspace = new WorkspaceService();
        var group = workspace.Create("Project")!;
        Assert.True(workspace.Rename(group, "project"));
        Assert.Equal("project", group.Name);
    }

    [Fact]
    public void ShrinkCapacity_KeepsWorkspaceItems()
    {
        var shelf = new ShelfService { Capacity = 2 };
        var workspace = shelf.AddText("长期资料", "group");
        shelf.AddText("临时旧");
        shelf.AddText("临时新");
        shelf.Capacity = 1;
        Assert.Equal(1, shelf.TemporaryCount);
        Assert.Contains(workspace, shelf.Items);
        Assert.Equal(2, shelf.Items.Count);
    }

    [Fact]
    public void AddText_DeduplicatesWithinEachGroup()
    {
        var shelf = new ShelfService();
        var temporary = shelf.AddText("相同内容");
        var first = shelf.AddText("相同内容", "a");
        var second = shelf.AddText("相同内容", "b");
        Assert.Same(first, shelf.AddText("相同内容", "a"));
        Assert.NotSame(temporary, first);
        Assert.NotSame(first, second);
        Assert.Equal(3, shelf.Items.Count);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void AddPath_DeduplicatesWithinEachGroup(bool image)
    {
        var shelf = new ShelfService();
        var path = Path.Combine(Path.GetTempPath(), "workspace-sample.png");
        ShelfItem Add(string? group) => image ? shelf.AddCachedImage(path, "图片", group) : shelf.AddFile(path, group);
        var temporary = Add(null);
        var grouped = Add("a");
        Assert.NotSame(temporary, grouped);
        Assert.Same(grouped, Add("a"));
        Assert.Equal(2, shelf.Items.Count);
    }

    [Fact]
    public void RemoveExpired_KeepsUnpinnedWorkspaceItems()
    {
        var shelf = new ShelfService();
        var temporary = shelf.AddText("临时资料");
        var grouped = shelf.AddText("长期资料", "a");
        temporary.CreatedAt = grouped.CreatedAt = DateTimeOffset.Now.AddDays(-60);
        var kept = ShelfRetention.RemoveExpired(shelf.Items, TimeSpan.FromHours(1), DateTimeOffset.Now);
        Assert.Same(grouped, Assert.Single(kept));
    }

    [Fact]
    public void ClearUnpinned_IsScopedAndKeepsPinnedItems()
    {
        var shelf = new ShelfService();
        shelf.AddText("临时");
        var pinnedTemporary = shelf.AddText("临时钉住");
        shelf.TogglePin(pinnedTemporary);
        shelf.AddText("组内", "a");
        var pinnedGroup = shelf.AddText("组内钉住", "a");
        shelf.TogglePin(pinnedGroup);
        var other = shelf.AddText("其他分组", "b");
        Assert.Equal((1, 1), shelf.ClearUnpinned());
        Assert.Contains(shelf.Items, item => item.Text == "组内");
        Assert.Equal((1, 1), shelf.ClearUnpinned("a"));
        Assert.Equal(3, shelf.Items.Count);
        Assert.Contains(other, shelf.Items);
        Assert.Contains(pinnedTemporary, shelf.Items);
        Assert.Contains(pinnedGroup, shelf.Items);
    }

    [Fact]
    public void MoveToGroup_ProtectsFromTemporaryCapacity()
    {
        var shelf = new ShelfService { Capacity = 1 };
        var item = shelf.AddText("资料");
        Assert.True(shelf.MoveToGroup(item, "a"));
        shelf.AddText("临时");
        Assert.True(shelf.MoveToGroup(item, "b"));
        Assert.False(shelf.MoveToGroup(item, "b"));
        Assert.Equal("b", item.GroupId);
        Assert.Contains(item, shelf.Items);
        Assert.Equal(1, shelf.TemporaryCount);
    }

    [Fact]
    public void MoveToTemporary_ReappliesCapacity()
    {
        var shelf = new ShelfService { Capacity = 1 };
        var item = shelf.AddText("旧资料", "a");
        item.CreatedAt = DateTimeOffset.Now.AddDays(-1);
        var temporary = shelf.AddText("新资料");
        shelf.MoveToGroup(item, null);
        Assert.Same(temporary, Assert.Single(shelf.Items));
    }

    [Fact]
    public void DeleteGroup_PinsMembersInTemporaryWithoutAffectingOtherGroups()
    {
        var workspace = new WorkspaceService();
        var group = workspace.Create("资料")!;
        var shelf = new ShelfService { Capacity = 1 };
        var first = shelf.AddText("一", group.Id);
        var second = shelf.AddText("二", group.Id);
        var other = shelf.AddText("三", "other");
        Assert.Equal(2, workspace.Delete(group, shelf.Items));
        shelf.LoadFrom(shelf.Items.ToList());
        Assert.Null(workspace.Find(group.Id));
        Assert.All(new[] { first, second }, item => { Assert.Null(item.GroupId); Assert.True(item.Pinned); });
        Assert.Equal("other", other.GroupId);
        Assert.Equal(3, shelf.Items.Count);
    }

    [Fact]
    public void Snapshot_RoundTripsGroupsMembershipAndDisabledSetting()
    {
        var workspace = new WorkspaceService();
        var group = workspace.Create("常用资料")!;
        var shelf = new ShelfService();
        shelf.AddText("临时");
        shelf.AddText("长期", group.Id);
        var json = ShelfStateCodec.SerializeSnapshot(shelf.Items, workspace.Groups, new ShelfSettings { EnableWorkspace = false });
        var (items, groups, settings) = ShelfStateCodec.DeserializeSnapshot(json);
        Assert.Equal(group.Id, Assert.Single(groups).Id);
        Assert.Equal("常用资料", groups[0].Name);
        Assert.Contains(items, item => item.Text == "长期" && item.GroupId == group.Id);
        Assert.Contains(items, item => item.Text == "临时" && item.IsTemporary);
        Assert.False(settings.EnableWorkspace);
        Assert.Contains("\"schemaVersion\": 3", json);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    public void LegacySnapshot_RestoresItemsToTemporary(int version)
    {
        var json = "{\"schemaVersion\":" + version + ",\"items\":[{\"id\":\"old\",\"kind\":\"text\",\"text\":\"旧资料\"}]}";
        var (items, groups, settings) = ShelfStateCodec.DeserializeSnapshot(json);
        Assert.True(Assert.Single(items).IsTemporary);
        Assert.Empty(groups);
        Assert.False(settings.EnableWorkspace);
    }

    [Fact]
    public void GroupPathList_PreservesOrderAndSkipsText()
    {
        var shelf = new ShelfService();
        shelf.AddFile(Path.Combine(Path.GetTempPath(), "temporary.txt"));
        var first = shelf.AddFile(Path.Combine(Path.GetTempPath(), "group-first.txt"), "a");
        shelf.AddText("组内文字", "a");
        var second = shelf.AddFile(Path.Combine(Path.GetTempPath(), "group-second.txt"), "a");
        shelf.AddFile(Path.Combine(Path.GetTempPath(), "other.txt"), "b");
        Assert.Equal(new[] { second.Path!, first.Path! }, PathListFormatter.Paths(shelf.Items.Where(item => item.GroupId == "a")));
    }

    [Fact]
    public void GroupOperations_LeaveOriginalFileHashUnchanged()
    {
        var root = Path.Combine(Path.GetTempPath(), $"transitshelf-workspace-{Guid.NewGuid():n}");
        Directory.CreateDirectory(root);
        try
        {
            var source = Path.Combine(root, "source.txt");
            File.WriteAllText(source, "workspace-source-hash-check");
            var before = SHA256.HashData(File.ReadAllBytes(source));
            var workspace = new WorkspaceService();
            var group = workspace.Create("资料")!;
            var shelf = new ShelfService();
            var item = shelf.AddFile(source, group.Id);
            var store = new ItemStore(Path.Combine(root, "state.json"));
            store.Save(shelf.Items, workspace.Groups, new ShelfSettings());
            var (loaded, groups, _) = store.LoadSnapshot();
            Assert.Equal(group.Id, Assert.Single(loaded).GroupId);
            Assert.Single(groups);
            workspace.Delete(group, shelf.Items);
            shelf.ClearUnpinned();
            Assert.Contains(item, shelf.Items);
            shelf.Remove(item);
            Assert.True(File.Exists(source));
            Assert.Equal(before, SHA256.HashData(File.ReadAllBytes(source)));
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
