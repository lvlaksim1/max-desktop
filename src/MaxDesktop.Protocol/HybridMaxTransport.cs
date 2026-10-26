namespace MaxDesktop.Protocol;

public enum MaxConnectionPreference { Automatic, BrowserWebSocket, WebSocket, Tcp }

/// <summary>
/// Selects Chromium WebSocket (when available), .NET WebSocket or legacy TCP
/// without QR login. Network fallback is allowed ONLY before the SMS request
/// could have been sent. An uncertain SMS result must not trigger a duplicate.
/// </summary>
public sealed class HybridMaxTransport : IMaxAuthenticationTransport
{
    private readonly IMaxSessionStore _sessionStore;
    private readonly NativeMaxWebSocketTransport _web;
    private readonly NativeMaxWebSocketTransport? _browser;
    private readonly NativeMaxTransport _tcp;
    private IMaxAuthenticationTransport _active;

    public HybridMaxTransport(IMaxSessionStore store, IMaxWebSocketChannel? browserChannel = null)
    {
        _sessionStore = store ?? throw new ArgumentNullException(nameof(store));
        _web = new NativeMaxWebSocketTransport(store);
        _browser = browserChannel is null ? null
            : new NativeMaxWebSocketTransport(store, browserChannel, "WEB_EDGE");
        _tcp = new NativeMaxTransport(store);
        _active = _browser ?? _web;
    }

    public MaxConnectionPreference PreferredMode { get; set; } = MaxConnectionPreference.Automatic;
    public ConnectionState State => _active.State;
    public string? CurrentPhone => _active.CurrentPhone;

    private IMaxAuthenticationTransport SelectByPreference() => PreferredMode switch
    {
        MaxConnectionPreference.Tcp => _tcp,
        MaxConnectionPreference.WebSocket => _web,
        MaxConnectionPreference.BrowserWebSocket => _browser ?? _web,
        _ => _browser ?? _web
    };

    public async Task<AuthResult> ProbeConnectionAsync(CancellationToken cancellationToken = default)
    {
        _active = SelectByPreference();
        var result = await _active.ProbeConnectionAsync(cancellationToken);
        if (PreferredMode == MaxConnectionPreference.Automatic)
        {
            string label = ReferenceEquals(_active, _browser)
                ? "WebSocket Microsoft Edge" : "WebSocket .NET";
            return result with { Error = label + ": " + (result.Error ?? "Проверка завершена.") };
        }
        return result;
    }

    private static bool FailedBeforeSms(AuthResult result) =>
        result.State == ConnectionState.Failed && (
            result.Error?.StartsWith("WS_CONNECT:", StringComparison.Ordinal) == true ||
            result.Error?.StartsWith("WS_INIT:", StringComparison.Ordinal) == true);

    public async Task<AuthResult> BeginLoginAsync(
        string phoneNumber, CancellationToken cancellationToken = default)
    {
        _active = SelectByPreference();
        AuthResult result = await _active.BeginLoginAsync(phoneNumber, cancellationToken);
        if (PreferredMode != MaxConnectionPreference.Automatic || !FailedBeforeSms(result))
            return result;

        // Do not issue a new SMS request after WS_AUTH_REQUEST, even if the
        // socket was closed and the server's result is unknown.
        var failures = new List<string>
        {
            (ReferenceEquals(_active, _browser) ? "Edge: " : ".NET: ") + result.Error
        };
        if (_browser is not null)
        {
            _active = _web;
            result = await _web.BeginLoginAsync(phoneNumber, cancellationToken);
            if (!FailedBeforeSms(result))
            {
                if (result.State == ConnectionState.Failed)
                    return result with { Error = string.Join("\n", failures) + "\n.NET: " + result.Error };
                return result;
            }
            failures.Add(".NET: " + result.Error);
        }

        _active = _tcp;
        result = await _tcp.BeginLoginAsync(phoneNumber, cancellationToken);
        if (result.State == ConnectionState.Failed)
            return result with { Error = string.Join("\n", failures) + "\nTCP: " + result.Error };
        return result;
    }

    public Task<AuthResult> VerifyCodeAsync(string verificationCode, CancellationToken cancellationToken = default) =>
        _active.VerifyCodeAsync(verificationCode, cancellationToken);
    public Task<AuthResult> VerifyPasswordAsync(string password, CancellationToken cancellationToken = default) =>
        _active.VerifyPasswordAsync(password, cancellationToken);

    public async Task<AuthResult> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        MaxSavedSession? session = _sessionStore.Load();
        if (session is null || string.IsNullOrWhiteSpace(session.LoginToken))
            return new AuthResult(ConnectionState.Disconnected, null);

        _active = session.Transport switch
        {
            "WEB_EDGE" when _browser is not null => _browser,
            "WEB" => _web,
            _ => _tcp
        };
        return await _active.RestoreSessionAsync(cancellationToken);
    }

    public Task LogoutAsync(CancellationToken cancellationToken = default) =>
        _active.LogoutAsync(cancellationToken);
    public Task<IReadOnlyList<ChatSummary>> GetChatsAsync(CancellationToken cancellationToken = default) =>
        _active.GetChatsAsync(cancellationToken);
    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(long chatId, CancellationToken cancellationToken = default) =>
        _active.GetMessagesAsync(chatId, cancellationToken);
    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default) =>
        _active.SendMessageAsync(chatId, text, cancellationToken);

    public void Dispose()
    {
        _browser?.Dispose();
        _web.Dispose();
        _tcp.Dispose();
    }
}
