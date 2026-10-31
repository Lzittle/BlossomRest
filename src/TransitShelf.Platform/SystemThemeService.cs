using Microsoft.Win32;

namespace TransitShelf.Platform;

/// <summary>
/// 当前用户的系统浅色 / 深色主题（FR-73「深浅主题跟随系统」）。
///
/// 只读 <c>HKCU\Software\Microsoft\Windows\CurrentVersion\Themes\Personalize</c> 下的
/// <c>AppsUseLightTheme</c>：1 = 浅色，0 = 深色。读不到（旧版本系统或键被策略移除）时
/// 按深色处理，与默认配色一致；本服务只读不写，不修改系统主题。
/// </summary>
public static class SystemThemeService
{
    /// <summary>「设置 → 个性化 → 颜色」写入的当前用户键。</summary>
    private const string PersonalizeKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize";

    private const string ValueName = "AppsUseLightTheme";

    /// <summary>系统当前是否使用浅色主题。</summary>
    public static bool IsLightTheme()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(PersonalizeKeyPath);
            return IsLightThemeValue(key?.GetValue(ValueName));
        }
        catch (Exception)
        {
            // 注册表读取受限时降级为深色，不抛出。
            return false;
        }
    }

    /// <summary>
    /// 注册表值的纯解析，便于单测。只有**非零整数**才算浅色：
    /// 键不存在（null）、类型不对（字符串等）都按深色处理。
    /// </summary>
    public static bool IsLightThemeValue(object? value) => value is int flag && flag != 0;
}
