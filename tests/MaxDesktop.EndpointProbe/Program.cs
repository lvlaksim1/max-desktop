using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MaxDesktop.Protocol;

bool physical = args.Any(arg => arg.Equals("--physical", StringComparison.OrdinalIgnoreCase));
bool requireSuccess = args.Any(arg => arg.Equals("--require-success", StringComparison.OrdinalIgnoreCase));
if (physical)
{
    Console.WriteLine("Anonymous native Android physical TCP SESSION_INIT; no phone, OTP, login token or SMS.");
    using var transport = new NativeMaxTransport(new NoSessionStore(), bindPhysicalInterface: true);
    var result = await transport.ProbeConnectionAsync();
    bool valid = result.State == ConnectionState.Disconnected &&
        result.Error?.Contains("SESSION_INIT работают", StringComparison.Ordinal) == true;
    Console.WriteLine("ANDROID_PHYSICAL_INIT=" + (valid ? "CONFIRMED" : "NOT_CONFIRMED"));
    if (!valid)
        Console.WriteLine("ANDROID_PHYSICAL_ERROR_CLASS=" + (result.State == ConnectionState.Failed ? "CONNECT_OR_INIT" : "STATE"));
    return requireSuccess && !valid ? 1 : 0;
}

// Anonymous, bounded production-endpoint discovery. No phone, SMS or account data.
foreach (string hostname in new[] { "api2.oneme.ru", "api.oneme.ru", "api-tg.oneme.ru" })
{
    string stage = "DNS";
    try
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(19));
        var hosts = await System.Net.Dns.GetHostAddressesAsync(hostname, timeout.Token);
        Console.WriteLine($"HOST={hostname} DNS_COUNT={hosts.Length}");
        using var tcp = new TcpClient();
        stage = "TCP";
        await tcp.ConnectAsync(hostname, 443, timeout.Token);
        stage = "TLS";
        using var ssl = new SslStream(tcp.GetStream(), false, MaxTlsCertificateValidator.Validate);
        await ssl.AuthenticateAsClientAsync(new SslClientAuthenticationOptions
        {
            TargetHost = hostname,
            EnabledSslProtocols = SslProtocols.None,
            CertificateRevocationCheckMode = X509RevocationMode.Online
        }, timeout.Token);
        Console.WriteLine($"HOST={hostname} TLS_OK={ssl.SslProtocol}");
        stage = "SESSION_INIT";

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
        await ssl.WriteAsync(MaxTcpFraming.Encode(1, 6, payload), timeout.Token);
        await ssl.FlushAsync(timeout.Token);
        byte[] header = new byte[MaxTcpFraming.HeaderSize];
        await ssl.ReadExactlyAsync(header, timeout.Token);
        byte[] body = new byte[MaxTcpFraming.PayloadLength(header)];
        if(body.Length > 0) await ssl.ReadExactlyAsync(body, timeout.Token);
        var answer = MaxTcpFraming.Decode(header, body);
        Console.WriteLine($"HOST={hostname} INIT_RESP_COMMAND={answer.Command} OPCODE={answer.Opcode} CALLSSEED={answer.Payload.ContainsKey("callsSeed")}");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"HOST={hostname} ERROR_STAGE={stage} TYPE={ex.GetType().Name} ROOT={ex.GetBaseException().GetType().Name}");
    }
    // Avoid rapid repeated connection attempts to provider infrastructure.
    await Task.Delay(TimeSpan.FromSeconds(5));
}
return 0;

sealed class NoSessionStore : IMaxSessionStore
{
    public MaxSavedSession? Load() => null;
    public void Save(MaxSavedSession session) => throw new InvalidOperationException("Anonymous tests must not save sessions.");
    public void Delete() { }
}
