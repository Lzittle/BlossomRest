using System.ComponentModel;
using System.Windows.Data;
using System.Windows.Markup;
using TransitShelf.Core.Localization;

namespace TransitShelf.App.Localization;

/// <summary>绑定到本地文案索引，语言切换时刷新现有窗口。</summary>
public sealed class TextExtension(string key) : MarkupExtension
{
    public override object ProvideValue(IServiceProvider serviceProvider) => new Binding("[" + key + "]")
    {
        Source = TextBinding.Instance,
        Mode = BindingMode.OneWay,
    }.ProvideValue(serviceProvider);
}

public sealed class TextBinding : INotifyPropertyChanged
{
    public static TextBinding Instance { get; } = new();
    private TextBinding() => Loc.LanguageChanged += () => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
    public string this[string key] => Loc.Text(key);
    public event PropertyChangedEventHandler? PropertyChanged;
}
