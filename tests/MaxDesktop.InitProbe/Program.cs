using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MaxDesktop.Protocol;

const string host = "api2.oneme.ru";
Console.WriteLine("INIT probe: one anonymous SESSION_INIT (opcode=6); no phone, codes or account tokens.");
string phase = "TCP_CONNECT";
try
{
    using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(25));
    using var tcp = new TcpClient();
    await tcp.ConnectAsync(host, 443, timeout.Token);
    phase = "TLS_HANDSHAKE";
    using var tls = new SslStream(tcp.GetStream(), false, MaxTlsCertificateValidator.Validate);
    await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
    {
        TargetHost = host,
        EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
        CertificateRevocationCheckMode = X509RevocationMode.Online
    }, timeout.Token);
    Console.WriteLine($"TLS_OK protocol={tls.SslProtocol}");

    phase = "SESSION_INIT";
    var payload = new Dictionary<string, object?>
    {
        ["deviceId"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
        ["mt_instanceid"] = Convert.ToHexString(RandomNumberGenerator.GetBytes(8)).ToLowerInvariant(),
        ["clientSessionId"] = RandomNumberGenerator.GetInt32(1, 71),
        ["userAgent"] = new Dictionary<string, object?>
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
        }
    };
    byte[] frame = MaxTcpFraming.Encode(1, 6, payload);
    await tls.WriteAsync(frame, timeout.Token);
    await tls.FlushAsync(timeout.Token);

    byte[] header = new byte[MaxTcpFraming.HeaderSize];
    await tls.ReadExactlyAsync(header, timeout.Token);
    byte[] body = new byte[MaxTcpFraming.PayloadLength(header)];
    if (body.Length > 0) await tls.ReadExactlyAsync(body, timeout.Token);
    var response = MaxTcpFraming.Decode(header, body);
    Console.WriteLine($"INIT_RESPONSE command={response.Command} opcode={response.Opcode} flags={response.Flags} bytes={body.Length}");
    Console.WriteLine("INIT_FIELDS " + string.Join(",",
        response.Payload.Keys.Where(k => !k.Contains("token", StringComparison.OrdinalIgnoreCase)).OrderBy(x => x)));
    Console.WriteLine($"INIT_CALLS_SEED_PRESENT={response.Payload.ContainsKey("callsSeed")}");
}
catch (Exception ex)
{
    Console.WriteLine($"INIT_PROBE_ERROR phase={phase} type={ex.GetType().Name} root={ex.GetBaseException().GetType().Name}");
}
// Diagnostic job reports only capability; it never exercises phone verification.
return 0;
