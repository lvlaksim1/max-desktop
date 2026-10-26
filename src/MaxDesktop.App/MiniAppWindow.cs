using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using MaxDesktop.Protocol;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MaxDesktop.App;

/// <summary>Isolated browser window; no MAX login tokens or fabricated signed WebAppData.</summary>
public sealed class MiniAppWindow : Window
{
    private readonly Uri _address;
    private readonly WebView2 _browser = new();
    private MiniAppNetworkProxy? _networkProxy;
    private string? _profileDirectory;
    private bool _closed;
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
        var external = new System.Windows.Controls.Button { Content = "Открыть в обычном браузере", Margin = new Thickness(6, 0, 0, 0) };
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
        Closed += (_, _) =>
        {
            _closed = true;
            _browser.Dispose();
            _networkProxy?.Dispose();
            _networkProxy = null;
            if (_profileDirectory is not null)
            {
                // A WebView2 process can briefly retain handles after Dispose.
                // A leftover empty InPrivate profile is not an authenticated MAX session.
                try { Directory.Delete(_profileDirectory, recursive: true); }
                catch (IOException) { }
                catch (UnauthorizedAccessException) { }
            }
        };
    }

    private async void Browser_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            // Unlike MAX's authenticated socket, default WebView2 went through
            // the blocked system VPN route, causing ERR_CONNECTION_CLOSED.
            // This loopback-only proxy binds MAX hosts to the physical interface;
            // other hosts use the configured system proxy / ordinary OS route.
            _networkProxy = new MiniAppNetworkProxy();
            _profileDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "MaxDesktop", "MiniAppWebView2", Guid.NewGuid().ToString("N"));
            var browserOptions = new CoreWebView2EnvironmentOptions
            {
                AdditionalBrowserArguments =
                    $"--proxy-server=http://127.0.0.1:{_networkProxy.Port} --proxy-bypass-list=<-loopback>"
            };
            var environment = await CoreWebView2Environment.CreateAsync(
                userDataFolder: _profileDirectory, options: browserOptions);
            if (_closed) return;
            var options = environment.CreateCoreWebView2ControllerOptions();
            options.IsInPrivateModeEnabled = true;
            await _browser.EnsureCoreWebView2Async(environment, options);
            if (_closed) return;
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
                    ? "Страница загружена через отдельный сетевой маршрут. Авторизация мини-приложения MAX пока не подтверждена."
                    : "Ошибка загрузки страницы: " + args.WebErrorStatus +
                      ". Для адресов MAX используется физическое подключение; проверьте доступность сайта.";
            };
            _browser.CoreWebView2.Navigate(_address.AbsoluteUri);
        }
        catch (Exception ex) when (ex is InvalidOperationException or IOException or
            UnauthorizedAccessException or System.Runtime.InteropServices.COMException)
        {
            _status.Text = "Не удалось открыть мини-приложение: " + ex.GetType().Name +
                ". Системный VPN и настройки Windows не изменены.";
        }
    }
}
