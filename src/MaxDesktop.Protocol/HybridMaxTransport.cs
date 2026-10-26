namespace MaxDesktop.Protocol;

public enum MaxConnectionPreference { Automatic, BrowserWebSocket, WebSocket, Tcp, PhysicalWebSocket }

/// <summary>
/// Selects Chromium WebSocket (when available), .NET WebSocket or legacy TCP
/// without QR login. Network fallback is allowed ONLY before the SMS request
/// could have been sent. An uncertain SMS result must not trigger a duplicate.
/// </summary>
public sealed class HybridMaxTransport : IMaxAuthenticationTransport
{
    private readonly IMaxSessionStore _sessionStore;
    private readonly NativeMaxWebSocketTransport _web;
    private readonly NativeMaxWebSocketTransport _physical;
    private readonly NativeMaxWebSocketTransport? _browser;
    private readonly NativeMaxTransport _tcp;
    private IMaxAuthenticationTransport _active;

    public HybridMaxTransport(IMaxSessionStore store, IMaxWebSocketChannel? browserChannel = null,
        IMaxWebSocketChannel? physicalChannel = null)
    {
        _sessionStore = store ?? throw new ArgumentNullException(nameof(store));
        _web = new NativeMaxWebSocketTransport(store);
        _physical = new NativeMaxWebSocketTransport(store,
            physicalChannel ?? new PhysicalMaxWebSocketChannel(), "WEB_PHYSICAL");
        _browser = browserChannel is null ? null
            : new NativeMaxWebSocketTransport(store, browserChannel, "WEB_EDGE");
        _tcp = new NativeMaxTransport(store);
        _active = _physical;
    }

    public MaxConnectionPreference PreferredMode { get; set; } = MaxConnectionPreference.Automatic;
    public ConnectionState State => _active.State;
    public string? CurrentPhone => _active.CurrentPhone;

    private IMaxAuthenticationTransport SelectByPreference() => PreferredMode switch
    {
        MaxConnectionPreference.Tcp => _tcp,
        MaxConnectionPreference.PhysicalWebSocket => _physical,
        MaxConnectionPreference.WebSocket => _web,
        MaxConnectionPreference.BrowserWebSocket => _browser ?? _web,
        _ => _physical
    };

    public async Task<AuthResult> ProbeConnectionAsync(CancellationToken cancellationToken = default)
    {
        _active = SelectByPreference();
        var result = await _active.ProbeConnectionAsync(cancellationToken);
        if (PreferredMode == MaxConnectionPreference.Automatic)
        {
            string label = ReferenceEquals(_active, _physical) ? "WebSocket через физический интерфейс" :
                ReferenceEquals(_active, _browser) ? "WebSocket Microsoft Edge" : "WebSocket .NET";
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
        if (PreferredMode != MaxConnectionPreference.Automatic)
            return await _active.BeginLoginAsync(phoneNumber, cancellationToken);

        // Only retry across transports if failure occurred before AUTH_REQUEST;
        // never risk issuing a duplicate SMS after an uncertain server response.
        var candidates = new List<(string Name, IMaxAuthenticationTransport Transport)>
        {
            ("Физический интерфейс", _physical)
        };
        if (_browser is not null) candidates.Add(("Edge", _browser));
        candidates.Add((".NET", _web));
        candidates.Add(("TCP", _tcp));

        var failures = new List<string>();
        for (int i = 0; i < candidates.Count; i++)
        {
            var candidate = candidates[i];
            _active = candidate.Transport;
            AuthResult result = await _active.BeginLoginAsync(phoneNumber, cancellationToken);
            bool last = i == candidates.Count - 1;
            if (result.State != ConnectionState.Failed)
                return result;

            string diagnostic = candidate.Name + ": " + result.Error;
            if (!FailedBeforeSms(result) || last)
            {
                if (failures.Count == 0) return result;
                failures.Add(diagnostic);
                return result with { Error = string.Join("\n", failures) };
            }
            failures.Add(diagnostic);
        }
        throw new InvalidOperationException("Нет доступного транспорта MAX.");
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
            "WEB_PHYSICAL" => _physical,
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
        _physical.Dispose();
        _web.Dispose();
        _tcp.Dispose();
    }
}
