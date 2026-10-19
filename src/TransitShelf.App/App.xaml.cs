using System.IO;
using System.Threading;
using System.Windows;
using TransitShelf.App.Theme;
using TransitShelf.App.Views;
using TransitShelf.Core.Settings;
using TransitShelf.Platform;

namespace TransitShelf.App;

public partial class App : Application
{
    private Mutex? _singleInstance;
    private System.Windows.Forms.NotifyIcon? _tray;

    protected override void OnStartup(StartupEventArgs e)
    {
        // 常驻工具不应因为单次异常就整个退出（曾经就因为剪贴板被占用而崩溃消失）。
        DispatcherUnhandledException += OnDispatcherUnhandledException;

        // 单实例：第二次启动直接退出，避免出现两个悬浮框。
        _singleInstance = new Mutex(true, @"Local\TransitShelf.SingleInstance", out var createdNew);
        if (!createdNew)
        {
            Shutdown();
            return;
        }

        base.OnStartup(e);

        var window = new ShelfWindow();
        MainWindow = window;
        window.Show();
        CreateTrayIcon(window);
    }

    private void OnDispatcherUnhandledException(object sender, System.Windows.Threading.DispatcherUnhandledExceptionEventArgs e)
    {
        e.Handled = true;
        TryLogError(e.Exception);
    }

    private static void TryLogError(Exception exception)
    {
        try
        {
            AppPaths.EnsureCreated();
            File.AppendAllText(
                Path.Combine(AppPaths.Root, "error.log"),
                $"[{DateTimeOffset.Now:O}] {exception}{Environment.NewLine}{Environment.NewLine}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            // 记录失败不再抛出
        }
    }

    /// <summary>
    /// 托盘图标是"找不到窗口"的兜底入口：本窗口不进任务栏、不进 Alt+Tab，
    /// 若被其他置顶窗口盖住，用户还能从这里唤回。
    /// </summary>
    private void CreateTrayIcon(ShelfWindow window)
    {
        // 菜单内容统一由 ShelfContextMenu 构建：托盘、悬浮球、面板三处共用同一套动作，
        // 避免加一项要改三处（见该类注释）。托盘额外带上「显示/收起」——
        // 它是"面板被盖住或已收起"时唯一的兜底入口。
        var menu = new System.Windows.Forms.ContextMenuStrip();
        ShelfActions.AppendTo(menu, window, includePanelVisibility: true);

        _tray = new System.Windows.Forms.NotifyIcon
        {
            Icon = LoadTrayIcon(),
            Text = "中转站",
            ContextMenuStrip = menu,
            Visible = true,
        };

        _tray.DoubleClick += (_, _) => window.ShowPanel();
    }

    /// <summary>
    /// 托盘图标来自 Assets/TransitShelf.ico（玫瑰文件夹主视觉，8 个尺寸）。
    /// 托盘会取到正好 16px 的那一帧，
    /// 比让系统从 32px 缩下来清晰。读不到时退回系统默认图标，不影响常驻。
    /// </summary>
    private static System.Drawing.Icon LoadTrayIcon()
    {
        try
        {
            using var stream = GetResourceStream(new Uri("Assets/TransitShelf.ico", UriKind.Relative))?.Stream;
            return stream is null
                ? System.Drawing.SystemIcons.Application
                : new System.Drawing.Icon(stream);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or System.Runtime.InteropServices.ExternalException)
        {
            TryLogError(ex);
            return System.Drawing.SystemIcons.Application;
        }
    }

    protected override void OnExit(ExitEventArgs e)
    {
        if (_tray is not null)
        {
            _tray.Visible = false;
            _tray.Dispose();
        }

        _singleInstance?.Dispose();
        base.OnExit(e);
    }
}
