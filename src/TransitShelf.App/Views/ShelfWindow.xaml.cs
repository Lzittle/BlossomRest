using System.IO;
using System.Windows;
using System.Windows.Data;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using Microsoft.Win32;
using TransitShelf.App.Theme;
using TransitShelf.Core;
using TransitShelf.Core.Persistence;
using TransitShelf.Core.Settings;
using TransitShelf.Platform;
using TransitShelf.Platform.Interop;

namespace TransitShelf.App.Views;

/// <summary>
/// 中转站浮窗（demo 版）：收起胶囊 → 悬浮/点击/拖拽经过三种方式展开 → 条目列表。
/// 窗口不抢焦点、不进 Alt+Tab（见技术框架设计 6.1）。
/// </summary>
public partial class ShelfWindow : Window
{
    private const double ExpandedWidth = 300;
    private const double ExpandedHeight = 400;
    private const double CollapsedSize = 64;
    private const double ExpandedCornerRadius = 8;
    private const double EdgeMargin = 12;

    /// <summary>
    /// 静止悬浮是否自动展开。**默认关闭**：它和"按住胶囊拖动"抢的是同一个动作——
    /// 鼠标一移上去就开始计时，用户还没按下、面板就弹开了，于是拖动只能靠抢 300 ms，
    /// 而且鼠标只是路过屏幕边缘也会被弹开。FR-02 里这条本来就标注为"（可选）"。
    /// 展开仍有三条路：单击胶囊、全局快捷键、拖文件经过热区。
    /// 将来要做设置项时，把这里换成设置值即可（见 PRD FR-70）。
    /// </summary>
    private const bool HoverExpandEnabled = false;
    private static readonly TimeSpan ItemTimeToLive = TimeSpan.FromHours(24);

    private readonly ShelfService _shelf = new() { Capacity = 9 };
    private readonly ClipboardService _clipboard = new();
    private readonly ShellService _shell = new();
    private readonly DragOutService _dragOut = new();
    private readonly ImageCacheService _images = new();
    private readonly FileRenameService _renamer = new();
    private readonly SourceWatcher _watcher = new();
    private readonly ItemStore _store = new(AppPaths.StateFile);

    private ShelfSettings _settings = new();

    private readonly DispatcherTimer _expandTimer;
    private readonly DispatcherTimer _collapseTimer;
    private readonly DispatcherTimer _statusTimer;
    private readonly DispatcherTimer _clearTimer;
    private readonly DispatcherTimer _topmostTimer;
    private readonly DispatcherTimer _saveTimer;
    private readonly DispatcherTimer _clockTimer;
    private const int HotKeyToggleId = 0x5453;   // "TS"
    private const int HotKeyCollectId = 0x5454;  // "TT"
    private static readonly uint HotKeyToggleKey = 0x44;  // D
    private static readonly uint HotKeyCollectKey = 0x43; // C

    private IntPtr _hwnd;
    private bool _expanded;
    private bool _pillHidden;
    private bool _clearArmed;
    private bool _clearArmedForWorkspace;
    private string? _clearArmedGroupId;
    private bool _suppressClick;
    private bool _draggingOut;
    private Point? _dragOrigin;
    private ShelfItem? _dragCandidate;

    /// <summary>设置窗口（FR-72 / FR-74 / FR-75）。单实例，反复点托盘不重复开。</summary>
    private Views.SettingsWindow? _settingsWindow;

    // 收起态胶囊的拖动状态。拖动过程使用物理像素，松开后保存位置（FR-05）。
    private bool _pillPressed;
    private bool _pillDragging;
    private Point _pillPressPoint;
    private int _pillPressLeftPx;
    private int _pillPressTopPx;
    private int _pillPressWidthPx;
    private int _pillPressHeightPx;
    private int? _customLeftPx;
    private int? _customTopPx;
    private string? _customMonitor;
    private bool _layoutApplying;
    private bool _layoutRefreshPending;

    /// <summary>
    /// 是否由"悬浮"触发展开。只有悬浮展开才在鼠标移出后自动收起；
    /// 点击 / 快捷键 / 拖拽展开的面板必须保持，否则用户一移开鼠标就消失。
    /// </summary>
    private bool _openedByHover;

    /// <summary>
    /// 多选模式（FR-21）。用显式按钮切换而不是 Ctrl+点击：本窗口永不激活，
    /// 无焦点窗口上的修饰键状态不可靠（见技术框架设计 6.10 / ADR D19）。
    /// </summary>
    private bool _multiSelect;

    // FR-44：筛选是当前视图状态，不写入 state.json，也不改变条目排序。
    private string _searchQuery = string.Empty;
    private ShelfFilterKind _filterKind = ShelfFilterKind.All;
    private bool _itemViewReady;

    public ShelfWindow()
    {
        InitializeComponent();

        // 悬浮 300 ms 展开；移出 1200 ms 收起（失焦不收起；主动展开的面板不自动收起）。
        _expandTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(300) };
        _expandTimer.Tick += (_, _) =>
        {
            _expandTimer.Stop();
            ExpandFromHover();
        };

        _collapseTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(1200) };
        _collapseTimer.Tick += (_, _) =>
        {
            _collapseTimer.Stop();
            if (!IsMouseOver && _openedByHover)
            {
                Collapse();
            }
        };

        _statusTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _statusTimer.Tick += (_, _) =>
        {
            _statusTimer.Stop();
            StatusText.Text = string.Empty;
        };

        // 清空需要二次点击确认，避免误触，同时避免弹出抢焦点的模态框。
        _clearTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(3) };
        _clearTimer.Tick += (_, _) =>
        {
            _clearTimer.Stop();
            _clearArmed = false;
            _clearArmedForWorkspace = false;
            _clearArmedGroupId = null;
            UpdateClearButton();
        };

        // 置顶抢位心跳：本窗口不激活，任何别的置顶窗口都会永久盖住它（见 NativeMethods.ReassertTopmost）。
        _topmostTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(4) };
        _topmostTimer.Tick += (_, _) => NativeMethods.ReassertTopmost(_hwnd);

        // 变更后延迟落盘，避免连续操作时频繁写文件。
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(500) };
        _saveTimer.Tick += (_, _) =>
        {
            _saveTimer.Stop();
            SaveState();
        };

        // 相对时间心跳。ShelfItem.RelativeTime 是只读计算属性，不会自己发通知，
        // 不踢它就会永远停在"刚刚"（见 ShelfItem.NotifyRelativeTime）。
        _clockTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(20) };
        _clockTimer.Tick += (_, _) => _shelf.RefreshTimeLabels();

        _itemView = CollectionViewSource.GetDefaultView(_shelf.Items);
        _itemView.Filter = value => value is ShelfItem item
            && IsCurrentItem(item)
            && ShelfItemFilter.Matches(item, _searchQuery, _filterKind);
        _workspace.Changed += OnWorkspaceChanged;
        _shelf.Changed += RefreshWorkspace;
        _shelf.Changed += ScheduleSave;
        ItemList.ItemsSource = _itemView;
        ItemList.SelectionChanged += (_, _) => UpdateBatchBar();
        _itemViewReady = true;

        SourceInitialized += OnSourceInitialized;
        Closed += OnClosed;
        ApplyLayout();
        RefreshWorkspace();
        LoadState();
        _clockTimer.Start();
    }

    private void OnSourceInitialized(object? sender, EventArgs e)
    {
        _hwnd = new WindowInteropHelper(this).Handle;
        NativeMethods.ApplyNonActivatingToolWindow(_hwnd);
        // 轮廓由 XAML 自绘（收起态正圆），系统再描边框/投影会在圆外面留下一个方块
        NativeMethods.TryRemoveWindowFrame(_hwnd);

        // 全局快捷键：Ctrl+Alt+D 唤出 / 收起（被占用时不影响其他功能）。
        var modifiers = NativeMethods.ModControl | NativeMethods.ModAlt;
        var toggleOk = NativeMethods.TryRegisterHotKey(_hwnd, HotKeyToggleId, modifiers, HotKeyToggleKey);
        var collectOk = NativeMethods.TryRegisterHotKey(_hwnd, HotKeyCollectId, modifiers, HotKeyCollectKey);
        if (HwndSource.FromHwnd(_hwnd) is { } source)
        {
            source.AddHook(WndProc);
        }

        SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
        // 构造阶段没有 HWND，位置只能先用 WPF 默认值；窗口建立后按保存的物理坐标恢复。
        ApplyLayout();

        _topmostTimer.Start();
        Status(toggleOk && collectOk
            ? $"{PaletteService.DisplayName(_settings.Palette)} · Ctrl+Alt+D 唤出 · Ctrl+Alt+C 收录"
            : $"{PaletteService.DisplayName(_settings.Palette)} · 部分快捷键被其他软件占用");
    }

    private void OnClosed(object? sender, EventArgs e)
    {
        _saveTimer.Stop();
        _clockTimer.Stop();

        // FR-74 退出清空：必须**先清后存**。
        // 反过来的话会把刚清掉的条目写进 state.json，下次启动又原样回来——开关等于没开。
        // 顺带只清未钉住的：钉住的语义是"别动它"，与手动清空一致（见 ClearAll）。
        if (_settings.ClearOnExit)
        {
            _shelf.ClearUnpinned();
        }

        SaveState();
        _watcher.Stop();
        _topmostTimer.Stop();
        SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
        NativeMethods.UnregisterHotKeyById(_hwnd, HotKeyToggleId);
        NativeMethods.UnregisterHotKeyById(_hwnd, HotKeyCollectId);
    }

    private IntPtr WndProc(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == NativeMethods.WmDisplayChange || msg == NativeMethods.WmDpiChanged)
        {
            ScheduleDisplayLayout();
            return IntPtr.Zero;
        }

        if (msg != NativeMethods.WmHotKey)
        {
            return IntPtr.Zero;
        }

        switch (wParam.ToInt32())
        {
            case HotKeyToggleId:
                TogglePanel();
                handled = true;
                break;

            case HotKeyCollectId:
                ShowPanel();
                CollectFromClipboard();
                handled = true;
                break;
        }

        return IntPtr.Zero;
    }

    private void OnDisplaySettingsChanged(object? sender, EventArgs e) => ScheduleDisplayLayout();

    private void ScheduleDisplayLayout()
    {
        if (_layoutRefreshPending || Dispatcher.HasShutdownStarted)
        {
            return;
        }

        _layoutRefreshPending = true;
        Dispatcher.BeginInvoke(DispatcherPriority.Loaded, new Action(() =>
        {
            _layoutRefreshPending = false;
            ApplyLayout();
        }));
    }

    /// <summary>托盘/悬浮球右键菜单调用：面板当前是否处于展开态（决定「显示/收起」两项谁是可用的）。</summary>
    public bool IsPanelExpanded => _expanded;

    /// <summary>托盘菜单 / 快捷键调用：展开并抢回置顶。悬浮球被隐藏时先恢复窗口（FR-09）。</summary>
    public void ShowPanel()
    {
        RestorePillIfHidden();
        _expandTimer.Stop();
        _collapseTimer.Stop();
        ExpandSticky();
        NativeMethods.ReassertTopmost(_hwnd);
    }

    public void CollapsePanel() => Collapse();

    /// <summary>
    /// 悬浮球右键「隐藏悬浮球（不退出）」：只隐藏窗口，进程、托盘、快捷键和条目都保留。
    /// 隐藏状态不落盘，重启后恢复显示，避免用户忘记隐藏而以为程序丢了（FR-09 / ADR D31）。
    /// </summary>
    public void HidePill()
    {
        if (_pillHidden)
        {
            return;
        }

        _expandTimer.Stop();
        _collapseTimer.Stop();
        _pillPressed = false;
        _pillDragging = false;
        if (PillPanel.IsMouseCaptured)
        {
            PillPanel.ReleaseMouseCapture();
        }

        _openedByHover = false;
        if (_expanded)
        {
            SetExpanded(false);
        }

        _pillHidden = true;
        Hide();
    }

    /// <summary>托盘 / 快捷键唤回：隐藏过的窗口需要先 Show 才能继续展开。</summary>
    private void RestorePillIfHidden()
    {
        if (!_pillHidden)
        {
            return;
        }

        _pillHidden = false;
        Show();
    }

    public void ClearAll()
    {
        // 托盘和右键菜单也走同一套二次确认，避免误触直接清空（FR-43）。
        // 菜单项明确叫「清空临时区」，所以不受当前工作区标签影响。
        RequestClear(workspaceScope: false, groupId: null);
    }

    private void ClearItems(string? groupId)
    {
        var (removed, kept) = _shelf.ClearUnpinned(groupId);

        if (removed == 0 && kept == 0)
        {
            Status("这里已经是空的");
        }
        else if (kept > 0)
        {
            Status($"已清空 {removed} 项，保留 {kept} 项已钉住");
        }
        else
        {
            Status($"已清空 {removed} 项");
        }
    }

    /// <summary>当前设置（托盘菜单用它初始化勾选状态）。</summary>
    public ShelfSettings Settings => _settings;

    /// <summary>开关：自动收录下载目录（FR-15）。</summary>
    public void SetWatchDownloads(bool enabled)
    {
        _settings.WatchDownloads = enabled;
        ApplyWatching();
        ScheduleSave();
    }

    /// <summary>开关：自动收录微信接收目录（FR-15）。</summary>
    public void SetWatchWeChat(bool enabled)
    {
        _settings.WatchWeChat = enabled;
        ApplyWatching();
        ScheduleSave();
    }

    /// <summary>
    /// 改临时区上限（FR-72）。立即生效：<see cref="ShelfService.Capacity"/> 的 setter 内部会触发淘汰，
    /// 因此要把淘汰条数回报给用户——这是不可逆操作，不能改完默默吞掉。
    ///
    /// 越界值在这里钳制而不是信任输入：设置窗口允许自由输入，但手输 0 会让
    /// <see cref="ShelfService.Capacity"/> 兜底到 1，等于一句话清空整个临时区。
    /// </summary>
    public void SetCapacity(int value)
    {
        var target = Math.Clamp(value, ShelfSettings.CapacityMin, ShelfSettings.CapacityMax);
        if (_settings.Capacity == target)
        {
            return;
        }

        var before = _shelf.Items.Count;
        _settings.Capacity = target;
        _shelf.Capacity = target;
        ScheduleSave();

        var evicted = before - _shelf.Items.Count;
        var corrected = target != value ? $"（已修正为合法区间 {ShelfSettings.CapacityMin}～{ShelfSettings.CapacityMax}）" : string.Empty;
        Status(evicted > 0
            ? $"上限：{target} 项（已淘汰 {evicted} 条，源文件仍在原处）{corrected}"
            : $"上限：{target} 项{corrected}");
    }

    /// <summary>
    /// 改条目过期时长（FR-72）。
    ///
    /// 只影响**下次启动**的过期清理，运行中不扫描、不删除任何条目——
    /// 沿用"退出不清理、只靠 TTL 与手动清空"这条既有约定（见技术框架设计 5.5）。
    /// </summary>
    public void SetTimeToLiveHours(int hours)
    {
        var target = Math.Clamp(hours, ShelfSettings.TimeToLiveHoursMin, ShelfSettings.TimeToLiveHoursMax);
        if (_settings.TimeToLiveHours == target)
        {
            return;
        }

        _settings.TimeToLiveHours = target;
        ScheduleSave();
        Status($"过期时长：{target} 小时（下次启动生效）");
    }

    /// <summary>
    /// 开关：退出时清空未钉住条目（FR-74，默认关闭）。
    ///
    /// 关闭时沿用既有约定"退出不清理、条目留到下次启动"；开启后每次退出都会丢弃现场，
    /// 所以它必须**默认关闭**，且设置项里带一句风险说明让用户知情（红线 R4：删除条目绝不删用户文件，
    /// 这里只清列表，源文件仍在）。
    /// </summary>
    public void SetClearOnExit(bool enabled)
    {
        _settings.ClearOnExit = enabled;
        ScheduleSave();
        Status(enabled ? "退出时清空临时区未钉住项，钉住项和工作区保留" : "退出时不再清空，条目留到下次启动");
    }

    /// <summary>
    /// 开关：启用工作区（FR-75，默认关闭）。
    ///
    /// 关闭时主面板只有临时区，与现有行为完全一致；开启后顶部出现「临时区 / 工作区」标签。
    /// 关闭只隐藏入口，已保存分组与条目继续保留。
    /// </summary>
    public void SetEnableWorkspace(bool enabled)
    {
        _settings.EnableWorkspace = enabled;
        if (!enabled)
        {
            _workspaceActive = false;
        }
        ResetScopeActions();
        RefreshWorkspace();
        ApplyLayout();
        ScheduleSave();
        Status(enabled ? "已启用工作区" : "已关闭工作区，回到单临时区模式");
    }

    /// <summary>打开设置窗口（FR-72 / FR-74 / FR-75）。托盘菜单「设置…」调用它。</summary>
    public void ShowSettings()
    {
        // 单实例：反复点托盘不该开出一堆设置窗口
        if (_settingsWindow is { } existing && existing.IsLoaded)
        {
            if (existing.WindowState == WindowState.Minimized)
            {
                existing.WindowState = WindowState.Normal;
            }

            existing.Activate();
            return;
        }

        _settingsWindow = new Views.SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Show();
    }

    /// <summary>
    /// 切换配色（FR-73）。整套颜色都在 Palettes/ 的字典里，这里只换字典 + 记下选择。
    /// 托盘菜单调用它，所以改完立刻落盘，重启后仍是这一套。
    /// </summary>
    public void ApplyPalette(string name)
    {
        var target = PaletteService.Normalize(name);
        PaletteService.Apply(target);
        _settings.Palette = target;
        ScheduleSave();
        Status($"配色：{PaletteService.DisplayName(target)}");
    }

    /// <summary>按当前设置重启目录监听。</summary>
    private void ApplyWatching()
    {
        var targets = WatchTargets.Resolve(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            _settings);

        _watcher.Restart(targets, OnWatchedFile, AppPaths.CacheDirectory);

        Status(targets.Count == 0
            ? "自动收录已关闭"
            : $"正在监听 {targets.Count} 个目录，新文件会自动进架");
    }

    /// <summary>监听回调来自后台线程，切回界面线程再改集合。</summary>
    private void OnWatchedFile(string path)
    {
        Dispatcher.Invoke(() =>
        {
            var item = _shelf.AddFile(path);
            Status($"自动收录：{item.DisplayName}");
        });
    }

    private void TogglePanel()
    {
        if (_expanded)
        {
            Collapse();
        }
        else
        {
            ShowPanel();
        }
    }

    private void ApplyLayout()
    {
        var panelWidth = _workspaceActive ? 320 : ExpandedWidth;
        var panelHeight = _workspaceActive ? 520 : ExpandedHeight;

        if (_layoutApplying)
        {
            return;
        }

        _layoutApplying = true;
        try
        {
            if (_expanded)
            {
                Width = panelWidth;
                Height = panelHeight;
                // 展开态用约等于系统圆角的圆角（透明窗口下系统不再代劳，见 XAML 顶部说明）
                RootBorder.CornerRadius = new CornerRadius(ExpandedCornerRadius);
                PillPanel.Visibility = Visibility.Collapsed;
                PanelRoot.Visibility = Visibility.Visible;
            }
            else
            {
                Width = CollapsedSize;
                Height = CollapsedSize;
                // 收起态 = 正圆：64×64 取半径 32
                RootBorder.CornerRadius = new CornerRadius(CollapsedSize / 2);
                PanelRoot.Visibility = Visibility.Collapsed;
                PillPanel.Visibility = Visibility.Visible;
            }

            if (_hwnd == IntPtr.Zero)
            {
                // SourceInitialized 之前只能先给 WPF 一个可见的主屏默认值；建立 HWND 后会立即用物理坐标重排。
                var work = SystemParameters.WorkArea;
                var width = _expanded ? panelWidth : CollapsedSize;
                var height = _expanded ? panelHeight : CollapsedSize;
                Left = work.Right - width - EdgeMargin;
                Top = work.Bottom - height - EdgeMargin;
                return;
            }

            UpdateLayout();
            ApplyNativePlacement();
        }
        finally
        {
            _layoutApplying = false;
        }
    }

    private void ApplyNativePlacement()
    {
        if (!NativeMethods.TryGetWindowRect(_hwnd, out var current))
        {
            return;
        }

        var screen = ResolvePlacementScreen();
        var work = screen.WorkingArea;
        var targetLeft = work.Right - current.Width - ToPhysical(EdgeMargin);
        var targetTop = work.Bottom - current.Height - ToPhysical(EdgeMargin);

        if (_customLeftPx is { } savedLeft && _customTopPx is { } savedTop)
        {
            // 先放到目标显示器的可见工作区，触发 WPF 的 WM_DPICHANGED；
            // 随后按新 DPI 的实际尺寸再裁一次。显示器拔除时也不会先闪到旧的屏幕外坐标。
            var initialAnchor = DisplayPlacement.ClampToWorkArea(
                savedLeft,
                savedTop,
                current.Width,
                current.Height,
                work.Left,
                work.Top,
                work.Right,
                work.Bottom);
            NativeMethods.TryMoveWindowNoActivate(_hwnd, initialAnchor.Left, initialAnchor.Top);
            NativeMethods.TryGetWindowRect(_hwnd, out current);

            var dpi = NativeMethods.GetWindowDpi(_hwnd);
            var pillSize = (int)Math.Round(CollapsedSize * dpi / 96d);
            var anchor = DisplayPlacement.ClampToWorkArea(
                savedLeft,
                savedTop,
                pillSize,
                pillSize,
                work.Left,
                work.Top,
                work.Right,
                work.Bottom);
            savedLeft = anchor.Left;
            savedTop = anchor.Top;
            _customLeftPx = savedLeft;
            _customTopPx = savedTop;
            _customMonitor = screen.DeviceName;

            if (_expanded)
            {
                targetLeft = savedLeft + pillSize - current.Width;
                targetTop = savedTop + pillSize - current.Height;
            }
            else
            {
                targetLeft = savedLeft;
                targetTop = savedTop;
            }
        }

        var clamped = DisplayPlacement.ClampToWorkArea(
            targetLeft,
            targetTop,
            current.Width,
            current.Height,
            work.Left,
            work.Top,
            work.Right,
            work.Bottom);
        NativeMethods.TryMoveWindowNoActivate(_hwnd, clamped.Left, clamped.Top);

        if (_customLeftPx is not null && _customTopPx is not null)
        {
            // 显示器被拔掉或工作区缩小时，上面的锚点已修正到当前显示器，关闭时一并落盘。
            _customMonitor = screen.DeviceName;
        }
    }

    private System.Windows.Forms.Screen ResolvePlacementScreen()
    {
        if (!string.IsNullOrWhiteSpace(_customMonitor))
        {
            var saved = System.Windows.Forms.Screen.AllScreens.FirstOrDefault(screen =>
                string.Equals(screen.DeviceName, _customMonitor, StringComparison.OrdinalIgnoreCase));
            if (saved is not null)
            {
                return saved;
            }
        }

        return _hwnd != IntPtr.Zero
            ? System.Windows.Forms.Screen.FromHandle(_hwnd)
            : System.Windows.Forms.Screen.PrimaryScreen
                ?? System.Windows.Forms.Screen.AllScreens.First();
    }

    private int ToPhysical(double dip)
    {
        var dpi = NativeMethods.GetWindowDpi(_hwnd);
        return (int)Math.Round(dip * dpi / 96d);
    }

    private void SetExpanded(bool expanded)
    {
        if (_expanded == expanded)
        {
            return;
        }

        _expanded = expanded;
        ApplyLayout();
        NativeMethods.ReassertTopmost(_hwnd);
    }

    /// <summary>主动展开（点击胶囊 / 快捷键 / 拖拽经过）：常驻，不因鼠标移开而收回。</summary>
    private void ExpandSticky()
    {
        _openedByHover = false;
        SetExpanded(true);
    }

    /// <summary>
    /// 悬浮展开，只在收起状态生效。
    /// 不能覆盖用户主动展开的常驻状态，否则"点开后再去别的软件"会被自动收起。
    /// </summary>
    private void ExpandFromHover()
    {
        if (_expanded)
        {
            return;
        }

        _openedByHover = true;
        SetExpanded(true);
    }

    private void Collapse()
    {
        _openedByHover = false;
        SetExpanded(false);
    }

    private void RefreshFooter()
    {
        var scopeCount = CurrentItems.Count();
        var visibleCount = _itemView.Cast<object>().OfType<ShelfItem>().Count();
        CountText.Text = _shelf.Items.Count.ToString();
        var filtered = scopeCount > 0 && visibleCount == 0;
        if (filtered)
        {
            EmptyHint.Text = "没有匹配的条目。\n试试修改搜索词或类型筛选。";
        }

        FooterText.Text = _workspaceActive
            ? visibleCount == scopeCount ? $"{scopeCount} 条 · 长期保留" : $"{visibleCount} / {scopeCount} 条 · 长期保留"
            : visibleCount == scopeCount ? $"{scopeCount} / {_shelf.Capacity} 条" : $"{visibleCount} / {scopeCount} 条 · 上限 {_shelf.Capacity}";
        EmptyState.Visibility = visibleCount == 0 ? Visibility.Visible : Visibility.Collapsed;
        ItemList.Visibility = visibleCount == 0 ? Visibility.Collapsed : Visibility.Visible;

        // 空列表没有可勾选的对象，直接不给进多选（否则会出现一个没有任何内容可选的操作条）。
        MultiSelectButton.IsEnabled = visibleCount > 0;
    }

    private void OnSearchTextChanged(object sender, TextChangedEventArgs e)
    {
        if (SearchPlaceholder is not null)
        {
            SearchPlaceholder.Visibility = string.IsNullOrWhiteSpace(SearchBox.Text)
                ? Visibility.Visible
                : Visibility.Collapsed;
        }

        if (!_itemViewReady)
        {
            return;
        }

        _searchQuery = SearchBox.Text;
        RefreshItemFilter();
    }

    private void OnSearchBoxPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (_hwnd == IntPtr.Zero)
        {
            return;
        }

        // 浮窗默认带 WS_EX_NOACTIVATE，保证平时不打断当前应用；搜索是显式输入动作，
        // 这里只在用户点入搜索框时临时允许激活，失去键盘焦点后再恢复原行为。
        NativeMethods.SetActivatable(_hwnd, true);
        NativeMethods.BringToForeground(_hwnd);
        Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            SearchBox.Focus();
            SearchBox.CaretIndex = SearchBox.Text.Length;
        }));
    }

    private void OnSearchBoxLostKeyboardFocus(object sender, KeyboardFocusChangedEventArgs e) =>
        NativeMethods.SetActivatable(_hwnd, false);

    private void OnFilterTypeClick(object sender, RoutedEventArgs e)
    {
        var menu = new ContextMenu();
        foreach (var kind in Enum.GetValues<ShelfFilterKind>())
        {
            var choice = new MenuItem
            {
                Header = FilterKindLabel(kind),
                IsCheckable = true,
                IsChecked = kind == _filterKind,
            };
            choice.Click += (_, _) => SetFilterKind(kind);
            menu.Items.Add(choice);
        }

        ShowMenu(menu, FilterTypeButton);
    }

    private void SetFilterKind(ShelfFilterKind kind)
    {
        _filterKind = kind;
        FilterTypeButton.Content = FilterKindLabel(kind);
        RefreshItemFilter();
    }

    private void RefreshItemFilter()
    {
        _itemView.Refresh();
        RefreshFooter();
        UpdateBatchBar();
    }

    private static string FilterKindLabel(ShelfFilterKind kind) => kind switch
    {
        ShelfFilterKind.File => "文件",
        ShelfFilterKind.Folder => "文件夹",
        ShelfFilterKind.Image => "图片",
        ShelfFilterKind.Text => "文字",
        _ => "全部类型",
    };

    /// <summary>启动时恢复上次的条目：刷新失效状态、丢弃过期项、清理孤儿缓存（FR-41 / FR-45）。</summary>
    private void LoadState()
    {
        try
        {
            AppPaths.EnsureCreated();

            var (items, groups, settings) = _store.LoadSnapshot();
            _settings = settings;
            _customMonitor = settings.WindowMonitor;
            _customLeftPx = settings.WindowLeftPx;
            _customTopPx = settings.WindowTopPx;
            _workspace.LoadFrom(groups);
            // 分组缺失时保住引用，不能让它在不可见区域消失或被临时区清理。
            foreach (var item in items.Where(item => item.GroupId is not null && _workspace.Find(item.GroupId) is null))
            {
                item.Pinned = true;
                item.GroupId = null;
            }
            _shelf.Capacity = settings.SafeCapacity;

            // 配色先于界面内容应用：设置文件里的名字可能已过期，Normalize 会收敛到已知方案。
            PaletteService.Apply(settings.Palette);

            if (items.Count > 0)
            {
                ShelfRetention.RefreshStates(items);
                var kept = ShelfRetention.RemoveExpired(items, settings.TimeToLive, DateTimeOffset.Now);
                var expired = items.Count - kept.Count;
                var missing = kept.Count(item => item.IsMissing);

                _shelf.LoadFrom(kept);

                if (expired > 0 || missing > 0)
                {
                    Status($"恢复 {kept.Count} 条（清理 {expired} 条过期 / {missing} 条失效）");
                }
            }

            AppPaths.CleanOrphanCache(_shelf.Items.Select(item => item.CachePath));
            RefreshWorkspace();
            ApplyWatching();
            ApplyLayout();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status($"读取状态失败：{ex.Message}");
        }
    }

    private void ScheduleSave()
    {
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    private void SaveState()
    {
        try
        {
            if (_customLeftPx is { } left && _customTopPx is { } top)
            {
                _settings.WindowLeftPx = left;
                _settings.WindowTopPx = top;
                _settings.WindowMonitor = _customMonitor;
            }

            _store.Save(_shelf.Items, _workspace.Groups, _settings);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Status($"保存状态失败：{ex.Message}");
        }
    }

    private void Status(string message)
    {
        StatusText.Text = message;
        _statusTimer.Stop();
        _statusTimer.Start();
    }

    private void UpdateClearButton() => ClearButton.Content = _clearArmed ? "确认清空" : "清空";

    // ── 展开 / 收起 ────────────────────────────────────────────────

    private void OnWindowMouseEnter(object sender, MouseEventArgs e)
    {
        _collapseTimer.Stop();
        // 悬浮展开默认关闭（见 HoverExpandEnabled）；按住胶囊是在拖动，更不该被它插进来
        if (HoverExpandEnabled && !_expanded && !_pillPressed)
        {
            _expandTimer.Start();
        }
    }

    private void OnWindowMouseLeave(object sender, MouseEventArgs e)
    {
        _expandTimer.Stop();
        if (_expanded)
        {
            _collapseTimer.Start();
        }
    }

    /// <summary>
    /// 按住胶囊可以拖动。移动超过 4 个 DIP 才算拖动，否则会和"点击展开"抢手势。
    /// </summary>
    private void OnPillMouseDown(object sender, MouseButtonEventArgs e)
    {
        // 关键：先把悬浮展开的计时器掐掉，否则 300ms 后面板会展开、胶囊被隐藏，拖动随即中断
        _expandTimer.Stop();
        _pillPressed = true;
        _pillDragging = false;
        _pillPressPoint = PointToScreen(e.GetPosition(this));
        if (NativeMethods.TryGetWindowRect(_hwnd, out var rect))
        {
            _pillPressLeftPx = rect.Left;
            _pillPressTopPx = rect.Top;
            _pillPressWidthPx = rect.Width;
            _pillPressHeightPx = rect.Height;
        }
        else
        {
            _pillPressLeftPx = (int)Math.Round(_pillPressPoint.X);
            _pillPressTopPx = (int)Math.Round(_pillPressPoint.Y);
            _pillPressWidthPx = (int)Math.Round(CollapsedSize);
            _pillPressHeightPx = (int)Math.Round(CollapsedSize);
        }
        PillPanel.CaptureMouse();
    }

    private void OnPillMouseMove(object sender, MouseEventArgs e)
    {
        if (!_pillPressed || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var now = PointToScreen(e.GetPosition(this));
        var dx = now.X - _pillPressPoint.X;
        var dy = now.Y - _pillPressPoint.Y;
        if (!_pillDragging && Math.Abs(dx) + Math.Abs(dy) < 4)
        {
            return;
        }

        _pillDragging = true;
        var desiredLeft = _pillPressLeftPx + (int)Math.Round(dx);
        var desiredTop = _pillPressTopPx + (int)Math.Round(dy);
        var center = new System.Drawing.Point(
            desiredLeft + _pillPressWidthPx / 2,
            desiredTop + _pillPressHeightPx / 2);
        var screen = System.Windows.Forms.Screen.FromPoint(center);
        var work = screen.WorkingArea;
        var clamped = DisplayPlacement.ClampToWorkArea(
            desiredLeft,
            desiredTop,
            _pillPressWidthPx,
            _pillPressHeightPx,
            work.Left,
            work.Top,
            work.Right,
            work.Bottom);

        if (_hwnd != IntPtr.Zero)
        {
            NativeMethods.TryMoveWindowNoActivate(_hwnd, clamped.Left, clamped.Top);
        }
        else
        {
            Left = clamped.Left;
            Top = clamped.Top;
        }

        _customLeftPx = clamped.Left;
        _customTopPx = clamped.Top;
        _customMonitor = screen.DeviceName;
    }

    private void OnPillClick(object sender, MouseButtonEventArgs e)
    {
        if (PillPanel.IsMouseCaptured)
        {
            PillPanel.ReleaseMouseCapture();
        }

        _pillPressed = false;
        if (_pillDragging)
        {
            // 拖过就不当点击，否则一松手就展开
            _pillDragging = false;
            ScheduleSave();
            return;
        }

        _expandTimer.Stop();
        ExpandSticky();
    }

    private void OnCollapseClick(object sender, RoutedEventArgs e) => Collapse();

    /// <summary>
    /// 收起态悬浮球右键：弹出与托盘同一套动作。
    ///
    /// 不传 includePanelVisibility——球本来就是收起态，「显示中转站 / 收起」在这里毫无意义。
    /// 用 ButtonUp 而不是 ButtonDown：与左键"按下-拖动-抬起"的手势模型保持一致，
    /// 避免按下就弹菜单、随后拖动胶囊时菜单还挂在屏幕上。
    /// </summary>
    private void OnPillRightClick(object sender, MouseButtonEventArgs e)
    {
        // 右键不该打断左键的拖动状态，但要清掉"按住"标记，否则鼠标移回时会被当成还在拖
        _pillPressed = false;
        if (PillPanel.IsMouseCaptured)
        {
            PillPanel.ReleaseMouseCapture();
        }

        _expandTimer.Stop();
        ShowShelfContextMenu(includePanelVisibility: false, includePillHide: true);
        e.Handled = true;
    }

    /// <summary>
    /// 展开态面板空白处右键。落在条目或按钮上时不接管——那些位置有自己的右键菜单
    /// （条目右键菜单见 ShelfWindow.xaml 的 DataTemplate），抢过来会让用户点不到"删除条目"。
    /// </summary>
    private void OnPanelRightClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject source
            && (FindAncestor<ListBoxItem>(source) is not null || FindAncestor<Button>(source) is not null))
        {
            return;
        }

        // 面板已展开，同样不需要「显示中转站 / 收起」
        ShowShelfContextMenu(includePanelVisibility: false);
        e.Handled = true;
    }

    /// <summary>
    /// 弹出统一右键菜单。
    ///
    /// 用 WPF 的 ContextMenu 而不是 WinForms 的 ContextMenuStrip：本窗口是
    /// ShowActivated=False 的不抢焦点窗口，WinForms 菜单需要宿主已激活才能正常接收消息，
    /// 直接 Show 出来会点不中。动作清单与托盘那一份同源（ShelfActions）。
    /// </summary>
    private void ShowShelfContextMenu(bool includePanelVisibility, bool includePillHide = false)
    {
        var menu = new ContextMenu
        {
            PlacementTarget = this,
            Placement = System.Windows.Controls.Primitives.PlacementMode.MousePoint,
        };

        foreach (var entry in ShelfActions.ToWpfItems(this, includePanelVisibility, includePillHide))
        {
            menu.Items.Add(entry);
        }

        menu.IsOpen = true;
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Collapse();
        }
    }

    // ── 拖入 ──────────────────────────────────────────────────────

    private void OnDragEnter(object sender, DragEventArgs e)
    {
        // 自己拖出条目时忽略自身窗口的拖放事件，避免自我接收。
        if (IgnoreSelfDrag(e))
        {
            return;
        }

        // 拖拽经过热区即展开：拖拽过程中用户腾不出手点按钮。
        _expandTimer.Stop();
        _collapseTimer.Stop();
        ExpandSticky();
        SetDropHighlight(true);
        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        if (IgnoreSelfDrag(e))
        {
            return;
        }

        e.Effects = DragDropEffects.Copy;
        e.Handled = true;
    }

    /// <summary>
    /// 拖入高亮：把面板描边换成主色。结束时不能回填"当初存下来的那支画刷"，
    /// 而是重新挂回 StrokeBrush 资源引用——否则运行期换过配色后，描边会留着上一套的颜色。
    /// </summary>
    private void SetDropHighlight(bool active)
    {
        if (active)
        {
            RootBorder.BorderBrush = (Brush)FindResource("DropStrokeBrush");
        }
        else
        {
            RootBorder.SetResourceReference(Border.BorderBrushProperty, "StrokeBrush");
        }
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        SetDropHighlight(false);
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (IgnoreSelfDrag(e))
        {
            return;
        }

        SetDropHighlight(false);

        var added = 0;
        if (!CanCollectInCurrentScope())
        {
            e.Handled = true;
            return;
        }
        if (e.Data.GetDataPresent(DataFormats.FileDrop)
            && e.Data.GetData(DataFormats.FileDrop) is string[] paths)
        {
            foreach (var path in paths)
            {
                _shelf.AddFile(path, CurrentGroupId);
                added++;
            }
        }
        else if (e.Data.GetDataPresent(DataFormats.UnicodeText)
                 && e.Data.GetData(DataFormats.UnicodeText) is string text
                 && !string.IsNullOrWhiteSpace(text))
        {
            _shelf.AddText(text, CurrentGroupId);
            added = 1;
        }

        Status(added > 0 ? $"已放入 {added} 项" : "这个格式暂不支持（demo 仅支持文件与文字）");
        e.Handled = true;
    }

    private bool IgnoreSelfDrag(DragEventArgs e)
    {
        if (!_draggingOut)
        {
            return false;
        }

        e.Effects = DragDropEffects.None;
        e.Handled = true;
        return true;
    }

    // ── 条目操作 ──────────────────────────────────────────────────

    private void OnItemClick(object sender, MouseButtonEventArgs e)
    {
        // 普通模式不留选中痕迹：鼠标按下时 ListBox 会顺手选中条目（多选模式正是靠这个状态），
        // 处理完就清掉，免得卡片上留一圈选中描边。
        if (!_multiSelect && ItemList.SelectedItem is not null)
        {
            ItemList.SelectedItem = null;
        }

        // 刚刚发生的是拖出，不应再触发单击动作。
        if (_suppressClick)
        {
            _suppressClick = false;
            return;
        }

        if (IsOnButton(e.OriginalSource))
        {
            return;
        }

        // 多选模式：勾选在鼠标按下时完成（见 OnItemDragStart），这里不再触发复制 / 打开。
        if (_multiSelect)
        {
            return;
        }

        var item = ItemAt(e.OriginalSource);
        if (item is null)
        {
            return;
        }

        e.Handled = true;

        // 单击主动作：文件夹打开（无"复制内容"语义），其余复制。
        if (item.Kind == ItemKind.Folder)
        {
            Open(item);
        }
        else
        {
            Copy(item);
        }
    }

    private void OnItemDragStart(object sender, MouseButtonEventArgs e)
    {
        _suppressClick = false;

        if (IsOnButton(e.OriginalSource))
        {
            _dragCandidate = null;
            return;
        }

        // 多选模式：左键点条目 = 勾选 / 取消勾选，同时拦掉 ListBox 自带的单选与拖出。
        if (_multiSelect)
        {
            _dragCandidate = null;
            _dragOrigin = null;

            if (ContainerAt(e.OriginalSource) is { } container)
            {
                container.IsSelected = !container.IsSelected;
            }

            e.Handled = true;
            return;
        }

        _dragCandidate = ItemAt(e.OriginalSource);
        _dragOrigin = _dragCandidate is null ? null : e.GetPosition(this);
    }

    private void OnItemDragMove(object sender, MouseEventArgs e)
    {
        if (_dragCandidate is null || _dragOrigin is null || e.LeftButton != MouseButtonState.Pressed)
        {
            return;
        }

        var delta = e.GetPosition(this) - _dragOrigin.Value;
        if (Math.Abs(delta.X) < SystemParameters.MinimumHorizontalDragDistance
            && Math.Abs(delta.Y) < SystemParameters.MinimumVerticalDragDistance)
        {
            return;
        }

        var item = _dragCandidate;
        _dragCandidate = null;
        _dragOrigin = null;
        _suppressClick = true;
        _draggingOut = true;

        try
        {
            var effect = _dragOut.StartDrag(this, item);
            Status(effect == DragDropEffects.None ? "目标没有接收" : "已拖出");
        }
        catch (Exception ex)
        {
            Status($"拖出失败：{ex.Message}");
        }
        finally
        {
            _draggingOut = false;
        }
    }

    private void OnItemDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (IsOnButton(e.OriginalSource))
        {
            return;
        }

        if (_multiSelect)
        {
            return;
        }

        if (ItemAt(e.OriginalSource) is { } item)
        {
            e.Handled = true;
            Open(item);
        }
    }

    private void OnOpenClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            Open(item);
        }
    }

    private void OnCopyClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            Copy(item);
        }
    }

    private void OnRevealClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            Reveal(item);
        }
    }

    private void OnCopyPathClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item)
        {
            return;
        }

        var value = item.Path ?? item.Text ?? item.DisplayName;
        Status(_clipboard.CopyPath(value) ? "已复制路径" : "复制失败，请重试（剪贴板可能被其他程序占用）");
    }

    private void OnDeleteClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is { } item)
        {
            _shelf.Remove(item);
            Status("已移除条目，原始文件未受影响");
        }
    }

    /// <summary>修复乱码显示名（FR-50）。只改架子里的名字，不动磁盘文件。</summary>
    private void OnRepairNameClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item)
        {
            return;
        }

        var suggestion = NameRepair.Suggest(item.DisplayName);
        if (suggestion is null)
        {
            Status("名称看起来正常，没有找到可修复的乱码");
            return;
        }

        item.DisplayName = suggestion.Repaired;
        Status(item.Path is null
            ? $"已修复显示名：{suggestion.Repaired}"
            : "已修复显示名（如需同步文件名，用「重命名源文件为显示名」）");
    }

    /// <summary>把磁盘文件重命名为当前显示名（FR-40）。属于修改用户文件，必须先确认。</summary>
    private void OnRenameSourceClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item)
        {
            return;
        }

        if (item.Path is null)
        {
            Status("该条目没有对应的磁盘文件");
            return;
        }

        if (!Confirm($"把磁盘文件重命名为：\n\n{item.DisplayName}\n\n原文件不会被删除，但位置会改变。继续吗？"))
        {
            return;
        }

        try
        {
            var newPath = _renamer.Rename(item.Path, item.DisplayName);
            item.Path = newPath;
            item.RefreshState();
            Status($"已重命名源文件：{item.DisplayName}");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException)
        {
            Status($"重命名失败：{ex.Message}");
        }
    }

    /// <summary>需要用户确认的弹窗：临时允许窗口激活，避免对话框被挡住导致"看起来卡死"。</summary>
    private bool Confirm(string message)
    {
        NativeMethods.SetActivatable(_hwnd, true);
        NativeMethods.BringToForeground(_hwnd);

        try
        {
            return MessageBox.Show(this, message, "中转站", MessageBoxButton.OKCancel, MessageBoxImage.Question)
                == MessageBoxResult.OK;
        }
        finally
        {
            NativeMethods.SetActivatable(_hwnd, false);
            NativeMethods.ReassertTopmost(_hwnd);
        }
    }

    private void OnClearClick(object sender, RoutedEventArgs e)
    {
        RequestClear(_workspaceActive, CurrentGroupId);
    }

    private void RequestClear(bool workspaceScope, string? groupId)
    {
        var hasItems = _shelf.Items.Any(item =>
            workspaceScope ? item.GroupId == groupId : item.GroupId is null);
        if (!hasItems)
        {
            Status("这里已经是空的");
            return;
        }

        var sameScope = _clearArmed
            && _clearArmedForWorkspace == workspaceScope
            && _clearArmedGroupId == groupId;
        if (!sameScope)
        {
            _clearArmed = true;
            _clearArmedForWorkspace = workspaceScope;
            _clearArmedGroupId = groupId;
            UpdateClearButton();
            _clearTimer.Stop();
            _clearTimer.Start();
            Status(workspaceScope
                ? "再点一次确认清空当前分组（钉住的会保留，原始文件不受影响）"
                : "再点一次确认清空临时区（钉住的会保留，原始文件不受影响）");
            return;
        }

        _clearTimer.Stop();
        _clearArmed = false;
        UpdateClearButton();
        ClearItems(workspaceScope ? groupId : null);
    }

    private void OnCollectClick(object sender, RoutedEventArgs e)
    {
        ShowPanel();
        CollectFromClipboard();
    }

    private void OnAddFilesClick(object sender, RoutedEventArgs e) => AddFilesFromDialog();

    // ── 多选与批量操作（FR-21）────────────────────────────────────

    private void OnMultiSelectClick(object sender, RoutedEventArgs e) => SetMultiSelect(!_multiSelect);

    private void OnBatchExitClick(object sender, RoutedEventArgs e) => SetMultiSelect(false);

    private void SetMultiSelect(bool enabled)
    {
        _multiSelect = enabled;

        if (enabled)
        {
            // 只有 Extended 才允许多选（见模板里的"选中不留下底色"：平时不依赖选中状态）。
            ItemList.SelectionMode = SelectionMode.Extended;
        }
        else
        {
            LeaveMultiSelectMode();
        }

        HeaderActions.Visibility = enabled ? Visibility.Collapsed : Visibility.Visible;
        BatchBar.Visibility = enabled ? Visibility.Visible : Visibility.Collapsed;
        RefreshBatchBar();

        Status(enabled ? "点条目勾选，再点「复制所选内容」" : "已退出多选");
    }

    /// <summary>
    /// 退出多选：必须**先清空选中、再切回单选**。
    /// 单选模式下的 SelectedItems 是只读集合，反过来做会抛 InvalidOperationException
    /// （会让"完成"按钮看起来点了没反应，UIA 验收脚本抓到过）。
    /// </summary>
    private void LeaveMultiSelectMode()
    {
        if (ItemList.SelectionMode != SelectionMode.Single)
        {
            ItemList.SelectedItems.Clear();
        }

        ItemList.SelectionMode = SelectionMode.Single;
    }

    private void OnBatchSelectAllClick(object sender, RoutedEventArgs e)
    {
        if (ItemList.SelectedItems.Count > 0 && ItemList.SelectedItems.Count == ItemList.Items.Count)
        {
            ItemList.SelectedItems.Clear();
        }
        else
        {
            ItemList.SelectAll();
        }

        UpdateBatchBar();
    }

    /// <summary>
    /// 复制勾选项的全部内容：文件同时挂载 CF_HDROP 与路径文本，文字原文按列表顺序保留。
    /// 目标应用可按能力选择文件附件或 UnicodeText，不再静默跳过文字条目。
    /// </summary>
    private void OnBatchCopyPathsClick(object sender, RoutedEventArgs e)
    {
        var selected = CurrentItems.Where(item => ItemList.SelectedItems.Contains(item)).ToList();
        if (selected.Count == 0)
        {
            Status("先点条目勾选，再复制路径清单");
            return;
        }

        var paths = PathListFormatter.Paths(selected);
        var content = PathListFormatter.FormatContents(selected);
        if (string.IsNullOrEmpty(content))
        {
            Status("勾中的条目没有可复制内容");
            return;
        }

        var copied = _clipboard.CopyMixed(paths, content);

        if (!copied)
        {
        Status("复制失败，请重试（剪贴板可能被其他程序占用）");
            return;
        }

        var textCount = selected.Count(item => item.Kind == ItemKind.Text);
        Status(paths.Count == 0
            ? $"已复制 {textCount} 条文字，去目标处 Ctrl+V"
            : textCount == 0
                ? $"已复制 {paths.Count} 条路径，去目标处 Ctrl+V"
                : $"已复制 {paths.Count} 条路径和 {textCount} 条文字，去目标处 Ctrl+V");
    }

    private void UpdateBatchBar()
    {
        // 条目被清空或全部过期后没有可勾选的对象，自动退出，不留一个空的操作条。
        if (_multiSelect && ItemList.Items.Count == 0)
        {
            ExitMultiSelectSilently();
            return;
        }

        RefreshBatchBar();
    }

    /// <summary>静默退出多选：不写状态栏，避免盖掉"已清空 N 项"这类操作结果。</summary>
    private void ExitMultiSelectSilently()
    {
        _multiSelect = false;
        LeaveMultiSelectMode();
        HeaderActions.Visibility = Visibility.Visible;
        BatchBar.Visibility = Visibility.Collapsed;
        RefreshBatchBar();
    }

    private void RefreshBatchBar()
    {
        var selected = ItemList.SelectedItems.Count;
        BatchCountText.Text = $"已选 {selected} 项";
        BatchSelectAllButton.Content =
            selected > 0 && selected == ItemList.Items.Count ? "全不选" : "全选";
    }

    private void OnTogglePinClick(object sender, RoutedEventArgs e)
    {
        if (ItemOf(sender) is not { } item)
        {
            return;
        }

        _shelf.TogglePin(item);
        Status(item.Pinned ? "已钉住：不会被自动清理" : "已取消钉住");
    }

    /// <summary>双击面板空白处收起（窗口不激活，Esc 收不到键盘事件，这里给一个鼠标替代）。</summary>
    private void OnPanelPreviewMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount < 2)
        {
            return;
        }

        if (e.OriginalSource is DependencyObject source
            && (FindAncestor<ListBoxItem>(source) is not null || FindAncestor<Button>(source) is not null))
        {
            return;
        }

        Collapse();
    }

    /// <summary>把剪贴板内容收进架子：文件 &gt; 文字 &gt; 图片（FR-13）。</summary>
    public void CollectFromClipboard()
    {
        if (!CanCollectInCurrentScope()) return;
        var payload = Diagnostics.Measure("读取剪贴板", _clipboard.Read);
        var added = 0;

        if (payload.Files.Count > 0)
        {
            foreach (var file in payload.Files)
            {
                _shelf.AddFile(file, CurrentGroupId);
                added++;
            }
        }
        else if (!string.IsNullOrWhiteSpace(payload.Text))
        {
            _shelf.AddText(payload.Text, CurrentGroupId);
            added = 1;
        }
        else if (payload.Image is not null)
        {
            var cached = _images.SavePng(payload.Image);
            if (cached is not null)
            {
                // 名字精确到秒：只到分钟的话，一分钟内连截两张图会得到完全一样的名字，
                // 列表里两行长得一模一样。跨天也靠日期段区分。
                _shelf.AddCachedImage(cached, $"截图 {DateTime.Now:MMdd-HHmmss}.png", CurrentGroupId);
                added = 1;
            }
        }

        Status(added > 0 ? $"已收录 {added} 项" : "剪贴板里没有可收录的内容");
    }

    /// <summary>手动选择文件加入（FR-14）。</summary>
    public void AddFilesFromDialog()
    {
        if (!CanCollectInCurrentScope()) return;
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Title = "添加到中转站",
            Multiselect = true,
            CheckFileExists = true,
        };

        // 弹系统对话框前临时允许激活并置于前台，否则对话框可能被别的窗口挡住，
        // 而 ShowDialog 已经阻塞了本窗口，看起来就像卡死。
        NativeMethods.SetActivatable(_hwnd, true);
        NativeMethods.BringToForeground(_hwnd);

        bool? result;
        try
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            result = dialog.ShowDialog(this);
            Diagnostics.LogSlow("打开文件选择框", watch.Elapsed);
        }
        finally
        {
            NativeMethods.SetActivatable(_hwnd, false);
            NativeMethods.ReassertTopmost(_hwnd);
        }

        if (result != true)
        {
            return;
        }

        foreach (var file in dialog.FileNames)
        {
            _shelf.AddFile(file, CurrentGroupId);
        }

        Status($"已添加 {dialog.FileNames.Length} 项");
    }

    private void Open(ShelfItem item)
    {
        if (item.Kind == ItemKind.Text)
        {
            Copy(item);
            return;
        }

        try
        {
            _shell.Open(item.Path!);
            Status(item.Kind == ItemKind.Folder ? "已打开文件夹" : "已用默认程序打开");
        }
        catch (Exception ex)
        {
            Status($"打开失败：{ex.Message}");
        }
    }

    private void Copy(ShelfItem item)
    {
        var ok = item.Kind switch
        {
            ItemKind.Text => _clipboard.CopyText(item.Text ?? string.Empty),
            _ => _clipboard.CopyFiles(new[] { item.Path! }),
        };

        // 失败时不再只说"被占用"：写入被拒绝的原因不止占用一种（见技术框架设计 6.5 与 error.log）
        Status(ok ? "已复制，去目标处 Ctrl+V" : "复制失败，请重试（剪贴板可能被其他程序占用）");
    }

    private void Reveal(ShelfItem item)
    {
        if (item.Path is null)
        {
            Copy(item);
            return;
        }

        try
        {
            _shell.Reveal(item.Path);
            Status("已在资源管理器中定位");
        }
        catch (Exception ex)
        {
            Status($"定位失败：{ex.Message}");
        }
    }

    private ListBoxItem? ContainerAt(object? source) =>
        source is DependencyObject dependencyObject
        && ItemsControl.ContainerFromElement(ItemList, dependencyObject) is ListBoxItem container
            ? container
            : null;

    private ShelfItem? ItemAt(object? source) => ContainerAt(source)?.DataContext as ShelfItem;

    private static ShelfItem? ItemOf(object sender) => (sender as FrameworkElement)?.DataContext as ShelfItem;

    private static bool IsOnButton(object? source) =>
        source is DependencyObject dependencyObject && FindAncestor<Button>(dependencyObject) is not null;

    /// <summary>
    /// 沿可视化树向上找祖先；遇到 <see cref="System.Windows.Documents.Run"/> 这类
    /// <see cref="FrameworkContentElement"/>（文字属于逻辑树、不是 Visual）时改用逻辑父节点。
    /// 直接用 VisualTreeHelper 会抛 InvalidOperationException，导致点击文字时操作静默失效。
    /// </summary>
    private static T? FindAncestor<T>(DependencyObject node) where T : DependencyObject
    {
        for (DependencyObject? current = node; current is not null; current = GetParentSafe(current))
        {
            if (current is T match)
            {
                return match;
            }
        }

        return null;
    }

    private static DependencyObject? GetParentSafe(DependencyObject node) => node switch
    {
        Visual or System.Windows.Media.Media3D.Visual3D => VisualTreeHelper.GetParent(node),
        FrameworkContentElement contentElement => contentElement.Parent,
        _ => LogicalTreeHelper.GetParent(node),
    };
}
