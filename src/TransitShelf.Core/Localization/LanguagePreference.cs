using System.Globalization;

namespace TransitShelf.Core.Localization;

/// <summary>语言偏好只影响程序文案，不改变条目内容。</summary>
public static class LanguagePreference
{
    public const string Automatic = "auto";
    public const string Chinese = "zh-CN";
    public const string English = "en";

    public static string Normalize(string? preference) => preference?.Trim().ToLowerInvariant() switch
    {
        "zh" or "zh-cn" or "zh-hans" => Chinese,
        "en" or "en-us" or "en-gb" => English,
        _ => Automatic,
    };

    public static string Resolve(string? preference, string? systemLanguage = null)
    {
        var normalized = Normalize(preference);
        if (normalized != Automatic) return normalized;
        return (systemLanguage ?? CultureInfo.CurrentUICulture.Name).StartsWith("zh", StringComparison.OrdinalIgnoreCase)
            ? Chinese : English;
    }
}
