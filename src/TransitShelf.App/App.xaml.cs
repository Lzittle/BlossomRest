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
        var menu = new System.Windows.Forms.ContextMenuStrip();
        menu.Items.Add("收录剪贴板内容", null, (_, _) => window.CollectFromClipboard());
        menu.Items.Add("添加文件…", null, (_, _) => window.AddFilesFromDialog());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        // 自动收录开关（默认关闭，见红线 R6）
        var watchDownloads = new System.Windows.Forms.ToolStripMenuItem("自动收录下载目录")
        {
            CheckOnClick = true,
            Checked = window.Settings.WatchDownloads,
        };
        watchDownloads.CheckedChanged += (_, _) => window.SetWatchDownloads(watchDownloads.Checked);

        var watchWeChat = new System.Windows.Forms.ToolStripMenuItem("自动收录微信接收目录")
        {
            CheckOnClick = true,
            Checked = window.Settings.WatchWeChat,
        };
        watchWeChat.CheckedChanged += (_, _) => window.SetWatchWeChat(watchWeChat.Checked);

        menu.Items.Add(watchDownloads);
        menu.Items.Add(watchWeChat);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        // 设置：清理策略、退出清空、工作区开关都收进独立窗口（FR-72 / FR-74 / FR-75）。
        // 不做成多层子菜单——设置项会随版本增长，子菜单藏得越深越没人找得到。
        menu.Items.Add("设置…", null, (_, _) => window.ShowSettings());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        // 配色（FR-73）：一条"切换到 X"的条目，直接写出目标方案名——
        // 之前做成下级菜单，用户根本找不到（实机反馈）。
        var paletteToggle = new System.Windows.Forms.ToolStripMenuItem();

        void RefreshPaletteToggle()
        {
            var active = PaletteService.Normalize(window.Settings.Palette);
            paletteToggle.Text = "切换配色：" + PaletteService.DisplayName(PaletteService.Next(active));
            paletteToggle.ToolTipText = "当前：" + PaletteService.DisplayName(active);
        }

        paletteToggle.Click += (_, _) =>
        {
            window.ApplyPalette(PaletteService.Next(window.Settings.Palette));
            RefreshPaletteToggle();
        };
        RefreshPaletteToggle();
        menu.Items.Add(paletteToggle);
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());

        menu.Items.Add("显示中转站", null, (_, _) => window.ShowPanel());
        menu.Items.Add("收起", null, (_, _) => window.CollapsePanel());
        menu.Items.Add("清空临时区", null, (_, _) => window.ClearAll());
        menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
        menu.Items.Add("退出", null, (_, _) => Shutdown());

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
