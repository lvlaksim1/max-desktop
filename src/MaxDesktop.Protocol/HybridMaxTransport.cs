namespace MaxDesktop.Protocol;

public enum MaxConnectionPreference { Automatic, WebSocket, Tcp }

/// <summary>
/// Chooses between independently implemented WEB JSON/WebSocket and ANDROID
/// binary TCP authentication. Does not change how sessions are encrypted.
/// No QR login is implemented or offered.
/// </summary>
public sealed class HybridMaxTransport : IMaxAuthenticationTransport
{
    private readonly IMaxSessionStore _sessionStore;
    private readonly NativeMaxWebSocketTransport _web;
    private readonly NativeMaxTransport _tcp;
    private IMaxAuthenticationTransport _active;

    public HybridMaxTransport(IMaxSessionStore store)
    {
        _sessionStore = store ?? throw new ArgumentNullException(nameof(store));
        _web = new NativeMaxWebSocketTransport(store);
        _tcp = new NativeMaxTransport(store);
        _active = _web;
    }

    public MaxConnectionPreference PreferredMode { get; set; } = MaxConnectionPreference.Automatic;
    public ConnectionState State => _active.State;
    public string? CurrentPhone => _active.CurrentPhone;

    private IMaxAuthenticationTransport SelectByPreference() =>
        PreferredMode == MaxConnectionPreference.Tcp ? _tcp : _web;

    public async Task<AuthResult> ProbeConnectionAsync(CancellationToken cancellationToken = default)
    {
        _active = SelectByPreference();
        var result = await _active.ProbeConnectionAsync(cancellationToken);
        if (PreferredMode == MaxConnectionPreference.Automatic)
        {
            string text = result.Error ?? "Проверка завершена.";
            return result with
            {
                Error = "WebSocket: " + text +
                    " Для отдельной проверки прежнего TCP выберите способ «TCP (мобильный)»."
            };
        }
        return result;
    }

    public async Task<AuthResult> BeginLoginAsync(
        string phoneNumber, CancellationToken cancellationToken = default)
    {
        _active = SelectByPreference();
        AuthResult result = await _active.BeginLoginAsync(phoneNumber, cancellationToken);

        // Only connection/anonymous-init failures allow transport fallback.
        // Once the SMS request may have reached the server, repeating it
        // automatically could send two verification codes or trigger limits.
        if (PreferredMode == MaxConnectionPreference.Automatic &&
            result.State == ConnectionState.Failed &&
            (result.Error?.StartsWith("WS_CONNECT:", StringComparison.Ordinal) == true ||
             result.Error?.StartsWith("WS_INIT:", StringComparison.Ordinal) == true))
        {
            string webFailure = result.Error;
            _active = _tcp;
            AuthResult tcpResult = await _tcp.BeginLoginAsync(phoneNumber, cancellationToken);
            if (tcpResult.State == ConnectionState.Failed)
                return tcpResult with { Error = "WebSocket: " + webFailure + "\nTCP: " + tcpResult.Error };
            return tcpResult;
        }
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
        // WEB sessions are bound to WEB deviceType; a TCP session must not
        // be restored with WebSocket/WEB fingerprints or vice versa.
        _active = string.Equals(session.Transport, "WEB", StringComparison.Ordinal)
            ? _web : _tcp;
        return await _active.RestoreSessionAsync(cancellationToken);
    }

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _active.LogoutAsync(cancellationToken);
    }

    public Task<IReadOnlyList<ChatSummary>> GetChatsAsync(CancellationToken cancellationToken = default) =>
        _active.GetChatsAsync(cancellationToken);

    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(long chatId, CancellationToken cancellationToken = default) =>
        _active.GetMessagesAsync(chatId, cancellationToken);

    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default) =>
        _active.SendMessageAsync(chatId, text, cancellationToken);

    public void Dispose()
    {
        _web.Dispose();
        _tcp.Dispose();
    }
}
