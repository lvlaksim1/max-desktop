using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Media;
using Microsoft.Win32;

namespace MaxDesktop.App;

public sealed class UserSettings
{
    public string Theme { get; set; } = "Системная";
    public string Accent { get; set; } = "#2876C7";
    public double WindowWidth { get; set; } = 1240;
    public double WindowHeight { get; set; } = 780;
    public double LeftPaneWidth { get; set; } = 215;
    public double ChatPaneWidth { get; set; } = 320;
    public double? WindowLeft { get; set; }
    public double? WindowTop { get; set; }
}

public static class SettingsStore
{
    public static string DataRoot => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "MaxDesktop");
    private static string SettingsPath => Path.Combine(DataRoot, "settings.json");

    public static UserSettings Load()
    {
        try
        {
            if (File.Exists(SettingsPath))
                return JsonSerializer.Deserialize<UserSettings>(File.ReadAllText(SettingsPath)) ?? new UserSettings();
        }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        catch (JsonException) { }
        return new UserSettings();
    }

    public static void Save(UserSettings settings)
    {
        Directory.CreateDirectory(DataRoot);
        string temporary = SettingsPath + ".tmp";
        File.WriteAllText(temporary, JsonSerializer.Serialize(settings, new JsonSerializerOptions { WriteIndented = true }));
        File.Move(temporary, SettingsPath, true);
    }

    public static void ApplyTheme(UserSettings settings)
    {
        bool systemDark = false;
        try
        {
            using RegistryKey? key = Registry.CurrentUser.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\Themes\Personalize");
            systemDark = Convert.ToInt32(key?.GetValue("AppsUseLightTheme", 1)) == 0;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or FormatException)
        {
            systemDark = false;
        }
        bool dark = settings.Theme == "Тёмная" || (settings.Theme == "Системная" && systemDark);
        SetBrush("WindowBrush", dark ? "#17212D" : "#F7F9FC");
        SetBrush("PanelBrush", dark ? "#1E2B3A" : "#FFFFFF");
        SetBrush("TextBrush", dark ? "#E7EDF5" : "#18283A");
        SetBrush("MutedBrush", dark ? "#AAB9CA" : "#627287");
        SetBrush("LineBrush", dark ? "#3B4A5B" : "#DDE4ED");
        SetBrush("InputBrush", dark ? "#28394A" : "#F2F5FA");
        SetBrush("AccentBrush", settings.Accent);
    }

    private static void SetBrush(string key, string value)
    {
        Color color;
        try { color = (Color)ColorConverter.ConvertFromString(value); }
        catch (FormatException) { color = Color.FromRgb(40, 118, 199); }
        Application.Current.Resources[key] = new SolidColorBrush(color);
    }
}
