using System.Windows;

namespace TransitShelf.App.Theme;

/// <summary>
/// 配色切换（FR-73 的"深浅主题"第一步）。
///
/// 设计意图：**换颜色只改一个字典**。所有语义色令牌（SurfaceBrush / AccentBrush / ...）
/// 都由 <c>Resources/Palettes/*.xaml</c> 定义，界面一律用 DynamicResource 取色；
/// 这里只负责把合并字典里的那一个替换掉，控件会自动重绘。
///
/// 加一套新配色：在 Resources/Palettes/ 下新建同令牌名的字典，在 <see cref="All"/> 与
/// <see cref="SourceFor"/> 里登记即可，不需要动任何界面代码。
/// </summary>
public static class PaletteService
{
    public const string Frappe = "Frappe";
    public const string RosePineDawn = "RosePineDawn";

    /// <summary>默认配色：方案一 Catppuccin Frappe。</summary>
    public const string Default = Frappe;

    public static readonly IReadOnlyList<string> All = [Frappe, RosePineDawn];

    /// <summary>当前生效的配色（始终是 <see cref="All"/> 里的某一项）。</summary>
    public static string Current { get; private set; } = Default;

    /// <summary>配色菜单上显示的名字。</summary>
    public static string DisplayName(string? name) => Normalize(name) switch
    {
        RosePineDawn => "Rosé Pine Dawn（浅色）",
        _ => "Catppuccin Frappe（深色）",
    };

    /// <summary>
    /// 下一套配色。只有两套时，托盘里用一条"切换到 X"的条目比藏在下级菜单里更好找；
    /// 将来加到三套以上，这个位置再改成列表式选择。
    /// </summary>
    public static string Next(string? current)
    {
        var active = Normalize(current);
        for (var i = 0; i < All.Count; i++)
        {
            if (string.Equals(All[i], active, StringComparison.OrdinalIgnoreCase))
            {
                return All[(i + 1) % All.Count];
            }
        }

        return All[0];
    }

    /// <summary>把未知/损坏的配置值收敛到已知配色，避免设置文件写错就启动不了。</summary>
    public static string Normalize(string? name)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate, name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return Default;
    }

    /// <summary>整套替换配色字典。传入未知名字时回落到默认配色。</summary>
    public static void Apply(string? name)
    {
        var target = Normalize(name);
        var resources = Application.Current?.Resources;
        if (resources is null)
        {
            Current = target;
            return;
        }

        var palette = new ResourceDictionary
        {
            Source = new Uri(SourceFor(target), UriKind.Relative),
        };

        var merged = resources.MergedDictionaries;
        var index = IndexOfPalette(merged);
        if (index >= 0)
        {
            merged[index] = palette;
        }
        else
        {
            // 理论上不会发生（App.xaml 里已声明）；兜底插到最前面，保证优先级最高。
            merged.Insert(0, palette);
        }

        Current = target;
    }

    private static string SourceFor(string name) => name switch
    {
        RosePineDawn => "Resources/Palettes/RosePineDawn.xaml",
        _ => "Resources/Palettes/Frappe.xaml",
    };

    private static int IndexOfPalette(IList<ResourceDictionary> merged)
    {
        for (var i = 0; i < merged.Count; i++)
        {
            var source = merged[i].Source?.OriginalString;
            if (source is not null && source.Contains("/Palettes/", StringComparison.OrdinalIgnoreCase))
            {
                return i;
            }
        }

        return -1;
    }
}
