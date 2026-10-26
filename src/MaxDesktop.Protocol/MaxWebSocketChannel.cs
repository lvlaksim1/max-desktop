using System.Net.Http;
using System.Net.WebSockets;
using System.Text;

namespace MaxDesktop.Protocol;

/// <summary>
/// Swappable WEB-protocol transport. The MAX message JSON format and login
/// state machine are independent from the OS / browser TLS implementation.
/// </summary>
public interface IMaxWebSocketChannel : IDisposable
{
    bool IsOpen { get; }
    Task ConnectAsync(CancellationToken cancellationToken);
    Task SendTextAsync(string text, CancellationToken cancellationToken);
    Task<string> ReceiveTextAsync(CancellationToken cancellationToken);
    Task DisconnectAsync();
}

public sealed class DotNetMaxWebSocketChannel : IMaxWebSocketChannel
{
    private const int MaxFrameSize = 2 * 1024 * 1024;
    private const string WebUserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";
    private ClientWebSocket? _socket;
    public bool IsOpen => _socket?.State == WebSocketState.Open;

    public async Task ConnectAsync(CancellationToken ct)
    {
        await DisconnectAsync();
        var socket = new ClientWebSocket();
        try
        {
            socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(25);
            socket.Options.Proxy = HttpClient.DefaultProxy;
            socket.Options.SetRequestHeader("Origin", MaxWebSocketCodec.Origin);
            socket.Options.SetRequestHeader("User-Agent", WebUserAgent);
            socket.Options.RemoteCertificateValidationCallback = MaxTlsCertificateValidator.Validate;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(18));
            await socket.ConnectAsync(new Uri(MaxWebSocketCodec.Endpoint), timeout.Token);
            _socket = socket;
        }
        catch
        {
            socket.Dispose();
            throw;
        }
    }

    public async Task SendTextAsync(string text, CancellationToken ct)
    {
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new IOException("WebSocket-соединение с MAX не установлено.");
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct);
    }

    public async Task<string> ReceiveTextAsync(CancellationToken ct)
    {
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new IOException("WebSocket-соединение с MAX не установлено.");
        using var buffer = new MemoryStream();
        byte[] chunk = new byte[8192];
        while (true)
        {
            var result = await socket.ReceiveAsync(chunk.AsMemory(), ct);
            if (result.MessageType == WebSocketMessageType.Close)
                throw new IOException("MAX закрыл WebSocket-соединение.");
            if (result.MessageType != WebSocketMessageType.Text)
                throw new InvalidDataException("MAX вернул неподдерживаемое бинарное сообщение.");
            if (buffer.Length + result.Count > MaxFrameSize)
                throw new InvalidDataException("Размер пакета MAX превышает 2 МиБ.");
            buffer.Write(chunk, 0, result.Count);
            if (result.EndOfMessage) return Encoding.UTF8.GetString(buffer.ToArray());
        }
    }

    public Task DisconnectAsync()
    {
        var socket = Interlocked.Exchange(ref _socket, null);
        if (socket is not null) { socket.Abort(); socket.Dispose(); }
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        var socket = Interlocked.Exchange(ref _socket, null);
        if (socket is not null) { socket.Abort(); socket.Dispose(); }
    }
}
