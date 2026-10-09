using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TransitShelf.App.Theme;
using TransitShelf.Core.Settings;

namespace TransitShelf.App.Views;

/// <summary>
/// 设置窗口（FR-72 / FR-74 / FR-75 / FR-73 / FR-15）。
///
/// 设计约束：**这个窗口不自己维护任何配置**，所有读写都转发给 <see cref="ShelfWindow"/> 的
/// <c>Set*</c> 方法，由主窗口统一落盘（<c>ScheduleSave</c>）。这样避免两份状态各写一半、
/// 关窗口时互相覆盖；也让"越界钳制"只存在于一处（<see cref="ShelfWindow.SetCapacity"/>）。
///
/// 开关控件在 Loaded 时会被程序赋值，会触发 Checked/Unchecked 事件。必须用
/// <see cref="_loading"/> 挡掉，否则用户只是打开窗口就被当成改了配置、白写一次盘。
/// </summary>
public partial class SettingsWindow : Window
{
    /// <summary>配置与落盘的实际持有者：这个窗口只发指令，不自己保存。</summary>
    private readonly ShelfWindow _owner;

    /// <summary>回填期间为 true，用来区分"用户点的"和"我刚设的"。</summary>
    private bool _loading;

    public SettingsWindow(ShelfWindow owner)
    {
        _owner = owner;
        InitializeComponent();

        Loaded += OnLoad;
        KeyDown += OnWindowKeyDown;
    }

    private void OnLoad(object? sender, RoutedEventArgs e)
    {
        _loading = true;
        try
        {
            var settings = _owner.Settings;
            CapacityBox.Text = settings.SafeCapacity.ToString(CultureInfo.InvariantCulture);
            TimeToLiveBox.Text = settings.TimeToLiveHours.ToString(CultureInfo.InvariantCulture);
            RangeHint.Text = $"合法区间：上限 {ShelfSettings.CapacityMin}～{ShelfSettings.CapacityMax} 项，"
                           + $"时长 {ShelfSettings.TimeToLiveHoursMin}～{ShelfSettings.TimeToLiveHoursMax} 小时。超出会自动修正。";
            ClearOnExitBox.IsChecked = settings.ClearOnExit;
            EnableWorkspaceBox.IsChecked = settings.EnableWorkspace;
            WatchDownloadsBox.IsChecked = settings.WatchDownloads;
            WatchWeChatBox.IsChecked = settings.WatchWeChat;
            RefreshPaletteButtons(settings.Palette);

            // 工作区还没实现：说清楚是"开发中"，而不是让用户以为开了就有（FR-75 分期）。
            WorkspaceNote.Text = "界面开发中，本轮只落盘开关；分组与钉入在下一阶段实现。";
        }
        finally
        {
            _loading = false;
        }

        Hint("改动即时生效，无需重启。");
    }

    private void OnWindowKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key == Key.Escape)
        {
            Close();
        }
    }

    // ── 清理策略（FR-72）──────────────────────────────────────────

    private void OnNumberKeyDown(object sender, KeyEventArgs e)
    {
        // 上下键微调：设置数值最常用的是"差一点"，翻到上个数比选中清空更快。
        if (sender is not TextBox box)
        {
            return;
        }

        var delta = e.Key switch
        {
            Key.Up => 1,
            Key.Down => -1,
            _ => 0,
        };

        if (delta == 0)
        {
            if (e.Key == Key.Enter)
            {
                // Enter 让焦点离开输入框，触发 LostFocus 提交
                box.MoveFocus(new TraversalRequest(FocusNavigationDirection.Next));
            }

            return;
        }

        var current = ParseInt(box.Text);
        box.Text = (current + delta).ToString(CultureInfo.InvariantCulture);
        box.CaretIndex = box.Text.Length;
        e.Handled = true;
    }

    private void OnCapacityLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not TextBox box)
        {
            return;
        }

        if (TryReadInt(box.Text, out var value))
        {
            var clamped = Math.Clamp(value, ShelfSettings.CapacityMin, ShelfSettings.CapacityMax);
            _owner.SetCapacity(clamped);
        }

        // 无条件回填：非法输入（字母、空、超界）都要有确定的落地值，不能留在框里
        box.Text = _owner.Settings.SafeCapacity.ToString(CultureInfo.InvariantCulture);
    }

    private void OnTimeToLiveLostFocus(object sender, RoutedEventArgs e)
    {
        if (_loading || sender is not TextBox box)
        {
            return;
        }

        if (TryReadInt(box.Text, out var value))
        {
            var clamped = Math.Clamp(value, ShelfSettings.TimeToLiveHoursMin, ShelfSettings.TimeToLiveHoursMax);
            _owner.SetTimeToLiveHours(clamped);
        }

        var hours = Math.Clamp(
            _owner.Settings.TimeToLiveHours,
            ShelfSettings.TimeToLiveHoursMin,
            ShelfSettings.TimeToLiveHoursMax);
        box.Text = hours.ToString(CultureInfo.InvariantCulture);
    }

    // ── 开关 ──────────────────────────────────────────────────────

    private void OnClearOnExitChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _owner.SetClearOnExit(ClearOnExitBox.IsChecked == true);
        Hint(ClearOnExitBox.IsChecked == true
            ? "退出时会清空未钉住的条目（钉住的保留）。"
            : "退出时不清空，条目留到下次启动。");
    }

    private void OnEnableWorkspaceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _owner.SetEnableWorkspace(EnableWorkspaceBox.IsChecked == true);
        Hint(EnableWorkspaceBox.IsChecked == true
            ? "已启用工作区，界面将在下一阶段完成后出现。"
            : "已关闭工作区，回到单临时区模式。");
    }

    private void OnWatchChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _owner.SetWatchDownloads(WatchDownloadsBox.IsChecked == true);
        _owner.SetWatchWeChat(WatchWeChatBox.IsChecked == true);
        Hint("自动收录设置已更新。");
    }

    // ── 配色（FR-73）──────────────────────────────────────────────

    private void OnPaletteClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { } button || button.Tag is not string name)
        {
            return;
        }

        _owner.ApplyPalette(name);
        RefreshPaletteButtons(_owner.Settings.Palette);
        Hint("配色：" + PaletteService.DisplayName(_owner.Settings.Palette));
    }

    /// <summary>
    /// 用加粗 + 主色标出当前配色。
    /// 不给按钮加"选中"背景色：底色和卡片同色系，浅色系下几乎看不出差别，
    /// 字重差别在两套配色里都读得出来。
    /// </summary>
    private void RefreshPaletteButtons(string current)
    {
        var active = PaletteService.Normalize(current);
        FrappeButton.FontWeight = active == PaletteService.Frappe ? FontWeights.SemiBold : FontWeights.Normal;
        DawnButton.FontWeight = active == PaletteService.RosePineDawn ? FontWeights.SemiBold : FontWeights.Normal;
    }

    // ── 其他 ──────────────────────────────────────────────────────

    private void OnCloseClick(object sender, RoutedEventArgs e) => Close();

    /// <summary>
    /// 解析输入。只接受纯整数（允许前后空白）：接受 "12abc" 会得到 12，
    /// 用户以为设成 12abc 的效果，其实是静默吞掉了误输入——这里宁可判定为无效。
    /// </summary>
    private static bool TryReadInt(string? text, out int value)
    {
        value = 0;
        var trimmed = (text ?? string.Empty).Trim();
        return int.TryParse(trimmed, NumberStyles.None, CultureInfo.InvariantCulture, out value);
    }

    private static int ParseInt(string? text) =>
        TryReadInt(text, out var value) ? value : 0;

    private void Hint(string message) => HintText.Text = message;
}
