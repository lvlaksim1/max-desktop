using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MaxDesktop.App;

/// <summary>Isolated browser window; no MAX login tokens or fabricated signed WebAppData.</summary>
public sealed class MiniAppWindow : Window
{
    private readonly Uri _address;
    private readonly WebView2 _browser = new();
    private readonly TextBlock _status = new()
    {
        Text = "Загрузка мини-приложения...",
        Margin = new Thickness(12, 4, 12, 4),
        TextWrapping = TextWrapping.Wrap
    };

    public MiniAppWindow(Uri address, string caption)
    {
        _address = address;
        Title = "Мини-приложение MAX — " + caption;
        Width = 900;
        Height = 720;
        MinWidth = 480;
        MinHeight = 400;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        var root = new DockPanel();
        var bar = new DockPanel { Margin = new Thickness(8) };
        DockPanel.SetDock(bar, Dock.Top);
        var external = new Button { Content = "Открыть в браузере", Margin = new Thickness(6, 0, 0, 0) };
        DockPanel.SetDock(external, Dock.Right);
        external.Click += (_, _) => Process.Start(new ProcessStartInfo(_address.AbsoluteUri)
        {
            UseShellExecute = true
        });
        bar.Children.Add(external);
        bar.Children.Add(_status);
        root.Children.Add(bar);
        root.Children.Add(_browser);
        Content = root;
        Loaded += Browser_Loaded;
        Closed += (_, _) => _browser.Dispose();
    }

    private async void Browser_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Separate in-private WebView2 profile: do not share authorization
            // cookies with the transport bridge or other MAX accounts.
            string root = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MaxDesktop", "MiniAppWebView2");
            var environment = await CoreWebView2Environment.CreateAsync(userDataFolder: root);
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            await _browser.EnsureCoreWebView2Async(environment, options);
            if (_browser.CoreWebView2 is null) throw new InvalidOperationException("WebView2 не инициализирован.");
            var settings = _browser.CoreWebView2.Settings;
            settings.AreDevToolsEnabled = false;
            settings.AreHostObjectsAllowed = false;
            settings.IsWebMessageEnabled = false;
            _browser.CoreWebView2.PermissionRequested += (_, args) =>
                args.State = CoreWebView2PermissionState.Deny;
            _browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            _browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                _status.Text = args.IsSuccess
                    ? "Страница загружена. Функции, требующие подтверждённые данные MAX, могут быть недоступны."
                    : "Ошибка загрузки страницы: " + args.WebErrorStatus;
            };
            _browser.CoreWebView2.Navigate(_address.AbsoluteUri);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or
            UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _status.Text = "Не удалось открыть мини-приложение: " + ex.GetType().Name;
        }
    }
}
