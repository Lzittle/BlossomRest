using System.Windows;
using System.Windows.Controls;
using TransitShelf.App.Theme;
using TransitShelf.App.Views;

namespace TransitShelf.App;

/// <summary>
/// 中转站右键菜单的**单一动作来源**。
///
/// 托盘（WinForms ContextMenuStrip）与悬浮球/面板（WPF ContextMenu）需要两种不同的菜单控件，
/// 控件类没法共用，但"有哪些动作、什么文案、什么顺序、点了干什么"必须共用——
/// 否则加一项就要改两处，两边迟早走偏。
///
/// 所以这里只描述动作清单，由调用方翻译成各自的菜单控件：
/// <list type="bullet">
/// <item><see cref="AppendTo"/> —— 翻译成 WinForms 菜单项；</item>
/// <item><see cref="Build"/> —— 翻译成 WPF 菜单项。</item>
/// </list>
/// </summary>
internal static class ShelfActions
{
    /// <summary>一个菜单动作（分隔线用 <see cref="IsSeparator"/> 表示）。</summary>
    internal sealed record MenuItemSpec(
        string Text,
        System.Action? Invoke,
        bool IsSeparator = false,
        Func<bool>? IsChecked = null,
        Func<bool>? IsEnabled = null)
    {
        internal static MenuItemSpec Separator { get; } = new(string.Empty, null, IsSeparator: true);
    }

    /// <summary>
    /// 构建动作清单。
    ///
    /// <paramref name="includePanelVisibility"/> 为 true 时带上「显示中转站 / 收起」——
    /// 只对托盘有意义：托盘是"面板可能被盖住或已收起"时唯一的兜底入口。
    /// 悬浮球（本来就是收起态）与已展开的面板都不需要这两项。
    /// </summary>
    internal static List<MenuItemSpec> Build(ShelfWindow window, bool includePanelVisibility)
    {
        var actions = new List<MenuItemSpec>
        {
            new("收录剪贴板内容", () => window.CollectFromClipboard()),
            new("添加文件…", () => window.AddFilesFromDialog()),
            MenuItemSpec.Separator,

            // 自动收录开关（默认关闭，见红线 R6）。勾选态每次展开时重取，
            // 避免菜单固化在构建那一刻、与设置窗口里的实际值不一致。
            new("自动收录下载目录",
                () => window.SetWatchDownloads(!window.Settings.WatchDownloads),
                IsChecked: () => window.Settings.WatchDownloads),
            new("自动收录微信接收目录",
                () => window.SetWatchWeChat(!window.Settings.WatchWeChat),
                IsChecked: () => window.Settings.WatchWeChat),
            MenuItemSpec.Separator,

            // 设置：清理策略、退出清空、工作区开关都收进独立窗口（FR-72 / FR-74 / FR-75）。
            new("设置…", () => window.ShowSettings()),
            MenuItemSpec.Separator,

            // 配色（FR-73）：一条"切换到 X"的条目，直接写出目标方案名——
            // 之前做成下级菜单，用户根本找不到（实机反馈）。
            new(PaletteText(window), () => window.ApplyPalette(PaletteService.Next(window.Settings.Palette))),
        };

        if (includePanelVisibility)
        {
            // 「显示/收起」互斥，用可用性表达：已经展开时"显示"没有效果，置灰比点了没反应好。
            actions.Add(new("显示中转站", () => window.ShowPanel(), IsEnabled: () => !window.IsPanelExpanded));
            actions.Add(new("收起", () => window.CollapsePanel(), IsEnabled: () => window.IsPanelExpanded));
        }

        actions.Add(new("清空临时区", () => window.ClearAll()));
        actions.Add(MenuItemSpec.Separator);
        actions.Add(new("退出", () => Application.Current.Shutdown()));
        return actions;
    }

    /// <summary>「切换配色：X」的文案。当前方案由 <see cref="ShelfWindow.Settings"/> 提供。</summary>
    private static string PaletteText(ShelfWindow window)
    {
        var active = PaletteService.Normalize(window.Settings.Palette);
        return "切换配色：" + PaletteService.DisplayName(PaletteService.Next(active));
    }

    /// <summary>翻译成 WPF 菜单项（悬浮球 / 面板右键用）。</summary>
    internal static IEnumerable<object> ToWpfItems(ShelfWindow window, bool includePanelVisibility)
    {
        foreach (var action in Build(window, includePanelVisibility))
        {
            if (action.IsSeparator)
            {
                yield return new Separator();
                continue;
            }

            var item = new MenuItem { Header = action.Text };
            var invoke = action.Invoke;
            item.Click += (_, _) => invoke?.Invoke();

            if (action.IsChecked is { } isChecked)
            {
                item.IsCheckable = true;
                item.IsChecked = isChecked();
            }

            if (action.IsEnabled is { } enabled)
            {
                item.IsEnabled = enabled();
            }

            yield return item;
        }
    }

    /// <summary>翻译成 WinForms 菜单项（托盘用）。</summary>
    internal static void AppendTo(
        System.Windows.Forms.ContextMenuStrip menu,
        ShelfWindow window,
        bool includePanelVisibility)
    {
        foreach (var action in Build(window, includePanelVisibility))
        {
            if (action.IsSeparator)
            {
                menu.Items.Add(new System.Windows.Forms.ToolStripSeparator());
                continue;
            }

            var item = new System.Windows.Forms.ToolStripMenuItem(action.Text);
            var invoke = action.Invoke;
            item.Click += (_, _) => invoke?.Invoke();

            if (action.IsChecked is { } isChecked)
            {
                item.CheckOnClick = true;
                item.Checked = isChecked();
            }

            if (action.IsEnabled is { } enabled)
            {
                item.Enabled = enabled();
            }

            menu.Items.Add(item);
        }

        // 每次展开前重新同步勾选/可用性——用户可能在设置窗口改过开关。
        menu.Opening += (_, _) =>
        {
            var items = menu.Items.OfType<System.Windows.Forms.ToolStripMenuItem>().ToList();
            var source = Build(window, includePanelVisibility)
                .Where(a => !a.IsSeparator)
                .ToList();

            for (var index = 0; index < items.Count && index < source.Count; index++)
            {
                var action = source[index];
                if (action.IsChecked is { } isChecked)
                {
                    items[index].Checked = isChecked();
                }

                if (action.IsEnabled is { } enabled)
                {
                    items[index].Enabled = enabled();
                }

                // 配色项的文案是「切换到下一个」，改过配色后文案要跟着走
                items[index].Text = action.Text;
            }
        };
    }
}