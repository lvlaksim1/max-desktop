using System.Net;
using System.Net.Http;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace MaxDesktop.Protocol;

/// <summary>
/// Builds a raw TCP tunnel using the current Windows/.NET proxy settings.
/// TLS validation of the final MAX endpoint is performed separately, above
/// this tunnel, by MaxTlsCertificateValidator. User/account tokens are never
/// passed in the proxy handshake.
/// </summary>
public static class MaxSystemProxyTunnel
{
    public sealed record Tunnel(TcpClient Socket, Stream Stream, string Kind);

    /// <summary>
    /// Returns null when the target is configured for direct connections,
    /// when no proxy is available, or for an unsupported proxy scheme.
    /// Never exposes proxy URL / proxy credentials through diagnostics.
    /// </summary>
    public static Uri? FindConfiguredProxy(string hostname)
    {
        try
        {
            Uri target = new($"https://{hostname}:443/");
            IWebProxy system = HttpClient.DefaultProxy;
            if (system.IsBypassed(target)) return null;
            Uri? proxy = system.GetProxy(target);
            if (proxy is null || proxy.Equals(target)) return null;
            if (proxy.Scheme is not ("http" or "https" or "socks5")) return null;
            if (string.Equals(proxy.Host, target.Host, StringComparison.OrdinalIgnoreCase) &&
                proxy.Port == target.Port) return null;
            return proxy;
        }
        catch (Exception ex) when (ex is UriFormatException or InvalidOperationException
            or NotSupportedException or WebException)
        {
            return null;
        }
    }

    /// <summary>
    /// Supports unauthenticated HTTP CONNECT, HTTPS CONNECT and SOCKS5.
    /// Returns a tunnelled stream over which the caller MUST establish normal
    /// end-to-end TLS with hostname/certificate validation.
    /// </summary>
    public static async Task<Tunnel> OpenAsync(
        Uri proxy, string targetHost, int targetPort, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(proxy);
        if (string.IsNullOrWhiteSpace(targetHost) || targetPort is < 1 or > 65535)
            throw new ArgumentException("Invalid MAX tunnel destination.");
        if (proxy.Scheme is not ("http" or "https" or "socks5"))
            throw new NotSupportedException("Системный прокси использует неподдерживаемый протокол.");
        if (!string.IsNullOrEmpty(proxy.UserInfo))
            throw new NotSupportedException("Прокси с учётными данными не поддерживается. Пароль прокси не передавался.");

        int proxyPort = proxy.IsDefaultPort
            ? proxy.Scheme switch { "http" => 80, "https" => 443, _ => 1080 }
            : proxy.Port;

        var tcp = new TcpClient();
        Stream? connection = null;
        try
        {
            await tcp.ConnectAsync(proxy.Host, proxyPort, cancellationToken);
            connection = tcp.GetStream();

            if (proxy.Scheme == "https")
            {
                var tlsToProxy = new SslStream(connection, leaveInnerStreamOpen: false);
                await tlsToProxy.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
                {
                    TargetHost = proxy.Host,
                    EnabledSslProtocols = SslProtocols.None,
                    CertificateRevocationCheckMode = X509RevocationMode.Online
                }, cancellationToken);
                connection = tlsToProxy;
            }

            if (proxy.Scheme == "socks5")
                await Socks5ConnectAsync(connection, targetHost, targetPort, cancellationToken);
            else
                await HttpConnectAsync(connection, targetHost, targetPort, cancellationToken);

            return new Tunnel(tcp, connection, proxy.Scheme.ToUpperInvariant());
        }
        catch
        {
            connection?.Dispose();
            tcp.Dispose();
            throw;
        }
    }

    private static async Task HttpConnectAsync(
        Stream stream, string host, int port, CancellationToken ct)
    {
        // Do not send Proxy-Authorization, cookies, phone numbers or tokens.
        string request = $"CONNECT {host}:{port} HTTP/1.1\r\nHost: {host}:{port}\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(request), ct);
        await stream.FlushAsync(ct);

        using var buffer = new MemoryStream();
        byte[] one = new byte[1];
        int consecutive = 0;
        while (buffer.Length < 8192)
        {
            int read = await stream.ReadAsync(one, ct);
            if (read == 0) throw new IOException("Прокси закрыл соединение до ответа CONNECT.");
            buffer.WriteByte(one[0]);
            consecutive = one[0] switch
            {
                (byte)'\r' when consecutive == 0 or 2 => consecutive + 1,
                (byte)'\n' when consecutive == 1 or 3 => consecutive + 1,
                _ => 0
            };
            if (consecutive == 4) break;
        }
        if (consecutive != 4) throw new InvalidDataException("Ответ HTTP CONNECT слишком большой.");
        string header = Encoding.ASCII.GetString(buffer.ToArray());
        string firstLine = header.Split("\r\n", 2, StringSplitOptions.None)[0];
        if (!firstLine.StartsWith("HTTP/1.", StringComparison.Ordinal) ||
            firstLine.Length < 12 || firstLine[8] != ' ')
            throw new InvalidDataException("Прокси вернул некорректный HTTP-ответ.");
        string status = firstLine.Substring(9, 3);
        if (status == "407")
            throw new UnauthorizedAccessException("Прокси требует отдельной авторизации.");
        if (status != "200")
            throw new IOException($"Прокси отказал в CONNECT (HTTP {status}).");
    }

    private static async Task Socks5ConnectAsync(
        Stream stream, string host, int port, CancellationToken ct)
    {
        await stream.WriteAsync(new byte[] { 0x05, 0x01, 0x00 }, ct);
        byte[] choice = new byte[2];
        await stream.ReadExactlyAsync(choice, ct);
        if (choice[0] != 0x05 || choice[1] != 0x00)
            throw new UnauthorizedAccessException("SOCKS5 требует неподдерживаемую авторизацию.");

        byte[] target = Encoding.ASCII.GetBytes(host);
        if (target.Length is 0 or > 255)
            throw new InvalidDataException("Имя узла слишком длинное для SOCKS5.");

        byte[] request = new byte[7 + target.Length];
        request[0] = 0x05; request[1] = 0x01; request[2] = 0x00;
        request[3] = 0x03; request[4] = (byte)target.Length;
        target.CopyTo(request, 5);
        request[^2] = (byte)(port >> 8);
        request[^1] = (byte)port;
        await stream.WriteAsync(request, ct);

        byte[] response = new byte[4];
        await stream.ReadExactlyAsync(response, ct);
        if (response[0] != 0x05 || response[2] != 0)
            throw new InvalidDataException("SOCKS5 вернул неправильную версию ответа.");
        if (response[1] != 0)
            throw new IOException($"SOCKS5 отказал в подключении (код {response[1]}).");

        int addressLength = response[3] switch
        {
            0x01 => 4,
            0x04 => 16,
            0x03 => await ReadLengthAsync(stream, ct),
            _ => throw new InvalidDataException("SOCKS5 вернул неизвестный тип адреса.")
        };
        // Consume the bound address and port exactly; don't swallow TLS bytes.
        byte[] remainder = new byte[addressLength + 2];
        await stream.ReadExactlyAsync(remainder, ct);
    }

    private static async Task<int> ReadLengthAsync(Stream stream, CancellationToken ct)
    {
        byte[] length = new byte[1];
        await stream.ReadExactlyAsync(length, ct);
        return length[0];
    }
}
