using System.Globalization;
using System.Reflection;
using System.Text.Json;

namespace TransitShelf.Core.Localization;

/// <summary>本地文案目录，无 UI、网络或第三方依赖。</summary>
public static class Loc
{
    public sealed record Entry(string Id, string Zh, string En);
    private static readonly IReadOnlyDictionary<string, Entry> Entries = Load();
    private static string _language = LanguagePreference.Chinese;

    public static string Language => _language;
    public static bool IsEnglish => _language == LanguagePreference.English;
    public static IReadOnlyCollection<Entry> Catalog => Entries.Values.ToArray();
    public static event Action? LanguageChanged;

    public static void SetLanguage(string? preference)
    {
        var language = LanguagePreference.Resolve(preference);
        if (_language == language) return;
        _language = language;
        LanguageChanged?.Invoke();
    }

    public static string Text(string id) => TextFor(_language, id);
    public static string TextFor(string? language, string id) => Entries.TryGetValue(id, out var entry)
        ? LanguagePreference.Resolve(language) == LanguagePreference.English ? entry.En : entry.Zh
        : id;
    public static string Format(string id, params object?[] values) => FormatFor(_language, id, values);
    public static string FormatFor(string? language, string id, params object?[] values) => string.Format(
        CultureInfo.GetCultureInfo(LanguagePreference.Resolve(language) == LanguagePreference.English ? "en-US" : "zh-CN"),
        TextFor(language, id), values);

    private static IReadOnlyDictionary<string, Entry> Load()
    {
        using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream("TransitShelf.Core.Localization.Strings.json")
            ?? throw new InvalidOperationException("Localization resources are missing.");
        return (JsonSerializer.Deserialize<List<Entry>>(stream) ?? []).ToDictionary(entry => entry.Id, StringComparer.Ordinal);
    }
}
