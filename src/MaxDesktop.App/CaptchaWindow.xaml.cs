using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Windows;
using Microsoft.Web.WebView2.Core;

namespace MaxDesktop.App;

/// <summary>
/// Human-operated verification only. The VK challenge is rendered inside an
/// isolated WebView2 profile. Neither the challenge URL nor the response token
/// is written to application logs or persisted on disk.
/// </summary>
public partial class CaptchaWindow : Window
{
    private const string BridgeUrl = "https://web.max.ru/max-captcha.html";
    private readonly Uri _challenge;
    public string? CaptchaToken { get; private set; }

    public CaptchaWindow(string challengeUrl)
    {
        if (!Uri.TryCreate(challengeUrl, UriKind.Absolute, out Uri? link) ||
            link.Scheme != Uri.UriSchemeHttps ||
            !(link.Host.Equals("id.vk.ru", StringComparison.OrdinalIgnoreCase) ||
              link.Host.Equals("id.vk.com", StringComparison.OrdinalIgnoreCase)) ||
            !link.AbsolutePath.Contains("not_robot_captcha", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Недопустимый адрес проверки MAX.", nameof(challengeUrl));
        _challenge = link;
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            string folder = Path.Combine(AppContext.BaseDirectory, "Resources");
            if (!File.Exists(Path.Combine(folder, "max-captcha.html")))
                throw new IOException("Страница проверки не установлена.");

            string dataFolder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MaxDesktop", "CaptchaBrowser");
            Directory.CreateDirectory(dataFolder);
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: dataFolder);
            await CaptchaBrowser.EnsureCoreWebView2Async(environment);

            var browser = CaptchaBrowser.CoreWebView2;
            browser.Settings.IsWebMessageEnabled = true;
            browser.Settings.AreHostObjectsAllowed = false;
            browser.Settings.AreDevToolsEnabled = false;
            browser.Settings.AreDefaultContextMenusEnabled = false;
            browser.SetVirtualHostNameToFolderMapping(
                "web.max.ru", folder, CoreWebView2HostResourceAccessKind.DenyCors);
            browser.NavigationStarting += (_, args) =>
            {
                if (!string.Equals(args.Uri, BridgeUrl, StringComparison.OrdinalIgnoreCase))
                    args.Cancel = true;
            };
            browser.WebMessageReceived += Browser_WebMessageReceived;
            browser.Navigate(BridgeUrl);
        }
        catch (Exception ex) when (ex is IOException or COMException or
                                   UnauthorizedAccessException or InvalidOperationException)
        {
            StatusLabel.Text = "Не удалось открыть проверку человека: " + ex.GetType().Name;
        }
    }

    private void Browser_WebMessageReceived(object? sender, CoreWebView2WebMessageReceivedEventArgs e)
    {
        if (!string.Equals(e.Source, BridgeUrl, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            using var document = JsonDocument.Parse(e.WebMessageAsJson);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("bridge", out var marker) ||
                marker.GetString() != "max-captcha-1" ||
                !root.TryGetProperty("type", out var type))
                return;

            switch (type.GetString())
            {
                case "ready":
                    CaptchaBrowser.CoreWebView2.PostWebMessageAsJson(
                        JsonSerializer.Serialize(new { action = "open", url = _challenge.AbsoluteUri }));
                    StatusLabel.Text = "Пройдите проверку в окне выше.";
                    break;
                case "solved":
                    if (root.TryGetProperty("token", out var value) &&
                        value.ValueKind == JsonValueKind.String)
                    {
                        string? token = value.GetString();
                        if (!string.IsNullOrWhiteSpace(token) && token.Length is >= 16 and <= 8192)
                        {
                            CaptchaToken = token;
                            DialogResult = true;
                        }
                    }
                    break;
            }
        }
        catch (JsonException)
        {
            // Ignore malformed untrusted page messages.
        }
    }

    private void Cancel_Click(object sender, RoutedEventArgs e) => DialogResult = false;
}
