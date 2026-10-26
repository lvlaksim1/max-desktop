using System.Net.Http;
using System.Net.WebSockets;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaxDesktop.Protocol;

/// <summary>
/// Independent version-11 WebSocket account authentication. No QR flow.
/// Uses the published WEB INIT (6), SMS request (17), code (18),
/// optional 2FA password (115), and token LOGIN (19) requests.
/// A successful build is not evidence of an accepted real-world account login.
/// </summary>
public sealed class NativeMaxWebSocketTransport : IMaxAuthenticationTransport
{
    private const int InitOpcode = 6;
    private const int RequestSmsOpcode = 17;
    private const int CheckCodeOpcode = 18;
    private const int LoginOpcode = 19;
    private const int CheckPasswordOpcode = 115;
    private const int MaxFrameSize = 2 * 1024 * 1024;
    private const string WebUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

    private readonly IMaxSessionStore _sessionStore;
    private readonly SemaphoreSlim _authGate = new(1, 1);
    private readonly SemaphoreSlim _requestGate = new(1, 1);
    private readonly IMaxWebSocketChannel _channel;
    private readonly string _transportTag;
    private int _sequence;
    private string _stage = "WS_IDLE";
    private bool _disposed;
    private string? _phone;
    private string? _deviceId;
    private string? _instanceId;
    private string? _verificationToken;
    private string? _passwordTrack;

    public NativeMaxWebSocketTransport(IMaxSessionStore sessionStore,
        IMaxWebSocketChannel? channel = null, string transportTag = "WEB")
    {
        _sessionStore = sessionStore ?? throw new ArgumentNullException(nameof(sessionStore));
        _channel = channel ?? new DotNetMaxWebSocketChannel();
        _transportTag = transportTag;
    }

    public ConnectionState State { get; private set; } = ConnectionState.Disconnected;
    public string? CurrentPhone => State == ConnectionState.Connected ? _phone : null;

    public async Task<AuthResult> ProbeConnectionAsync(CancellationToken cancellationToken = default)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            EnsureNotDisposed();
            if (State is ConnectionState.AwaitingVerification or ConnectionState.AwaitingPassword or ConnectionState.Connected)
                return new AuthResult(State, "Нельзя проверить соединение во время активной авторизации.");

            try
            {
                await OpenAndInitializeAsync(Guid.NewGuid().ToString(), cancellationToken);
                return new AuthResult(ConnectionState.Disconnected,
                    "WebSocket: защищённое подключение и SESSION_INIT подтверждены сервером. Номер не передавался.");
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                return new AuthResult(ConnectionState.Failed, SafeError(ex));
            }
            finally
            {
                await DisconnectAsync();
                State = ConnectionState.Disconnected;
            }
        }
        finally { _authGate.Release(); }
    }

    public async Task<AuthResult> BeginLoginAsync(
        string phoneNumber, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(phoneNumber ?? "", @"^\+[1-9][0-9]{7,14}$", RegexOptions.CultureInvariant))
            return new AuthResult(ConnectionState.Failed, "Введите номер в международном формате.");

        await _authGate.WaitAsync(cancellationToken);
        try
        {
            EnsureNotDisposed();
            _phone = phoneNumber;
            _deviceId = Guid.NewGuid().ToString();
            _instanceId = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant();
            State = ConnectionState.Connecting;
            try
            {
                await OpenAndInitializeAsync(_deviceId, cancellationToken);
                var response = await RequestAsync(RequestSmsOpcode, new
                {
                    phone = phoneNumber,
                    type = "START_AUTH",
                    language = "ru"
                }, cancellationToken);
                _verificationToken = GetString(response.Payload, "token");
                if (string.IsNullOrEmpty(_verificationToken))
                    throw new InvalidDataException("В ответе MAX отсутствует токен проверки номера.");

                int? codeLength = GetInt(response.Payload, "codeLength");
                State = ConnectionState.AwaitingVerification;
                return new AuthResult(State, null, codeLength);
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                State = ConnectionState.Failed;
                await DisconnectAsync();
                return new AuthResult(State, SafeError(ex));
            }
        }
        finally { _authGate.Release(); }
    }

    public async Task<AuthResult> VerifyCodeAsync(
        string verificationCode, CancellationToken cancellationToken = default)
    {
        if (!Regex.IsMatch(verificationCode?.Trim() ?? "", @"^[0-9]{4,10}$", RegexOptions.CultureInvariant))
            return new AuthResult(ConnectionState.Failed, "Код должен содержать от 4 до 10 цифр.");

        await _authGate.WaitAsync(cancellationToken);
        try
        {
            if (State != ConnectionState.AwaitingVerification || _verificationToken is null)
                return new AuthResult(ConnectionState.Failed, "Сначала запросите код подтверждения.");

            try
            {
                var response = await RequestAsync(CheckCodeOpcode, new
                {
                    token = _verificationToken,
                    verifyCode = verificationCode.Trim(),
                    authTokenType = "CHECK_CODE"
                }, cancellationToken);

                string? loginToken = ReadLoginToken(response.Payload);
                if (!string.IsNullOrWhiteSpace(loginToken))
                    return await CompleteAuthenticatedLoginAsync(loginToken, cancellationToken);

                if (TryProperty(response.Payload, "passwordChallenge", out var challenge))
                {
                    _passwordTrack = GetString(challenge, "trackId");
                    if (string.IsNullOrEmpty(_passwordTrack))
                        throw new InvalidDataException("MAX запросил пароль без идентификатора проверки.");
                    State = ConnectionState.AwaitingPassword;
                    return new AuthResult(State, null, PasswordHint: GetString(challenge, "hint"));
                }
                if (TryProperty(response.Payload, "tokenAttrs", out var attrs) &&
                    TryProperty(attrs, "REGISTER", out _))
                    return new AuthResult(ConnectionState.Failed,
                        "Аккаунт требует регистрации; создание нового аккаунта пока не реализовано.");

                throw new InvalidDataException("Не получен токен входа или запрос дополнительного пароля.");
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                return new AuthResult(State, SafeError(ex));
            }
        }
        finally { _authGate.Release(); }
    }

    public async Task<AuthResult> VerifyPasswordAsync(
        string password, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrEmpty(password))
            return new AuthResult(ConnectionState.Failed, "Введите дополнительный пароль.");

        await _authGate.WaitAsync(cancellationToken);
        try
        {
            if (State != ConnectionState.AwaitingPassword || string.IsNullOrEmpty(_passwordTrack))
                return new AuthResult(ConnectionState.Failed, "Дополнительный пароль сервером не запрошен.");

            try
            {
                var response = await RequestAsync(CheckPasswordOpcode, new
                {
                    trackId = _passwordTrack,
                    password
                }, cancellationToken);
                string? token = ReadLoginToken(response.Payload);
                if (string.IsNullOrWhiteSpace(token))
                    throw new InvalidDataException("MAX не вернул токен после дополнительной проверки.");
                return await CompleteAuthenticatedLoginAsync(token, cancellationToken);
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                return new AuthResult(State, SafeError(ex));
            }
        }
        finally { _authGate.Release(); }
    }

    public async Task<AuthResult> RestoreSessionAsync(CancellationToken cancellationToken = default)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            EnsureNotDisposed();
            var saved = _sessionStore.Load();
            if (saved is null || saved.Transport != _transportTag || string.IsNullOrEmpty(saved.LoginToken))
                return new AuthResult(ConnectionState.Disconnected, null);

            State = ConnectionState.Connecting;
            try
            {
                _phone = saved.Phone;
                _deviceId = saved.DeviceId;
                _instanceId = saved.InstanceId;
                await OpenAndInitializeAsync(saved.DeviceId, cancellationToken);
                await LoginByTokenAsync(saved.LoginToken, cancellationToken);
                State = ConnectionState.Connected;
                return new AuthResult(State, null);
            }
            catch (Exception ex) when (IsConnectionException(ex))
            {
                State = ConnectionState.Failed;
                await DisconnectAsync();
                return new AuthResult(State, SafeError(ex));
            }
        }
        finally { _authGate.Release(); }
    }

    private async Task<AuthResult> CompleteAuthenticatedLoginAsync(string token, CancellationToken ct)
    {
        await LoginByTokenAsync(token, ct);
        if (_phone is null || _deviceId is null || _instanceId is null)
            throw new InvalidDataException("Неполные идентификаторы WebSocket-сеанса.");
        _sessionStore.Save(new MaxSavedSession(_phone, token, _deviceId, _instanceId, _transportTag));
        _verificationToken = null;
        _passwordTrack = null;
        State = ConnectionState.Connected;
        return new AuthResult(State, null);
    }

    private async Task LoginByTokenAsync(string token, CancellationToken ct)
    {
        await RequestAsync(LoginOpcode, new
        {
            interactive = true,
            token,
            chatsCount = 40,
            chatsSync = 0,
            contactsSync = 0,
            presenceSync = -1,
            draftsSync = 0
        }, ct);
    }

    private async Task OpenAndInitializeAsync(string deviceId, CancellationToken ct)
    {
        await DisconnectAsync();
        _stage = "WS_CONNECT";
        await _channel.ConnectAsync(ct);
        _stage = "WS_INIT";
        await RequestAsync(InitOpcode, new
        {
            deviceId,
            userAgent = new
            {
                deviceType = "WEB",
                locale = "ru",
                deviceLocale = "ru",
                osVersion = "Windows 10",
                deviceName = "Chrome",
                headerUserAgent = WebUserAgent,
                appVersion = "26.6.17",
                screen = "1080x1920 1.0x",
                timezone = "Europe/Moscow",
                pushDeviceType = "WEBPUSH"
            }
        }, ct);
        _stage = "WS_READY";
    }

    private async Task<MaxWebSocketFrame> RequestAsync(int opcode, object payload, CancellationToken ct)
    {
        await _requestGate.WaitAsync(ct);
        try
        {
            if (!_channel.IsOpen)
                throw new IOException("WebSocket-соединение с MAX не установлено.");

            _stage = opcode switch
            {
                InitOpcode => "WS_INIT",
                RequestSmsOpcode => "WS_AUTH_REQUEST",
                CheckCodeOpcode => "WS_CHECK_CODE",
                CheckPasswordOpcode => "WS_CHECK_PASSWORD",
                LoginOpcode => "WS_LOGIN",
                _ => "WS_REQUEST"
            };
            int seq = Interlocked.Increment(ref _sequence);
            string request = MaxWebSocketCodec.Encode(seq, opcode, payload);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            await _channel.SendTextAsync(request, timeout.Token);

            // Messages are read sequentially; unsolicited push messages do not
            // resolve a pending RPC response and never expose account data.
            for (int count = 0; count < 50; count++)
            {
                string text = await _channel.ReceiveTextAsync(timeout.Token);
                MaxWebSocketFrame response = MaxWebSocketCodec.Decode(text);
                if (response.Sequence != seq || response.Opcode != opcode)
                    continue;
                if (response.Command == 3)
                    throw new MaxWebSocketServerException(ServerMessage(response.Payload));
                if (response.Command != 1)
                    throw new InvalidDataException("MAX вернул неожиданный тип WebSocket-ответа.");
                return response;
            }
            throw new InvalidDataException("MAX вернул слишком много сообщений без ответа на запрос.");
        }
        finally { _requestGate.Release(); }
    }

    private static bool TryProperty(JsonElement element, string key, out JsonElement value)
    {
        value = default;
        return element.ValueKind == JsonValueKind.Object && element.TryGetProperty(key, out value) &&
            value.ValueKind != JsonValueKind.Null;
    }

    private static string? GetString(JsonElement value, string key) =>
        TryProperty(value, key, out var field) && field.ValueKind == JsonValueKind.String
            ? field.GetString() : null;

    private static int? GetInt(JsonElement value, string key) =>
        TryProperty(value, key, out var field) && field.TryGetInt32(out int result)
            ? result : null;

    private static string? ReadLoginToken(JsonElement payload)
    {
        return TryProperty(payload, "tokenAttrs", out var attrs) &&
            TryProperty(attrs, "LOGIN", out var login)
            ? GetString(login, "token") : null;
    }

    private static string ServerMessage(JsonElement payload)
    {
        string msg = GetString(payload, "localizedMessage") ??
            GetString(payload, "error") ?? GetString(payload, "message") ??
            "Сервер отклонил запрос.";
        return msg.Length > 170 ? msg[..170] : msg;
    }

    private string SafeError(Exception ex)
    {
        if (ex is MaxWebSocketServerException server) return $"{_stage}: {server.Message}";
        if (ex is OperationCanceledException) return $"{_stage}: время ожидания истекло или операция отменена.";
        if (ex is WebSocketException web)
        {
            var chain = new List<string> { $"WebSocket={web.WebSocketErrorCode}" };
            for (Exception? inner = web.InnerException; inner is not null && chain.Count < 5; inner = inner.InnerException)
            {
                if (inner is System.Net.Sockets.SocketException socket)
                    chain.Add($"Сеть={socket.SocketErrorCode}");
                else if (inner is HttpRequestException http)
                    chain.Add($"HTTP={(int?)http.StatusCode ?? 0}");
                else if (inner is System.Security.Authentication.AuthenticationException)
                    chain.Add("TLS=ошибка сертификата");
                else if (inner is IOException io &&
                         (io.Message.Contains("EOF", StringComparison.OrdinalIgnoreCase) ||
                          io.Message.Contains("0 bytes", StringComparison.OrdinalIgnoreCase)))
                    chain.Add("TLS=EOF до завершения соединения");
                else chain.Add(inner.GetType().Name);
            }
            return $"{_stage}: сбой WebSocket ({string.Join(", ", chain)}).";
        }
        if (ex is System.Security.Authentication.AuthenticationException)
            return $"{_stage}: серверный TLS-сертификат WebSocket не прошёл проверку.";
        if (ex is InvalidDataException) return $"{_stage}: {ex.Message}";
        if (ex is IOException) return $"{_stage}: {ex.Message}";
        return $"{_stage}: {ex.GetType().Name}";
    }

    private static bool IsConnectionException(Exception ex) =>
        ex is IOException or InvalidDataException or WebSocketException or
            OperationCanceledException or InvalidOperationException or
            System.Security.Authentication.AuthenticationException or
            System.Security.Cryptography.CryptographicException;

    private Task DisconnectAsync() => _channel.DisconnectAsync();

    public async Task LogoutAsync(CancellationToken cancellationToken = default)
    {
        await _authGate.WaitAsync(cancellationToken);
        try
        {
            await DisconnectAsync();
            _verificationToken = null;
            _passwordTrack = null;
            _phone = null;
            _sessionStore.Delete();
            State = ConnectionState.Disconnected;
        }
        finally { _authGate.Release(); }
    }

    public Task<IReadOnlyList<ChatSummary>> GetChatsAsync(CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChatSummary>>([]);
    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(long chatId, CancellationToken cancellationToken = default) =>
        Task.FromResult<IReadOnlyList<ChatMessage>>([]);
    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default) =>
        Task.FromException(new NotSupportedException("Обмен сообщениями WebSocket ещё не реализован."));

    private void EnsureNotDisposed()
    {
        if (_disposed) throw new ObjectDisposedException(nameof(NativeMaxWebSocketTransport));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _channel.Dispose();
        _authGate.Dispose();
        _requestGate.Dispose();
    }

    private sealed class MaxWebSocketServerException : IOException
    {
        public MaxWebSocketServerException(string message) : base(message) { }
    }
}
