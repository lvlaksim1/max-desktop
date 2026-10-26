using System.IO;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Channels;
using System.Windows.Threading;
using MaxDesktop.Protocol;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.Wpf;

namespace MaxDesktop.App;

/// <summary>
/// Browser-network-stack transport for MAX WEB JSON frames, hosted on a
/// tightly scoped local WebView2 document with the WEB Origin. Uses Chromium
/// WebSocket, not SslStream / ClientWebSocket. No QR, no remote page scripts.
/// </summary>
public sealed class EdgeMaxWebSocketChannel : IMaxWebSocketChannel
{
    private const string BridgeTag = "maxdesktop-edge-1";
    private const string LocalHost = "web.max.ru";
    private const string BridgeUrl = "https://web.max.ru/max-bridge.html";
    private readonly WebView2 _control;
    private readonly SemaphoreSlim _initializeGate = new(1, 1);
    private TaskCompletionSource<bool>? _pageReady;
    private TaskCompletionSource<bool>? _connected;
    private Channel<string>? _messages;
    private bool _initialized;
    private bool _isOpen;
    private bool _disposed;
    private int _sessionSequence;
    private int _sessionId;

    public EdgeMaxWebSocketChannel(WebView2 control)
    {
        _control = control ?? throw new ArgumentNullException(nameof(control));
    }

    public bool IsOpen => _isOpen;

    private async Task RunOnUiAsync(Func<Task> action, CancellationToken ct)
    {
        if (_control.Dispatcher.HasShutdownStarted)
            throw new IOException("Окно MAX Desktop уже завершает работу.");
        if (_control.Dispatcher.CheckAccess())
            await action();
        else
            await await _control.Dispatcher.InvokeAsync(action, DispatcherPriority.Normal, ct);
    }

    private async Task RunOnUiAsync(Action action, CancellationToken ct)
    {
        if (_control.Dispatcher.HasShutdownStarted)
            throw new IOException("Окно MAX Desktop уже завершает работу.");
        if (_control.Dispatcher.CheckAccess())
            action();
        else
            await _control.Dispatcher.InvokeAsync(action, DispatcherPriority.Normal, ct);
    }

    private async Task InitializeAsync(CancellationToken ct)
    {
        if (_initialized) return;
        await _initializeGate.WaitAsync(ct);
        try
        {
            if (_initialized) return;
            if (_disposed) throw new ObjectDisposedException(nameof(EdgeMaxWebSocketChannel));
            _pageReady = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
            await RunOnUiAsync(async () =>
            {
                // WebView2 Runtime is usually installed alongside Edge, but
                // must not be assumed to exist on all Windows installations.
                try
                {
                    CoreWebView2Environment.GetAvailableBrowserVersionString();
                }
                catch (WebView2RuntimeNotFoundException e)
                {
                    throw new IOException("Не установлен Microsoft Edge WebView2 Runtime.", e);
                }

                string folder = Path.Combine(AppContext.BaseDirectory, "Resources");
                string html = Path.Combine(folder, "max-bridge.html");
                if (!File.Exists(html))
                    throw new FileNotFoundException("Не найден внутренний модуль WebSocket.", html);
                string userFolder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "MaxDesktop", "WebSocketBrowser");
                Directory.CreateDirectory(userFolder);
                var environment = await CoreWebView2Environment.CreateAsync(
                    userDataFolder: userFolder);
                await _control.EnsureCoreWebView2Async(environment);
                var browser = _control.CoreWebView2;
                browser.Settings.IsWebMessageEnabled = true;
                browser.Settings.AreDevToolsEnabled = false;
                browser.Settings.AreDefaultContextMenusEnabled = false;
                browser.Settings.AreHostObjectsAllowed = false;
                browser.Settings.IsStatusBarEnabled = false;
                browser.WebMessageReceived -= OnWebMessage;
                browser.WebMessageReceived += OnWebMessage;
                browser.NavigationStarting -= OnNavigationStarting;
                browser.NavigationStarting += OnNavigationStarting;
                browser.NavigationCompleted -= OnNavigationCompleted;
                browser.NavigationCompleted += OnNavigationCompleted;
                browser.SetVirtualHostNameToFolderMapping(
                    LocalHost, folder, CoreWebView2HostResourceAccessKind.DenyCors);
                browser.Navigate(BridgeUrl);
            }, ct);
            await _pageReady.Task.WaitAsync(TimeSpan.FromSeconds(25), ct);
            _initialized = true;
        }
        catch (Exception ex) when (ex is COMException or UnauthorizedAccessException)
        {
            throw new IOException(
                $"Не удалось создать сетевой модуль Edge (0x{ex.HResult:X8}).", ex);
        }
        finally { _initializeGate.Release(); }
    }

    private void OnNavigationStarting(object? sender, CoreWebView2NavigationStartingEventArgs e)
    {
        if (!string.Equals(e.Uri, BridgeUrl, StringComparison.OrdinalIgnoreCase))
            e.Cancel = true;
    }

    private void OnNavigationCompleted(object? sender, CoreWebView2NavigationCompletedEventArgs e)
    {
        if (!e.IsSuccess)
            _pageReady?.TrySetException(new IOException(
                $"Внутренняя страница Edge недоступна ({e.WebErrorStatus})."));
    }

    private void OnWebMessage(object? sender, CoreWebView2WebMessageReceivedEventArgs args)
    {
        if (!string.Equals(args.Source, BridgeUrl, StringComparison.OrdinalIgnoreCase))
            return;
        try
        {
            using var doc = JsonDocument.Parse(args.WebMessageAsJson);
            var root = doc.RootElement;
            if (!root.TryGetProperty("bridge", out var marker) ||
                marker.GetString() != BridgeTag ||
                !root.TryGetProperty("type", out var kind) ||
                kind.ValueKind != JsonValueKind.String)
                return;
            string? type = kind.GetString();
            if (type == "ready")
            {
                _pageReady?.TrySetResult(true);
                return;
            }
            if (!root.TryGetProperty("id", out var id) ||
                !id.TryGetInt32(out int session) || session != _sessionId)
                return;

            switch (type)
            {
                case "open":
                    _isOpen = true;
                    _connected?.TrySetResult(true);
                    break;
                case "message":
                    if (root.TryGetProperty("data", out var data) &&
                        data.ValueKind == JsonValueKind.String)
                    {
                        string? text = data.GetString();
                        if (text is { Length: <= 2097152 })
                            _messages?.Writer.TryWrite(text);
                    }
                    break;
                case "error":
                    _isOpen = false;
                    var networkError = new IOException(
                        "Chromium WebSocket не установил соединение; ошибка сети или TLS.");
                    _connected?.TrySetException(networkError);
                    _messages?.Writer.TryComplete(networkError);
                    break;
                case "closed":
                    _isOpen = false;
                    int closeCode = root.TryGetProperty("code", out var c) &&
                        c.TryGetInt32(out int value) ? value : 0;
                    var closedError = new IOException($"Chromium закрыл WebSocket (код {closeCode}).");
                    _connected?.TrySetException(closedError);
                    _messages?.Writer.TryComplete(closedError);
                    break;
            }
        }
        catch (JsonException) { /* Discard malformed/untrusted page messages. */ }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(EdgeMaxWebSocketChannel));
        await DisconnectAsync();
        await InitializeAsync(cancellationToken);
        _sessionId = Interlocked.Increment(ref _sessionSequence);
        _isOpen = false;
        _connected = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        _messages = Channel.CreateBounded<string>(new BoundedChannelOptions(128)
        {
            SingleReader = true,
            SingleWriter = true,
            FullMode = BoundedChannelFullMode.DropOldest
        });
        await RunOnUiAsync(() =>
        {
            _control.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                bridge = BridgeTag,
                action = "connect",
                id = _sessionId
            }));
        }, cancellationToken);

        try
        {
            await _connected.Task.WaitAsync(TimeSpan.FromSeconds(20), cancellationToken);
        }
        catch (TimeoutException ex)
        {
            throw new IOException("Chromium не подтвердил WebSocket-соединение за 20 секунд.", ex);
        }
    }

    public async Task SendTextAsync(string text, CancellationToken cancellationToken)
    {
        if (!_isOpen) throw new IOException("Chromium WebSocket не подключён.");
        int id = _sessionId;
        await RunOnUiAsync(() =>
        {
            _control.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
            {
                bridge = BridgeTag,
                action = "send",
                id,
                data = text
            }));
        }, cancellationToken);
    }

    public async Task<string> ReceiveTextAsync(CancellationToken cancellationToken)
    {
        if (_messages is not { } messages)
            throw new IOException("Chromium WebSocket не инициализирован.");
        try { return await messages.Reader.ReadAsync(cancellationToken); }
        catch (ChannelClosedException ex)
        {
            throw new IOException("Соединение Chromium WebSocket закрыто.", ex);
        }
    }

    public async Task DisconnectAsync()
    {
        int previous = _sessionId;
        _sessionId = Interlocked.Increment(ref _sessionSequence);
        _isOpen = false;
        _messages?.Writer.TryComplete();
        _messages = null;
        _connected?.TrySetCanceled();
        _connected = null;
        if (!_initialized || _disposed || _control.Dispatcher.HasShutdownStarted) return;
        try
        {
            await RunOnUiAsync(() =>
            {
                _control.CoreWebView2.PostWebMessageAsJson(JsonSerializer.Serialize(new
                {
                    bridge = BridgeTag,
                    action = "close",
                    id = previous
                }));
            }, CancellationToken.None);
        }
        catch (Exception ex) when (ex is InvalidOperationException or COMException)
        {
            // Window/runtime may already be shutting down.
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _isOpen = false;
        _messages?.Writer.TryComplete();
        _connected?.TrySetCanceled();
        // The WebView2 control is owned by MainWindow and dies with it.
        _initializeGate.Dispose();
    }
}
