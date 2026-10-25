namespace MaxDesktop.Protocol;

public enum ConnectionState { Disconnected, Connecting, AwaitingVerification, Connected, Failed }
public sealed record ChatSummary(long Id, string Title, string Preview, int UnreadCount);
public sealed record ChatMessage(long Id, long ChatId, string Sender, string Text, DateTimeOffset SentAt);
public sealed record AuthResult(ConnectionState State, string? Error);

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
