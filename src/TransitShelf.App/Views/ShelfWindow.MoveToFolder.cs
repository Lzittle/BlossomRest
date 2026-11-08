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

        if (!Confirm(Loc.Format("TA6056189FF03", item.DisplayName, target)
                     + Loc.Text("T63F8D29140A6")))
        {
            return;
        }

        Status(Loc.Text("TC133A47D40C3"), 30);
        try
        {
            var outcome = await Task.Run(() => _mover.Move(source, target));
            if (!outcome.Moved)
            {
                Status(Loc.Text("T7D85C01675E2"));
                return;
            }

            item.Path = outcome.Path;
            item.RefreshState();
            ScheduleSave();

            _lastMove = new MoveRecord(source, outcome.Path, item);
            UpdateUndoMoveButton();

            var folder = FolderName(target);
            Status(outcome.Renamed
                ? Loc.Format("TCD37925B4E85", folder, Path.GetFileName(outcome.Path))
                : Loc.Format("T6D435951A749", folder));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            Status(Loc.Format("T2C46D88280BA", ex.Message));
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
            Status(Loc.Text("T19A377F7F45D"));
            return;
        }

        Status(Loc.Text("T1AD3D7ED00A2"), 30);
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
                ? Loc.Format("TF1D87E7EEB76", Path.GetFileName(outcome.Path))
                : Loc.Text("T760FD5289D80"));
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException
                                       or NotSupportedException)
        {
            Status(Loc.Format("T2786182F5043", ex.Message));
        }
    }

    private void UpdateUndoMoveButton() =>
        UndoMoveButton.Visibility = _lastMove is null ? Visibility.Collapsed : Visibility.Visible;

    /// <summary>选择目标文件夹；与添加文件夹使用同一套"临时允许激活"处理，避免对话框被浮窗挡住。</summary>
    private bool TryPickMoveTarget(ShelfItem item, out string target)
    {
        using var dialog = new System.Windows.Forms.FolderBrowserDialog
        {
            Description = Loc.Format("T930C3A619FE3", item.DisplayName),
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
