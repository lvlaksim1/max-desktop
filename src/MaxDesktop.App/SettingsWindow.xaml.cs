using System.Windows;
using System.Windows.Controls;

namespace MaxDesktop.App;

public partial class SettingsWindow : Window
{
    private readonly UserSettings _settings;

    public SettingsWindow(UserSettings settings)
    {
        InitializeComponent();
        _settings = settings;
        ThemePicker.SelectedIndex = settings.Theme switch
        {
            "Светлая" => 1,
            "Тёмная" => 2,
            _ => 0
        };
        for (int i = 0; i < ColorPicker.Items.Count; i++)
        {
            if (ColorPicker.Items[i] is ComboBoxItem item && Equals(item.Tag, settings.Accent))
            {
                ColorPicker.SelectedIndex = i;
                break;
            }
        }
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _settings.Theme = (ThemePicker.SelectedItem as ComboBoxItem)?.Content?.ToString() ?? "Системная";
        _settings.Accent = (ColorPicker.SelectedItem as ComboBoxItem)?.Tag?.ToString() ?? "#2876C7";
        SettingsStore.ApplyTheme(_settings);
        SettingsStore.Save(_settings);
        DialogResult = true;
    }
}
