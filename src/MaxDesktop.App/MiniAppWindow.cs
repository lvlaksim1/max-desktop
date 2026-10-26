 using System.IO;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using MaxDesktop.Protocol;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MaxDesktop.App;

/// <summary>Isolated browser for a server-signed BOT_WEB_APP URL; never disclose the URL.</summary>
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
        // The server-signed URL authenticates the selected account.
        // Never offer to open it in a system browser or expose it in the UI.
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

    // The original Android MAX registers these JS names at document creation.
    // An ordinary Windows host has no Android cellular network. Rather than
    // allowing the JS bridge request to time out silently, it returns only
    // the original Android's "no_cellular" error. No operator call or trust
    // assertion is emulated; the server decides whether SMS can be offered.
    private static string CreateCellularUnavailableBridge(string signedOrigin)
    {
        const string script = """
            (() => {
                'use strict';
                if (window !== window.top || location.origin !== SIGNED_ORIGIN) return;
                const forward = (eventName, body) => {
                    if (eventName !== 'WebAppVerifyMobileId' || typeof body !== 'string' || body.length > 16384) return;
                    let payload;
                    try { payload = JSON.parse(body); } catch { return; }
                    if (!payload || typeof payload.requestId !== 'string') return;
                    window.chrome?.webview?.postMessage({
                        kind: 'mobile-id-no-cellular', requestId: payload.requestId
                    });
                };
                Object.defineProperty(window, 'WebViewHandler', {
                    value: Object.freeze({postEvent(_eventName, _body) {}}),
                    writable: false, configurable: false
                });
                Object.defineProperty(window, 'PrivateWebViewHandler', {
                    value: Object.freeze({postEvent: forward}),
                    writable: false, configurable: false
                });
            })();
            """;
        // Insert only the URL origin: never expose the signed URL, query or
        // fragment (which contain MAX account authentication information).
        return script.Replace("SIGNED_ORIGIN", JsonSerializer.Serialize(signedOrigin),
            StringComparison.Ordinal);
    }

    private async Task HandleMobileIdBridgeMessageAsync(
        CoreWebView2WebMessageReceivedEventArgs args, string signedOrigin)
    {
        if (_closed || _browser.CoreWebView2 is null) return;
        try
        {
            if (!Uri.TryCreate(args.Source, UriKind.Absolute, out Uri? source) ||
                !source.GetLeftPart(UriPartial.Authority).Equals(signedOrigin,
                    StringComparison.OrdinalIgnoreCase))
                return;
            using JsonDocument document = JsonDocument.Parse(args.WebMessageAsJson);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("kind", out JsonElement kind) ||
                kind.GetString() != "mobile-id-no-cellular" ||
                !root.TryGetProperty("requestId", out JsonElement id) ||
                id.ValueKind != JsonValueKind.String ||
                !MaxMiniAppCellularUnavailable.TryCreateResponse(id.GetString(), out string response))
                return;

            // Only forward the explicit negative answer used by the original
            // Android implementation; never construct successful Mobile ID data.
            string js = "window.WebApp?.sendEvent?.(" +
                        JsonSerializer.Serialize(MaxMiniAppCellularUnavailable.EventName) + "," +
                        JsonSerializer.Serialize(response) + ");";
            await _browser.CoreWebView2.ExecuteScriptAsync(js);
            if (!_closed)
                _status.Text = "Сотовая сеть Android на ПК отсутствует; передана штатная ошибка MAX. " +
                               "Если сервер разрешает привязку через СМС для аккаунта с паролем, " +
                               "этот вариант должен предложить сам MAX.";
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or
            ArgumentException or System.Runtime.InteropServices.COMException or ObjectDisposedException)
        {
            // Malformed/untrusted messages must not crash an authenticated
            // mini-app, and must never be written to logs or shown verbatim.
        }
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
            // MAX's Android mini-app uses WebViewHandler/PrivateWebViewHandler.
            // Enable the message channel only inside this isolated mini-app,
            // and accept only the narrow, fail-closed Mobile ID error event.
            settings.IsWebMessageEnabled = true;
            string signedOrigin = _address.GetLeftPart(UriPartial.Authority);
            string bridgeScript = CreateCellularUnavailableBridge(signedOrigin);
            await _browser.CoreWebView2.AddScriptToExecuteOnDocumentCreatedAsync(bridgeScript);
            _browser.CoreWebView2.WebMessageReceived += async (_, args) =>
                await HandleMobileIdBridgeMessageAsync(args, signedOrigin);
            _browser.CoreWebView2.PermissionRequested += (_, args) =>
                args.State = CoreWebView2PermissionState.Deny;
            _browser.CoreWebView2.NewWindowRequested += (_, args) => args.Handled = true;
            _browser.CoreWebView2.NavigationCompleted += (_, args) =>
            {
                _status.Text = args.IsSuccess
                    ? "Адрес выдан MAX. Платформа запуска в URL: " +
                      MaxMiniAppLaunchEnvironment.PlatformDescription(_address) +
                      ". Ограничения доверенного устройства определяет MAX."
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
