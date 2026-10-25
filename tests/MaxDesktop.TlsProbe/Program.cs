using MaxDesktop.Protocol;
using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;

const string host = "api2.oneme.ru";
const int port = 443;
Console.WriteLine("MAX TCP/TLS handshake-only test; NO login and NO user data.");
try
{
    var ips = await Dns.GetHostAddressesAsync(host);
    Console.WriteLine($"DNS_OK count={ips.Length}");
}
catch (Exception e)
{
    Console.WriteLine($"DNS_ERROR type={e.GetType().Name}");
    return 0; // diagnostics, not a release gate
}

foreach (var option in new[] { ("SystemRoot", SslProtocols.None), ("BundledMaxRoot", SslProtocols.None), ("BundledExplicit", SslProtocols.Tls12 | SslProtocols.Tls13), ("SystemTls12", SslProtocols.Tls12) })
{
    var (trust, protocol) = option;
    Console.WriteLine($"PROBE_BEGIN TRUST={trust} TLS_PREF={protocol}");
    string phase = "TCP_CONNECT";
    try
    {
        using var token = new CancellationTokenSource(TimeSpan.FromSeconds(13));
        using var tcp = new TcpClient();
        await tcp.ConnectAsync(host, port, token.Token);
        Console.WriteLine("TCP_OK");
        phase = "TLS_HANDSHAKE";
        using var ssl = new SslStream(tcp.GetStream(), false,
            (sender, certificate, chain, errors) =>
            {
                if (errors != SslPolicyErrors.None)
                    Console.WriteLine($"TLS_CERTIFICATE_POLICY_ERRORS {errors}");
                bool allowed = trust.StartsWith("Bundled", StringComparison.Ordinal)
                    ? MaxTlsCertificateValidator.Validate(sender, certificate, chain, errors)
                    : errors == SslPolicyErrors.None;
                Console.WriteLine($"TLS_CHAIN_VALID={allowed}");
                return allowed;
            });
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = protocol,
            CertificateRevocationCheckMode = X509RevocationMode.Online
        }, token.Token);
        Console.WriteLine($"TLS_OK version={ssl.SslProtocol} cipher={ssl.NegotiatedCipherSuite}");
    }
    catch (Exception e)
    {
        var core = e.GetBaseException();
        Console.WriteLine($"PROBE_ERROR phase={phase} exception={e.GetType().Name} root={core.GetType().Name}");
        // Exception text often carries host, proxy or OS-specific data.
        // Do not print it into public CI logs.
    }
    Console.WriteLine($"PROBE_END TRUST={trust} TLS_PREF={protocol}");
    await Task.Delay(TimeSpan.FromSeconds(5));
}

return 0;
