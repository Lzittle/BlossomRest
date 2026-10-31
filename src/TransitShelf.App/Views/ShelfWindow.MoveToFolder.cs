using System.IO;
using System.Windows;
using TransitShelf.Core;
using TransitShelf.Platform;
using TransitShelf.Platform.Interop;

namespace TransitShelf.App.Views;

/// <summary>
/// FR-23「移动到文件夹…」与单级撤销。移动会真实改动用户文件位置，因此必须二次确认，
/// 且目标重名时只追加序号、绝不覆盖（见技术框架 6.14 / ADR D37）。
/// </summary>
public partial class ShelfWindow
{
    private readonly FileMoveService _mover = new();

    /// <summary>最近一次移动，只在本次运行内保留；单级撤销。</summary>
    private MoveRecord? _lastMove;

    private sealed record MoveRecord(string OriginalPath, string CurrentPath, ShelfItem Item);

    private async void OnMoveToFolderClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { Path: { } source } item)
        {
            return;
        }

        if (!TryPickMoveTarget(item, out var target))
        {
            return;
        }

        if (!Confirm($"将「{item.DisplayName}」移动到：\n\n{target}\n\n原文件会从当前位置移走；目标已有同名文件时自动追加序号，不会覆盖。"
                     + "移动后可点「撤销移动」恢复。继续吗？"))
        {
            return;
        }

        Status("正在移动…", 30);
        try
        {
            var outcome = await Task.Run(() => _mover.Move(source, target));
            if (!outcome.Moved)
            {
                Status("该条目已经在这个文件夹中");
                return;
            }

            item.Path = outcome.Path;
            item.RefreshState();
            ScheduleSave();

            _lastMove = new MoveRecord(source, outcome.Path, item);
            UpdateUndoMoveButton();

            var folder = FolderName(target);
            Status(outcome.Renamed
                ? $"已移动到「{folder}」，同名已改为「{Path.GetFileName(outcome.Path)}」；可撤销"
                : $"已移动到「{folder}」；可撤销");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            Status($"移动失败：{ex.Message}");
        }
    }

    private async void OnUndoMoveClick(object sender, RoutedEventArgs e)
    {
        if (_lastMove is not { } record)
        {
            return;
        }

        var originalDirectory = Path.GetDirectoryName(record.OriginalPath);
        if (originalDirectory is null)
        {
            Status("撤销失败：找不到原始目录");
            return;
        }

        Status("正在撤销移动…", 30);
        try
        {
            var outcome = await Task.Run(() => _mover.Move(record.CurrentPath, originalDirectory));

            if (_shelf.Items.Contains(record.Item))
            {
                record.Item.Path = outcome.Path;
                record.Item.RefreshState();
                ScheduleSave();
            }

            _lastMove = null;
            UpdateUndoMoveButton();

            Status(outcome.Renamed
                ? $"已撤销移动；原位置已有同名文件，落位为「{Path.GetFileName(outcome.Path)}」"
                : "已撤销移动，文件已回到原位置");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            Status($"撤销失败：{ex.Message}；可稍后重试");
        }
    }

    private void UpdateUndoMoveButton() =>
        UndoMoveButton.Visibility = _lastMove is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>选择目标文件夹；与添加文件夹使用同一套"临时允许激活"处理，避免对话框被浮窗挡住。</summary>
    private bool TryPickMoveTarget(ShelfItem item, out string target)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = $"把「{item.DisplayName}」移动到哪个文件夹",
            UseDescriptionForTitle = true,
            ShowNewFolderButton = true,
        };

        NativeMethods.SetActivatable(_hwnd, true);
        NativeMethods.BringToForeground(_hwnd);

        try
        {
            dialog.ShowDialog();
        }
        finally
        {
            NativeMethods.SetActivatable(_hwnd, false);
            NativeMethods.ReassertTopmost(_hwnd);
        }

        target = dialog.SelectedPath;
        return !string.IsNullOrWhiteSpace(target);
    }

    private static string FolderName(string path)
    {
        var name = Path.GetFileName(path.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? path : name;
    }
}
