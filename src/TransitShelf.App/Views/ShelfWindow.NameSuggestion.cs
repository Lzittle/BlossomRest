using System.IO;
using System.Windows;
using TransitShelf.Core;

namespace TransitShelf.App.Views;

/// <summary>
/// FR-51「建议名称」：为机器生成的名字给出语义建议（见技术框架设计 6.15 / ADR D38）。
/// 只改架子里的显示名，不改磁盘文件；磁盘改名仍走 FR-40 的「重命名源文件为显示名」。
/// </summary>
public partial class ShelfWindow
{
    private async void OnSuggestNameClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item)
        {
            return;
        }

        var kind = item.Kind;
        var displayName = item.DisplayName;
        var path = item.Path;
        var createdAt = item.CreatedAt;

        // 读文件时间属于阻塞 I/O，放到后台；建议本身是纯函数，可随结果一起回到 UI 线程执行。
        var modifiedAt = await Task.Run(() => LastWriteTimeOf(path, createdAt));
        var suggestion = NameSuggester.Suggest(kind, displayName, path, modifiedAt, Loc.Language);
        if (suggestion is null)
        {
            Status(Loc.Text("T8A90B938E02A"));
            return;
        }

        if (!Confirm(Loc.Format("T4C45FC39F133", suggestion.Name, suggestion.Reason)
                     + Loc.Text("T4C0BEA47C9D7")))
        {
            Status(Loc.Text("TACFBC9DBE201"));
            return;
        }

        item.DisplayName = suggestion.Name;
        ScheduleSave();
        Status(Loc.Format("TEFD3EC5E9B37", suggestion.Name));
    }

    /// <summary>文件 / 文件夹的修改时间；失效条目或读取失败时退回收录时间。</summary>
    private static DateTimeOffset LastWriteTimeOf(string? path, DateTimeOffset fallback)
    {
        if (path is null)
        {
            return fallback;
        }

        try
        {
            if (Directory.Exists(path))
            {
                return new DateTimeOffset(Directory.GetLastWriteTime(path));
            }

            if (File.Exists(path))
            {
                return new DateTimeOffset(File.GetLastWriteTime(path));
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 读不到时间不影响建议名本身，退回收录时间即可。
        }

        return fallback;
    }
}
