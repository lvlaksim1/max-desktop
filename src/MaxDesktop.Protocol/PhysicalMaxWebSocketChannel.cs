using System.Net;
using System.Net.Http;
using System.Net.NetworkInformation;
using System.Net.Security;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Text;

namespace MaxDesktop.Protocol;

/// <summary>
/// Per-application physical-interface connection. The system VPN and its
/// routing, DNS, proxy and firewall settings remain unchanged.
/// TLS is still validated for the original MAX hostname.
/// </summary>
public sealed class PhysicalMaxWebSocketChannel : IMaxWebSocketChannel
{
    private const int MaxFrameSize = 2 * 1024 * 1024;
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 " +
        "(KHTML, like Gecko) Chrome/141.0.0.0 Safari/537.36";

    private ClientWebSocket? _socket;
    private HttpMessageInvoker? _invoker;

    public bool IsOpen => _socket?.State == WebSocketState.Open;

    /// <summary>
    /// Selects a regular up Ethernet/Wi-Fi adapter with IPv4 and a gateway;
    /// rejects TUN/TAP and other recognisable virtual VPN interfaces.
    /// Addresses are used only locally and are never logged or persisted.
    /// </summary>
    internal static IPAddress FindPhysicalIPv4()
    {
        foreach (var network in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (network.OperationalStatus != OperationalStatus.Up ||
                network.NetworkInterfaceType is not (NetworkInterfaceType.Ethernet or NetworkInterfaceType.Wireless80211))
                continue;

            string description = network.Description + " " + network.Name;
            if (new[] { "TUN", "TAP", "Wintun", "WireGuard", "VPN", "Xray",
                        "sing-box", "Clash" }
                .Any(token => description.Contains(token, StringComparison.OrdinalIgnoreCase)))
                continue;

            IPInterfaceProperties properties;
            try { properties = network.GetIPProperties(); }
            catch (NetworkInformationException) { continue; }

            if (!properties.GatewayAddresses.Any(g => g.Address.AddressFamily == AddressFamily.InterNetwork))
                continue;

            var candidate = properties.UnicastAddresses
                .Select(u => u.Address)
                .FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork &&
                    !IPAddress.IsLoopback(ip) && !ip.IsIPv6LinkLocal &&
                    !ip.GetAddressBytes().AsSpan(0, 2).SequenceEqual(new byte[] { 169, 254 }));

            if (candidate is not null)
                return candidate;
        }
        throw new IOException("Рабочий физический сетевой интерфейс IPv4 не обнаружен.");
    }

    private sealed class MaxHandshakeHeadersHandler(HttpMessageHandler inner) : DelegatingHandler(inner)
    {
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            request.Headers.TryAddWithoutValidation("Origin", MaxWebSocketCodec.Origin);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            return base.SendAsync(request, cancellationToken);
        }
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await DisconnectAsync();
        IPAddress localAddress = FindPhysicalIPv4();
        var socket = new ClientWebSocket();
        var handler = new SocketsHttpHandler
        {
            UseProxy = false,
            ConnectCallback = async (context, ct) =>
            {
                var tcp = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
                try
                {
                    tcp.Bind(new IPEndPoint(localAddress, 0));
                    await tcp.ConnectAsync(context.DnsEndPoint, ct);
                    return new NetworkStream(tcp, ownsSocket: true);
                }
                catch
                {
                    tcp.Dispose();
                    throw;
                }
            }
        };
        handler.SslOptions.RemoteCertificateValidationCallback = MaxTlsCertificateValidator.Validate;
        var invoker = new HttpMessageInvoker(new MaxHandshakeHeadersHandler(handler), disposeHandler: true);
        try
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(18));
            // With HttpMessageInvoker, leave ClientWebSocket.Options at their defaults.
            await socket.ConnectAsync(new Uri(MaxWebSocketCodec.Endpoint), invoker, timeout.Token);
            _socket = socket;
            _invoker = invoker;
        }
        catch
        {
            socket.Dispose();
            invoker.Dispose();
            throw;
        }
    }

    public async Task SendTextAsync(string text, CancellationToken ct)
    {
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new IOException("WebSocket через физический интерфейс не подключён.");
        byte[] bytes = Encoding.UTF8.GetBytes(text);
        await socket.SendAsync(bytes.AsMemory(), WebSocketMessageType.Text, true, ct);
    }

    public async Task<string> ReceiveTextAsync(CancellationToken ct)
    {
        if (_socket is not { State: WebSocketState.Open } socket)
            throw new IOException("WebSocket через физический интерфейс не подключён.");
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
        Interlocked.Exchange(ref _socket, null)?.Dispose();
        Interlocked.Exchange(ref _invoker, null)?.Dispose();
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        Interlocked.Exchange(ref _socket, null)?.Dispose();
        Interlocked.Exchange(ref _invoker, null)?.Dispose();
    }
}
