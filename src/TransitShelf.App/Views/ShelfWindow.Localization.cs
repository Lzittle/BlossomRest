using System.Windows;
using TransitShelf.Core.Localization;

namespace TransitShelf.App.Views;

public partial class ShelfWindow
{
    public void SetLanguage(string language)
    {
        _settings.Language = LanguagePreference.Normalize(language);
        Loc.SetLanguage(_settings.Language);
        ScheduleSave();
    }

    private void RefreshLanguage()
    {
        foreach (var item in _shelf.Items) item.NotifyLanguageChanged();
        RefreshWorkspace();
        UpdateClearButton();
        FilterTypeButton.Content = FilterKindLabel(_filterKind);
        ApplyLayout();
        StatusText.Text = string.Empty;
    }
}
