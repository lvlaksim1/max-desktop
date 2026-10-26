using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Text;

namespace MaxDesktop.Protocol;

/// <summary>
/// Private, short-lived CONNECT proxy used by one mini-app WebView2 window.
/// MAX domains use the already proven physical-interface route. Other hosts
/// use Windows' configured proxy or the normal OS route (which preserves VPN).
/// TLS is always negotiated by Chromium with the destination, not intercepted.
/// </summary>
public sealed class MiniAppNetworkProxy : IDisposable
{
    private readonly IPAddress _physicalAddress;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly TcpListener _listener;
    private readonly Task _acceptLoop;
    private bool _disposed;

    public int Port { get; }

    public MiniAppNetworkProxy(IPAddress? physicalAddress = null)
    {
        _physicalAddress = physicalAddress ?? PhysicalMaxWebSocketChannel.FindPhysicalIPv4();
        if (_physicalAddress.AddressFamily != AddressFamily.InterNetwork)
            throw new ArgumentException("Нужен физический IPv4-адрес.", nameof(physicalAddress));

        _listener = new TcpListener(IPAddress.Loopback, 0);
        _listener.Start(32);
        Port = ((IPEndPoint)_listener.LocalEndpoint).Port;
        _acceptLoop = AcceptLoopAsync(_shutdown.Token);
    }

    public static bool IsMaxHost(string host) =>
        host.Equals("max.ru", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".max.ru", StringComparison.OrdinalIgnoreCase) ||
        host.Equals("oneme.ru", StringComparison.OrdinalIgnoreCase) ||
        host.EndsWith(".oneme.ru", StringComparison.OrdinalIgnoreCase);

    public static bool TryReadConnectHost(string? firstLine, out string host)
    {
        host = "";
        if (firstLine is null) return false;
        string[] parts = firstLine.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (parts.Length != 3 || parts[0] != "CONNECT" ||
            parts[2] is not ("HTTP/1.1" or "HTTP/1.0")) return false;

        string authority = parts[1];
        if (!authority.EndsWith(":443", StringComparison.Ordinal) || authority.Length > 258)
            return false;
        string candidate = authority[..^4];
        if (candidate.Length is < 1 or > 253 || candidate[0] == '.' ||
            candidate[^1] == '.' ||
            candidate.Contains("..", StringComparison.Ordinal) ||
            !candidate.All(ch => char.IsAsciiLetterOrDigit(ch) || ch is '.' or '-'))
            return false;
        if (candidate.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            candidate.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            candidate.EndsWith(".local", StringComparison.OrdinalIgnoreCase))
            return false;
        if (IPAddress.TryParse(candidate, out IPAddress? ip) && !IsPublicIPv4(ip))
            return false;
        host = candidate;
        return true;
    }

    public static bool IsPublicIPv4(IPAddress ip)
    {
        if (ip.AddressFamily != AddressFamily.InterNetwork) return false;
        byte[] a = ip.GetAddressBytes();
        return a[0] != 0 && a[0] != 10 && a[0] != 127 && a[0] < 224 &&
               !(a[0] == 169 && a[1] == 254) &&
               !(a[0] == 172 && a[1] is >= 16 and <= 31) &&
               !(a[0] == 192 && a[1] == 168) &&
               !(a[0] == 100 && a[1] is >= 64 and <= 127) &&
               !(a[0] == 198 && a[1] is >= 18 and <= 19);
    }

    private async Task AcceptLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            TcpClient client;
            try { client = await _listener.AcceptTcpClientAsync(ct); }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (SocketException) when (ct.IsCancellationRequested) { break; }
            _ = ServeAsync(client, ct); // Each tunnel owns and closes its streams.
        }
    }

    private static async Task<string?> ReadHeaderAsync(Stream stream, CancellationToken ct)
    {
        byte[] one = new byte[1];
        using var buffer = new MemoryStream();
        int sequence = 0;
        while (buffer.Length < 8192)
        {
            int received = await stream.ReadAsync(one.AsMemory(), ct);
            if (received == 0) return null;
            buffer.WriteByte(one[0]);
            sequence = (sequence, one[0]) switch
            {
                (0, 13) => 1, (1, 10) => 2,
                (2, 13) => 3, (3, 10) => 4,
                (_, 13) => 1, _ => 0
            };
            if (sequence == 4)
                return Encoding.ASCII.GetString(buffer.ToArray());
        }
        return null;
    }

    private async Task<Stream> OpenPhysicalAsync(string hostname, CancellationToken ct)
    {
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(hostname, ct);
        Exception? failure = null;
        foreach (IPAddress ip in addresses.Where(IsPublicIPv4))
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                socket.Bind(new IPEndPoint(_physicalAddress, 0));
                await socket.ConnectAsync(new IPEndPoint(ip, 443), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex) { failure = ex; socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new IOException("MAX недоступен через физический сетевой интерфейс.", failure);
    }

    private async Task<Stream> OpenSystemAsync(string hostname, CancellationToken ct)
    {
        Uri? systemProxy = MaxSystemProxyTunnel.FindConfiguredProxy(hostname);
        if (systemProxy is not null)
        {
            MaxSystemProxyTunnel.Tunnel result =
                await MaxSystemProxyTunnel.OpenAsync(systemProxy, hostname, 443, ct);
            // The stream owns its socket or TLS wrapper and is disposed after the tunnel.
            return result.Stream;
        }

        // Other hosts continue through the ordinary system route (TUN/VPN).
        IPAddress[] addresses = await Dns.GetHostAddressesAsync(hostname, ct);
        Exception? failure = null;
        foreach (IPAddress ip in addresses.Where(IsPublicIPv4))
        {
            var socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);
            try
            {
                await socket.ConnectAsync(new IPEndPoint(ip, 443), ct);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch (SocketException ex) { failure = ex; socket.Dispose(); }
            catch { socket.Dispose(); throw; }
        }
        throw new IOException("Узел недоступен через системную сеть.", failure);
    }

    private async Task ServeAsync(TcpClient client, CancellationToken shutdown)
    {
        using (client)
        {
            try
            {
                using var requestTimeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
                requestTimeout.CancelAfter(TimeSpan.FromSeconds(10));
                Stream browser = client.GetStream();
                string? header = await ReadHeaderAsync(browser, requestTimeout.Token);
                string? first = header?.Split("\r\n", 2, StringSplitOptions.None)[0];
                if (!TryReadConnectHost(first, out string hostname))
                {
                    await browser.WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 403 Forbidden\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"),
                        shutdown);
                    return;
                }

                using var connectTimeout = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
                connectTimeout.CancelAfter(TimeSpan.FromSeconds(20));
                using Stream remote = IsMaxHost(hostname)
                    ? await OpenPhysicalAsync(hostname, connectTimeout.Token)
                    : await OpenSystemAsync(hostname, connectTimeout.Token);
                await browser.WriteAsync(Encoding.ASCII.GetBytes(
                    "HTTP/1.1 200 Connection Established\r\n\r\n"), shutdown);

                using var relay = CancellationTokenSource.CreateLinkedTokenSource(shutdown);
                Task outgoing = browser.CopyToAsync(remote, 16384, relay.Token);
                Task incoming = remote.CopyToAsync(browser, 16384, relay.Token);
                await Task.WhenAny(outgoing, incoming);
                relay.Cancel();
                try { await Task.WhenAll(outgoing, incoming); }
                catch (OperationCanceledException) when (relay.IsCancellationRequested) { }
                catch (IOException) when (relay.IsCancellationRequested) { }
            }
            catch (Exception ex) when (ex is IOException or SocketException or
                OperationCanceledException or UnauthorizedAccessException or InvalidDataException or
                System.Security.Authentication.AuthenticationException)
            {
                // No tunnel body, hostname, credentials, cookies or WebAppData in logs.
                try
                {
                    await client.GetStream().WriteAsync(Encoding.ASCII.GetBytes(
                        "HTTP/1.1 502 Bad Gateway\r\nConnection: close\r\nContent-Length: 0\r\n\r\n"),
                        CancellationToken.None);
                }
                catch (Exception) { /* Browser may have closed the tunnel. */ }
            }
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        _shutdown.Cancel();
        _listener.Stop();
        _shutdown.Dispose();
    }
}
