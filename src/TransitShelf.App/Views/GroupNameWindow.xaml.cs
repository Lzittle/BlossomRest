using System.Windows;
using System.Windows.Controls;

namespace TransitShelf.App.Views;

/// <summary>分组名输入窗口；显式编辑时允许键盘焦点，浮窗本身仍不激活。</summary>
public partial class GroupNameWindow : Window
{
    public GroupNameWindow(string title, string initialName)
    {
        InitializeComponent();
        Title = title;
        NameBox.Text = initialName;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
    }

    public string GroupName => NameBox.Text.Trim();

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (SaveButton is not null) SaveButton.IsEnabled = !string.IsNullOrWhiteSpace(NameBox.Text);
    }

    private void OnSaveClick(object sender, RoutedEventArgs e) => DialogResult = true;
}
