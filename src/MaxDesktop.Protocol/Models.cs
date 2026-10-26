namespace MaxDesktop.Protocol;

public enum ConnectionState { Disconnected, Connecting, AwaitingVerification, AwaitingPassword, AwaitingCaptcha, Connected, Failed }
public sealed record ChatSummary(long Id, string Title, string Preview, int UnreadCount);
public sealed record ChatMessage(long Id, long ChatId, string Sender, string Text, DateTimeOffset SentAt);
public sealed record AuthResult(ConnectionState State, string? Error, int? CodeLength = null, string? PasswordHint = null, string? CaptchaUrl = null);


public sealed record MaxSavedSession(string Phone, string LoginToken, string DeviceId, string InstanceId, string Transport = "TCP");

/// <summary>Session storage is injected so the protocol layer does not own Windows UI or DPAPI APIs.</summary>
public interface IMaxSessionStore
{
    MaxSavedSession? Load();
    void Save(MaxSavedSession session);
    void Delete();
}

public interface IMaxAuthenticationTransport : IMaxTransport, IDisposable
{
    Task<AuthResult> RestoreSessionAsync(CancellationToken cancellationToken = default);
    Task<AuthResult> ProbeConnectionAsync(CancellationToken cancellationToken = default);
    Task<AuthResult> VerifyPasswordAsync(string password, CancellationToken cancellationToken = default);
    Task LogoutAsync(CancellationToken cancellationToken = default);
    Task<AuthResult> CompleteCaptchaAsync(string captchaToken, CancellationToken cancellationToken = default) =>
        Task.FromResult(new AuthResult(ConnectionState.Failed, "Проверка человека этим способом входа не поддерживается."));
    Task CancelPendingLoginAsync() => Task.CompletedTask;
    string? CurrentPhone { get; }
}

public interface IMaxTransport
{
    ConnectionState State { get; }
    Task<AuthResult> BeginLoginAsync(string phoneNumber, CancellationToken cancellationToken = default);
    Task<AuthResult> VerifyCodeAsync(string verificationCode, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatSummary>> GetChatsAsync(CancellationToken cancellationToken = default);
    Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(long chatId, CancellationToken cancellationToken = default);
    Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default);
}

/// <summary>The UI remains usable offline; unverified network operations fail explicitly.</summary>
public sealed class UnavailableMaxTransport : IMaxTransport
{
    public const string Reason = "Авторизация MAX не проверена на реальном входе. Сетевые операции недоступны.";
    public ConnectionState State => ConnectionState.Disconnected;
    public Task<AuthResult> BeginLoginAsync(string phoneNumber, CancellationToken cancellationToken = default)
        => Task.FromResult(new AuthResult(ConnectionState.Failed, Reason));
    public Task<AuthResult> VerifyCodeAsync(string verificationCode, CancellationToken cancellationToken = default)
        => Task.FromResult(new AuthResult(ConnectionState.Failed, Reason));
    public Task<IReadOnlyList<ChatSummary>> GetChatsAsync(CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ChatSummary>>(Array.Empty<ChatSummary>());
    public Task<IReadOnlyList<ChatMessage>> GetMessagesAsync(long chatId, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<ChatMessage>>(Array.Empty<ChatMessage>());
    public Task SendMessageAsync(long chatId, string text, CancellationToken cancellationToken = default)
        => Task.FromException(new NotSupportedException(Reason));
}
