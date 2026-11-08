using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using TransitShelf.App.Theme;
using TransitShelf.Core;
using TransitShelf.Core.Settings;
using TransitShelf.Platform;

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
            RangeHint.Text = Loc.Format("T26B7FE1FC1BF", ShelfSettings.CapacityMin, ShelfSettings.CapacityMax)
                           + Loc.Format("TE484BD72C004", ShelfSettings.TimeToLiveHoursMin, ShelfSettings.TimeToLiveHoursMax);
            ClearOnExitBox.IsChecked = settings.ClearOnExit;
            // 自启状态来自注册表而不是 state.json：用户在系统设置里关掉后，这里要如实显示关。
            StartWithWindowsBox.IsChecked = StartupService.IsEnabled();
            EnableWorkspaceBox.IsChecked = settings.EnableWorkspace;
            WatchDownloadsBox.IsChecked = settings.WatchDownloads;
            WatchWeChatBox.IsChecked = settings.WatchWeChat;
            RefreshPaletteButtons(settings.Palette);
            RefreshClickBehaviorButtons(settings.ClickBehavior);
            AutoLanguageButton.FontWeight = settings.Language == LanguagePreference.Automatic ? FontWeights.SemiBold : FontWeights.Normal;
            ChineseLanguageButton.FontWeight = settings.Language == LanguagePreference.Chinese ? FontWeights.SemiBold : FontWeights.Normal;
            EnglishLanguageButton.FontWeight = settings.Language == LanguagePreference.English ? FontWeights.SemiBold : FontWeights.Normal;
            Width = Loc.IsEnglish ? 600 : 520;

            WorkspaceNote.Text = Loc.Text("T692989EB1DE7");
        }
        finally
        {
            _loading = false;
        }

        Hint(Loc.Text("TADFF1D8892C5"));
    }

    private void OnLanguageClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string language }) return;
        _owner.SetLanguage(language);
        OnLoad(this, new RoutedEventArgs());
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
            ? Loc.Text("T490A7B5C347E")
            : Loc.Text("TA4EAD4C7BB5F"));
    }

    /// <summary>
    /// 开机自启（FR-73）。开关的最终状态以注册表为准：
    /// 写入失败（安全软件拦截 / 组策略）时把开关回填成真实状态，不能让界面说一套、系统里是另一套。
    /// </summary>
    private void OnStartWithWindowsChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        var enabled = StartWithWindowsBox.IsChecked == true;
        if (!_owner.SetStartWithWindows(enabled))
        {
            _loading = true;
            StartWithWindowsBox.IsChecked = StartupService.IsEnabled();
            _loading = false;
            Hint(Loc.Text("T9BDF2F70E4B0"));
            return;
        }

        Hint(enabled
            ? Loc.Text("TE8EA4E67BC7A")
            : Loc.Text("T90F467689BD6"));
    }

    private void OnEnableWorkspaceChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _owner.SetEnableWorkspace(EnableWorkspaceBox.IsChecked == true);
        Hint(EnableWorkspaceBox.IsChecked == true
            ? Loc.Text("T80AD82C383FF")
            : Loc.Text("T4929FDA5A0FB"));
    }

    private void OnWatchChanged(object sender, RoutedEventArgs e)
    {
        if (_loading)
        {
            return;
        }

        _owner.SetWatchDownloads(WatchDownloadsBox.IsChecked == true);
        _owner.SetWatchWeChat(WatchWeChatBox.IsChecked == true);
        Hint(Loc.Text("TBBC9BB5B951F"));
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
        Hint(Loc.Text("T39364D84BE9A") + PaletteService.DisplayName(_owner.Settings.Palette));
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
        SystemButton.FontWeight = active == PaletteService.System ? FontWeights.SemiBold : FontWeights.Normal;
    }

    // ── 单击行为（FR-70）────────────────────────────────────────────

    private void OnClickBehaviorClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button { Tag: string name }
            || !Enum.TryParse<ShelfClickBehavior>(name, out var behavior))
        {
            return;
        }

        _owner.SetClickBehavior(behavior);
        RefreshClickBehaviorButtons(behavior);
        Hint(behavior switch
        {
            ShelfClickBehavior.AlwaysOpen => Loc.Text("TEEC3D62C707A"),
            ShelfClickBehavior.AlwaysCopy => Loc.Text("TE5F1E0FFFC8F"),
            _ => Loc.Text("T3154B5EAA176"),
        });
    }

    /// <summary>
    /// 与配色按钮同一套标记法：加粗当前项。三个选项互斥，不加选中背景。
    /// </summary>
    private void RefreshClickBehaviorButtons(ShelfClickBehavior current)
    {
        ByKindButton.FontWeight = current == ShelfClickBehavior.ByKind ? FontWeights.SemiBold : FontWeights.Normal;
        AlwaysOpenButton.FontWeight = current == ShelfClickBehavior.AlwaysOpen ? FontWeights.SemiBold : FontWeights.Normal;
        AlwaysCopyButton.FontWeight = current == ShelfClickBehavior.AlwaysCopy ? FontWeights.SemiBold : FontWeights.Normal;
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
