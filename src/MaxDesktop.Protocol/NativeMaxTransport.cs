using System.Collections.Concurrent;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.RegularExpressions;

namespace MaxDesktop.Protocol;

/// <summary>
/// First native (non-browser) MAX account transport. Commands and packet formats
/// follow the checked 26.35.1 APK and independent published protocol observations.
/// Until a real user logs in, interoperability remains an integration hypothesis.
/// </summary>
public sealed class NativeMaxTransport : IMaxAuthenticationTransport
{
    // These three production nodes responded to protocol v10 SESSION_INIT
    // through verified TLS on Windows in workflow 40248116311.
    private static readonly string[] VerifiedHosts =
        ["api2.oneme.ru", "api.oneme.ru", "api-tg.oneme.ru"];
    private const int Port = 443;
    private const ushort SessionInit = 6;
    private const ushort AuthRequest = 17;
    private const ushort VerifyCode = 18;
    private const ushort Login = 19;
    private const ushort CheckPassword = 115;
    private const ushort Ping = 1;
    private const string EmptyConfigHash =
        "00000000-0000000000000000-00000000-0000000000000000-0000000000000000-0-0000000000000000-00000000";

    private readonly IMaxSessionStore _sessionStore;
    private readonly bool _bindPhysical;
    private readonly SemaphoreSlim _authGate = new(1, 1);
    private readonly SemaphoreSlim _writeGate = new(1, 1);
    private readonly ConcurrentDictionary<ushort, TaskCompletionSource<MaxWireFrame>> _pending = new();
    private TcpClient? _tcp;
    private SslStream? _ssl;
    private CancellationTokenSource? _connectionCancel;
    private Task? _readTask;
    private Task? _pingTask;
    private int _sequence;
    private bool _disposed;
    private string? _phone;
    private string? _requestToken;
    private string? _passwordTrack;
    private string? _deviceId;
    private string? _instanceId;
    private byte[]? _mode;
    private int? _codeLength;
    private bool _tlsCertificateSeen;
    private bool _tlsCertificateTrusted;
    private int _tlsAttemptCount;
    private string _lastTriedHost = "api2.oneme.ru";
    private string? _connectedHost;
    private string _endpointDiagnostics = "";
    private bool _connectedViaSystemProxy;
    // These markers never contain a phone, verification code or authorization token.
    private string _connectionPhase = "ОЖИДАНИЕ";
    public string LastConnectionPhase => _connectionPhase;


    public NativeMaxTransport(IMaxSessionStore sessionStore, bool bindPhysicalInterface = false)
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _bindPhysical = bindPhysicalInterface;
    }

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;

    /// <summary>
    /// Tests the real connection and anonymous SESSION_INIT, sending no phone,
    /// verification code, login token or existing session identifier.
    /// </summary>
    public async Task<AuthResult> ProbeConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            if (State is ConnectionState.Connected or ConnectionState.AwaitingVerification or ConnectionState.AwaitingPassword)
                return new AuthResult(State, "Диагностика недоступна во время активной авторизации.");

            try
            {
                string testDeviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
                string testInstanceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
                await OpenConnectionAsync(testDeviceId, testInstanceId, cancellationToken);
                _connectionPhase = "SESSION_INIT_OK";
                return new AuthResult(ConnectionState.Disconnected,
                    $"Проверка успешна: TLS и SESSION_INIT работают ({_connectedHost}, " +
                    (_connectedViaSystemProxy ? "через системный прокси" : "напрямую") +
                    "). Номер телефона не передавался.");
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return new AuthResult(ConnectionState.Failed, ConnectionError(ex));
            }
            finally
            {
                await DisconnectAsync();
                State = ConnectionState.Disconnected;
            }
        }
        finally { _authGate.Release(); }
    }

    public string? CurrentPhone => State == ConnectionState.Connected ? _phone : null;

    public async Task<AuthResult> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            var saved = _sessionStore.Load();
            if (saved is null || string.IsNullOrWhiteSpace(saved.LoginToken))
                return new AuthResult(ConnectionState.Disconnected, null);
            State = ConnectionState.Connecting;
            try
            {
                _phone = saved.Phone;
                await OpenConnectionAsync(saved.DeviceId, saved.InstanceId, cancellationToken);
                await CompleteLoginAsync(saved.LoginToken, cancellationToken);
                State = ConnectionState.Connected;
                return new AuthResult(State, null);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                State = ConnectionState.Failed;
                await DisconnectAsync();
                return new AuthResult(State, ConnectionError(ex));
            }
        }
        finally { _authGate.Release(); }
    }

    public async Task<AuthResult> BeginLoginAsync(string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(phoneNumber ?? "", @"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant))
            return new AuthResult(ConnectionState.Failed, "Введите номер в международном формате: +79991234567.");

        await _authGate.WaitAsync(cancellationToken);
        try
        {
            ThrowIfDisposed();
            State = ConnectionState.Connecting;
            try
            {
                _phone = phoneNumber;
                _deviceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
                _instanceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
                await OpenConnectionAsync(_deviceId, _instanceId, cancellationToken);
                var response = await RequestAsync(AuthRequest, new Dictionary<string, object?>
                {
                    ["phone"] = phoneNumber,
                    ["type"] = "START_AUTH",
                    ["mode"] = _mode
                }, cancellationToken);
                _requestToken = Field(response.Payload, "token");
                _codeLength = IntField(response.Payload, "codeLength");
                if (Map(response.Payload, "callOutParams") is not null && _codeLength is null or <= 0)
                {
                    State = ConnectionState.Failed;
                    return new AuthResult(State,
                        "MAX запросил подтверждение звонком. Эта ветвь авторизации ещё не реализована; SMS-код может не прийти.");
                }
                if (string.IsNullOrEmpty(_requestToken))
                    throw new InvalidDataException("Сервер не вернул токен для проверки номера.");
                State = ConnectionState.AwaitingVerification;
                return new AuthResult(State, null, _codeLength);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                State = ConnectionState.Failed;
                await DisconnectAsync();
                return new AuthResult(State, ConnectionError(ex));
            }
        }
        finally { _authGate.Release(); }
    }

    public async Task<AuthResult> VerifyCodeAsync(string verificationCode, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(verificationCode) ||
            !Regex.IsMatch(verificationCode.Trim(), @"^[0-9]{4,10}$"))
            return new AuthResult(ConnectionState.Failed, "Код должен содержать от 4 до 10 цифр.");
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            if (State != ConnectionState.AwaitingVerification || string.IsNullOrEmpty(_requestToken))
                return new AuthResult(ConnectionState.Failed, "Сначала запросите код подтверждения.");
            try
            {
                var response = await RequestAsync(VerifyCode, new Dictionary<string, object?>
                {
                    ["token"] = _requestToken,
                    ["verifyCode"] = verificationCode.Trim(),
                    ["authTokenType"] = "CHECK_CODE"
                }, cancellationToken);
                string? token = FindToken(response.Payload, "LOGIN");
                if (!string.IsNullOrEmpty(token))
                    return await SaveAuthenticatedLoginAsync(token, cancellationToken);

                if (Map(response.Payload, "passwordChallenge") is { } challenge)
                {
                    _passwordTrack = Field(challenge, "trackId");
                    if (string.IsNullOrEmpty(_passwordTrack))
                        throw new InvalidDataException("Проверка паролем запрошена без идентификатора.");
                    State = ConnectionState.AwaitingPassword;
                    return new AuthResult(State, null, PasswordHint: Field(challenge, "hint"));
                }
                if (FindToken(response.Payload, "REGISTER") is not null)
                    return new AuthResult(ConnectionState.Failed,
                        "Для этого номера требуется регистрация нового аккаунта. Создание профиля пока не реализовано.");

                throw new InvalidDataException("Ответ подтверждения не содержит токен входа или запрос пароля.");
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return new AuthResult(ConnectionState.Failed, ConnectionError(ex));
            }
        }
        finally { _authGate.Release(); }
    }

    public async Task<AuthResult> VerifyPasswordAsync(string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
            return new AuthResult(ConnectionState.Failed, "Введите пароль дополнительной проверки.");
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            if (State != ConnectionState.AwaitingPassword || string.IsNullOrEmpty(_passwordTrack))
                return new AuthResult(ConnectionState.Failed, "Проверка пароля не запрошена сервером.");
            try
            {
                var response = await RequestAsync(CheckPassword, new Dictionary<string, object?>
                {
                    ["trackId"] = _passwordTrack,
                    ["password"] = password
                }, cancellationToken);
                string? token = FindToken(response.Payload, "LOGIN");
                if (string.IsNullOrEmpty(token))
                    throw new InvalidDataException("После проверки пароля сервер не выдал токен входа.");
                return await SaveAuthenticatedLoginAsync(token, cancellationToken);
            }
            catch (Exception ex) when (IsExpected(ex))
            {
                return new AuthResult(ConnectionState.AwaitingPassword, ConnectionError(ex));
            }
        }
        finally { _authGate.Release(); }
    }

    public async Task CancelPendingLoginAsync()
    {
        await _authGate.WaitAsync();
        try
        {
            if (State != ConnectionState.Connected)
            {
                await DisconnectAsync();
                _requestToken = null;
                _passwordTrack = null;
                _phone = null;
                State = ConnectionState.Disconnected;
            }
        }
        finally { _authGate.Release(); }
    }

    private async Task<AuthResult> SaveAuthenticatedLoginAsync(string token, CancellationToken cancellationToken)
    {
        await CompleteLoginAsync(token, cancellationToken);
        if (_deviceId is null || _instanceId is null || _phone is null)
            throw new InvalidDataException("Идентификаторы сеанса не инициализированы.");
        _sessionStore.Save(new MaxSavedSession(_phone, token, _deviceId, _instanceId,
            _bindPhysical ? "TCP_PHYSICAL" : "TCP"));
        _requestToken = null;
        _passwordTrack = null;
        State = ConnectionState.Connected;
        return new AuthResult(State, null);
    }

    private async Task CompleteLoginAsync(string token, CancellationToken ct)
    {
        var payload = new Dictionary<string, object?>
        {
            ["token"] = token,
            ["interactive"] = true,
            ["chatsSync"] = -1L,
            ["contactsSync"] = -1L,
            ["presenceSync"] = -1L,
            ["draftsSync"] = -1L,
            ["configHash"] = EmptyConfigHash,
            ["exp"] = new Dictionary<string, object?> { ["chatsCountGroups"] = new byte[] { 0x0a, 0x32 } },
            ["userAgent"] = UserAgent()
        };
        if (_mode is not null) payload["chatCacheFingerprint"] = _mode;
        await RequestAsync(Login, payload, ct);
    }

    private static Dictionary<string, object?> UserAgent() => new()
    {
        ["deviceType"] = "ANDROID",
        ["appVersion"] = MaxAndroidFingerprint.ApkVersion,
        ["buildNumber"] = MaxAndroidFingerprint.BuildNumber,
        ["osVersion"] = "Android 13",
        ["timezone"] = "Europe/Moscow",
        ["screen"] = "420dpi 420dpi 1080x2400",
        ["pushDeviceType"] = "GCM",
        ["arch"] = MaxAndroidFingerprint.Architecture,
        ["locale"] = "ru",
        ["deviceLocale"] = "ru",
        ["deviceName"] = "Google Pixel 6a"
    };

    private async Task OpenConnectionAsync(string deviceId, string instanceId, CancellationToken ct)
    {
        await DisconnectAsync();
        _deviceId = deviceId;
        _instanceId = instanceId;
        try
        {
            // Prefer configured system proxy tunnels (HTTP CONNECT / SOCKS5)
            // and then fall back to bounded direct connections. All paths
            // enforce the real MAX hostname, SNI and certificate validation.
            await ConnectVerifiedTlsAsync(ct);
            var ssl = _ssl ?? throw new IOException("TLS-соединение не инициализировано.");
            _connectionCancel = new CancellationTokenSource();
            _readTask = ReadLoopAsync(ssl, _connectionCancel.Token);
            var response = await RequestAsync(SessionInit, new Dictionary<string, object?>
            {
                ["deviceId"] = deviceId,
                ["mt_instanceid"] = instanceId,
                ["clientSessionId"] = RandomNumberGenerator.GetInt32(1, 71),
                ["userAgent"] = UserAgent()
            }, ct);

            if (!LongField(response.Payload, "callsSeed", out long seed))
                throw new InvalidDataException("Ответ SESSION_INIT не содержит callsSeed.");
            _mode = MaxAndroidFingerprint.Generate(deviceId, seed);
            _pingTask = HeartbeatAsync(_connectionCancel.Token);
        }
        catch
        {
            await DisconnectAsync();
            throw;
        }
    }

    private async Task ConnectVerifiedTlsAsync(CancellationToken ct)
    {
        _connectionPhase = "PROXY_DISCOVERY";
        _connectedHost = null;
        _connectedViaSystemProxy = false;
        _endpointDiagnostics = "";
        var proxyResults = new Dictionary<string, string>(StringComparer.Ordinal);
        bool systemProxyConfigured = false;
        _tlsAttemptCount = 0;

        // The physical-interface mode is per-application; it intentionally
        // ignores the system proxy without changing Windows or VPN settings.
        // The original mobile TCP mode retains its existing proxy behavior.
        if (!_bindPhysical)
        foreach (string hostname in VerifiedHosts)
        {
            Uri? proxy = MaxSystemProxyTunnel.FindConfiguredProxy(hostname);
            if (proxy is null) continue;
            systemProxyConfigured = true;
            if (_tlsAttemptCount > 0) await Task.Delay(TimeSpan.FromSeconds(5), ct);
            _tlsAttemptCount++;
            _lastTriedHost = hostname;
            _tlsCertificateSeen = false;
            _tlsCertificateTrusted = false;
            MaxSystemProxyTunnel.Tunnel? tunnel = null;
            SslStream? tls = null;
            try
            {
                using var attempt = CancellationTokenSource.CreateLinkedTokenSource(ct);
                attempt.CancelAfter(TimeSpan.FromSeconds(15));

                _connectionPhase = "PROXY_CONNECT";
                tunnel = await MaxSystemProxyTunnel.OpenAsync(
                    proxy, hostname, Port, attempt.Token);
                _connectionPhase = "TLS_HANDSHAKE";
                tls = new SslStream(tunnel.Stream, leaveInnerStreamOpen: false,
                    (sender, certificate, chain, errors) =>
                    {
                        _tlsCertificateSeen = certificate is not null;
                        _tlsCertificateTrusted = MaxTlsCertificateValidator.Validate(
                            sender, certificate, chain, errors);
                        return _tlsCertificateTrusted;
                    });
                await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = hostname,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.Online
                }, attempt.Token);
                _tcp = tunnel.Socket;
                _ssl = tls;
                _connectedHost = hostname;
                _connectedViaSystemProxy = true;
                _connectionPhase = "TLS_ESTABLISHED";
                return;
            }
            catch (Exception ex) when ((IsExpected(ex) || ex is UnauthorizedAccessException) &&
                !ct.IsCancellationRequested)
            {
                tls?.Dispose();
                tunnel?.Stream.Dispose();
                tunnel?.Socket.Dispose();
                Exception cause = ex.GetBaseException();
                string category = ex is UnauthorizedAccessException ? "ТРЕБУЕТ АВТОРИЗАЦИИ" :
                    cause.Message.Contains("EOF", StringComparison.OrdinalIgnoreCase) ? "EOF" :
                    _tlsCertificateSeen && !_tlsCertificateTrusted ? "СЕРТИФИКАТ" :
                    ex is OperationCanceledException ? "ТАЙМ-АУТ" :
                    _connectionPhase == "PROXY_CONNECT" ? "СБОЙ ПРОКСИ" : cause.GetType().Name;
                proxyResults[hostname] = category;
            }
        }

        _connectionPhase = "DNS_RESOLVE";
        var results = new Dictionary<string, string>(StringComparer.Ordinal);
        var addresses = new List<(string Host, IPAddress Ip)>();

        IPAddress? physicalAddress = _bindPhysical
            ? PhysicalMaxWebSocketChannel.FindPhysicalIPv4()
            : null;
        // Visit different verified service domains BEFORE retrying the same
        // DNS name. Three consecutive attempts on api2.oneme.ru were all
        // closed at TLS ClientHello in the user's Windows environment.
        var resolvedByHost = new Dictionary<string, IPAddress[]>(StringComparer.Ordinal);
        foreach (string hostname in VerifiedHosts)
        {
            try
            {
                IPAddress[] resolved = await Dns.GetHostAddressesAsync(hostname, ct);
                resolvedByHost[hostname] = resolved
                    .Where(ip => _bindPhysical
                        ? ip.AddressFamily == AddressFamily.InterNetwork
                        : ip.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6)
                    .Distinct()
                    .OrderBy(ip => ip.AddressFamily == AddressFamily.InterNetwork ? 0 : 1)
                    .Take(2).ToArray();
            }
            catch (Exception ex) when (IsExpected(ex) && !ct.IsCancellationRequested)
            {
                // One DNS failure does not suppress other verified domains.
                resolvedByHost[hostname] = [];
            }
        }

        // Maximum 6 outbound TCP/TLS attempts, spaced >= 5 seconds apart.
        for (int round = 0; round < 2; round++)
            foreach (string hostname in VerifiedHosts)
                if (resolvedByHost.TryGetValue(hostname, out var ips) && ips.Length > round)
                    addresses.Add((hostname, ips[round]));

        if (addresses.Count == 0)
        {
            _endpointDiagnostics = systemProxyConfigured
                ? "Системный прокси обнаружен, но туннель не установлен; " +
                  string.Join("; ", proxyResults.Select(p => p.Key + "=" + p.Value))
                : "Системный прокси не настроен.";
            throw new IOException("Не удалось получить IP-адреса проверенных узлов MAX.");
        }

        Exception? lastFailure = null;
        foreach (var (hostname, address) in addresses)
        {
            if (_tlsAttemptCount > 0)
                await Task.Delay(TimeSpan.FromSeconds(5), ct);

            _tlsAttemptCount++;
            _lastTriedHost = hostname;
            _tlsCertificateSeen = false;
            _tlsCertificateTrusted = false;

            // A source-address-bound TcpClient follows the proven Windows
            // physical route while preserving TLS SNI and certificate checks.
            var candidate = physicalAddress is null
                ? new TcpClient(address.AddressFamily)
                : new TcpClient(new IPEndPoint(physicalAddress, 0));
            SslStream? candidateSsl = null;
            try
            {
                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
                timeout.CancelAfter(TimeSpan.FromSeconds(15));
                _connectionPhase = "TCP_CONNECT";
                await candidate.ConnectAsync(address, Port, timeout.Token);
                _connectionPhase = "TLS_HANDSHAKE";
                candidateSsl = new SslStream(candidate.GetStream(), false,
                    (sender, certificate, chain, errors) =>
                    {
                        _tlsCertificateSeen = certificate is not null;
                        _tlsCertificateTrusted = MaxTlsCertificateValidator.Validate(
                            sender, certificate, chain, errors);
                        return _tlsCertificateTrusted;
                    });

                await candidateSsl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    // TLS SNI and certificate hostname validation are always
                    // bound to the actual verified DNS name (not the IP).
                    TargetHost = hostname,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.Online
                }, timeout.Token);

                _tcp = candidate;
                _ssl = candidateSsl;
                _connectedHost = hostname;
                _connectedViaSystemProxy = false;
                _connectionPhase = "TLS_ESTABLISHED";
                return;
            }
            catch (Exception ex) when (IsExpected(ex) && !ct.IsCancellationRequested)
            {
                candidateSsl?.Dispose();
                candidate.Dispose();
                lastFailure = ex;
                Exception root = ex.GetBaseException();
                string category = ex is OperationCanceledException ? "ТАЙМ-АУТ" :
                    _connectionPhase == "TCP_CONNECT" ? "TCP" :
                    root.Message.Contains("EOF", StringComparison.OrdinalIgnoreCase) ? "EOF" :
                    _tlsCertificateSeen && !_tlsCertificateTrusted ? "СЕРТИФИКАТ" :
                    root.GetType().Name;
                // No network addresses, account identifiers or exception text.
                results[hostname] = category;
            }
        }

        // Keep only per-host failure categories; never the proxy URL, IP,
        // phone number, verification code or session token.
        string directResults = string.Join("; ",
            VerifiedHosts.Where(results.ContainsKey)
                .Select(host => host + "=" + results[host]));
        string proxyText = systemProxyConfigured
            ? "прокси: " + string.Join("; ",
                VerifiedHosts.Where(proxyResults.ContainsKey)
                    .Select(host => host + "=" + proxyResults[host])) + ". "
            : "Системный прокси не настроен. ";
        _endpointDiagnostics = proxyText + "напрямую: " + directResults;
        throw new IOException(
            $"Все {_tlsAttemptCount} попыток подключения к трём проверенным узлам MAX завершились неудачно.",
            lastFailure);
    }

    private async Task<MaxWireFrame> RequestAsync(ushort opcode,
        Dictionary<string, object?> payload, CancellationToken ct)
    {
        ThrowIfDisposed();
        if (_ssl is not { } ssl)
            throw new IOException("Соединение с MAX не установлено.");
        _connectionPhase = opcode switch
        {
            SessionInit => "SESSION_INIT",
            AuthRequest => "AUTH_REQUEST",
            VerifyCode => "AUTH_CHECK_CODE",
            Login => "LOGIN",
            CheckPassword => "AUTH_PASSWORD",
            Ping => "PING",
            _ => "PROTOCOL_REQUEST"
        };
        ushort seq = unchecked((ushort)Interlocked.Increment(ref _sequence));
        var waiter = new TaskCompletionSource<MaxWireFrame>(TaskCreationOptions.RunContinuationsAsynchronously);
        if (!_pending.TryAdd(seq, waiter))
            throw new IOException("Конфликт идентификатора сетевого запроса.");
        try
        {
            byte[] bytes = MaxTcpFraming.Encode(seq, opcode, payload);
            await _writeGate.WaitAsync(ct);
            try { await ssl.WriteAsync(bytes, ct); await ssl.FlushAsync(ct); }
            finally { _writeGate.Release(); }
            var frame = await waiter.Task.WaitAsync(TimeSpan.FromSeconds(30), ct);
            if (frame.Command == 3)
                throw new MaxServerException(ServerError(frame.Payload));
            if (frame.Command != 1)
                throw new InvalidDataException($"MAX ответил неподдерживаемой командой {frame.Command}.");
            return frame;
        }
        finally { _pending.TryRemove(seq, out _); }
    }

    private async Task ReadLoopAsync(SslStream ssl, CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                byte[] header = new byte[MaxTcpFraming.HeaderSize];
                await ssl.ReadExactlyAsync(header, ct);
                int length = MaxTcpFraming.PayloadLength(header);
                byte[] body = new byte[length];
                if (length > 0) await ssl.ReadExactlyAsync(body, ct);
                var frame = MaxTcpFraming.Decode(header, body);
                if (frame.Command is 1 or 3 && _pending.TryRemove(frame.Sequence, out var task))
                    task.TrySetResult(frame);
                // cmd=2 notifications are left to the later chat/event subsystem.
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (IsExpected(ex))
        {
            foreach (var item in _pending)
                if (_pending.TryRemove(item.Key, out var waiter))
                    waiter.TrySetException(new IOException("Соединение с MAX прервано.", ex));
        }
    }

    private async Task HeartbeatAsync(CancellationToken ct)
    {
        try
        {
            while (!ct.IsCancellationRequested)
            {
                await Task.Delay(TimeSpan.FromSeconds(27), ct);
                if (ct.IsCancellationRequested) return;
                await RequestAsync(Ping, new Dictionary<string, object?> { ["interactive"] = false }, ct);
            }
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { }
        catch (Exception ex) when (IsExpected(ex))
        {
            // A failed ping cannot be treated as a successful connection.
            State = ConnectionState.Failed;
        }
    }

    private static Dictionary<string, object?>? Map(Dictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? value) ? value as Dictionary<string, object?> : null;

    private static string? Field(Dictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? value) ? value as string : null;

    private static int? IntField(Dictionary<string, object?> map, string key) =>
        map.TryGetValue(key, out object? value) && value is IConvertible convertible
            ? Convert.ToInt32(convertible) : null;

    private static bool LongField(Dictionary<string, object?> map, string key, out long result)
    {
        result = 0;
        if (!map.TryGetValue(key, out object? value) || value is null) return false;
        try { result = Convert.ToInt64(value); return true; }
        catch (Exception ex) when (ex is InvalidCastException or OverflowException or FormatException) { return false; }
    }

    private static string? FindToken(Dictionary<string, object?> payload, string type)
    {
        var attrs = Map(payload, "tokenAttrs");
        var entry = attrs is null ? null : Map(attrs, type);
        return entry is null ? null : Field(entry, "token");
    }

    private static string ServerError(Dictionary<string, object?> payload)
    {
        object? error = payload.TryGetValue("error", out var value) ? value : null;
        if (error is Dictionary<string, object?> nested)
            error = Field(nested, "message") ?? Field(nested, "type") ?? Field(nested, "code");
        string message = error?.ToString() ?? "сервер отклонил запрос";
        if (message.Equals("error.profile.active.session.no2fa", StringComparison.OrdinalIgnoreCase))
            return "error.profile.active.session.no2fa: MAX не разрешает новую сессию при уже активной сессии аккаунта без настроенного пароля для входа (2FA).";
        return message.Length > 160 ? message[..160] : message;
    }

    private string ConnectionError(Exception ex)
    {
        // An unexpected EOF is a connectivity failure, not a rejected phone number.
        var cause = ex.GetBaseException();
        if ((ex is IOException || cause is IOException) &&
            (ex.Message.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase) ||
             cause.Message.Contains("unexpected EOF", StringComparison.OrdinalIgnoreCase) ||
             cause.Message.Contains("0 bytes from the transport", StringComparison.OrdinalIgnoreCase)))
        {
            string detail = _connectionPhase == "TLS_HANDSHAKE"
                ? _tlsCertificateSeen
                    ? (_tlsCertificateTrusted ? "Сертификат сервера был проверен." :
                        "Сертификат получен, но проверка не завершилась успешно.")
                    : "Сертификат сервера ещё не был получен."
                : "Транспортный поток закрылся до завершения запроса.";
            return $"Этап {_connectionPhase}: TLS/TCP-соединение оборвано (EOF). {detail} " +
                   $"Последний узел: {_lastTriedHost}. Попыток подключения: {_tlsAttemptCount}. {_endpointDiagnostics} Номер телефона и код здесь не проверялись.";
        }
        return $"Этап {_connectionPhase}, попыток {_tlsAttemptCount}. {_endpointDiagnostics} {SafeError(ex)}";
    }

    private static string SafeError(Exception ex)
    {
        if (ex is MaxServerException) return ex.Message;
        if (ex is OperationCanceledException) return "Операция отменена.";
        if (ex is AuthenticationException) return "Не удалось проверить TLS-сертификат сервера MAX.";
        if (ex is SocketException) return "Соединение с сервером MAX недоступно.";
        if (ex is TimeoutException) return "Время ожидания ответа MAX истекло.";
        if (ex is NotSupportedException) return ex.Message;
        if (ex is InvalidDataException or IOException) return ex.Message;
        return "Ошибка подключения к MAX: " + ex.GetType().Name;
    }

    private static bool IsExpected(Exception ex) =>
        ex is IOException or SocketException or AuthenticationException or OperationCanceledException
            or TimeoutException or NotSupportedException or InvalidDataException
            or System.Security.Cryptography.CryptographicException or MaxServerException
            or InvalidOperationException;

    public Task<IReadOnlyList<ChatSummary>> GetChatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChatSummary>>(Array.Empty<ChatSummary>());
    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(long chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());
    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Отправка сообщений ещё не реализована."));

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            await DisconnectAsync();
            _sessionStore.Delete();
            _phone = null;
            _requestToken = null;
            _passwordTrack = null;
            State = ConnectionState.Disconnected;
        }
        finally { _authGate.Release(); }
    }

    private async Task DisconnectAsync()
    {
        if (_connectionCancel is { } cancel) cancel.Cancel();
        _ssl?.Dispose();
        _tcp?.Dispose();
        if (_readTask is not null)
        {
            try { await _readTask.WaitAsync(TimeSpan.FromSeconds(2)); }
            catch (Exception ex) when (IsExpected(ex)) { }
        }
        _connectionCancel?.Dispose();
        _connectionCancel = null;
        _ssl = null;
        _tcp = null;
        _readTask = null;
        _pingTask = null;
        _mode = null;
        foreach (var pair in _pending)
            if (_pending.TryRemove(pair.Key, out var waiter))
                waiter.TrySetException(new IOException("Соединение закрыто."));
    }

    private void ThrowIfDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeMaxTransport));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _connectionCancel?.Cancel();
        _ssl?.Dispose();
        _tcp?.Dispose();
        _connectionCancel?.Dispose();
        _authGate.Dispose();
        _writeGate.Dispose();
    }

    private sealed class MaxServerException : IOException
    {
        public MaxServerException(string description) : base("MAX отклонил запрос: " + description) { }
    }
}
