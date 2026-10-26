using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using MaxDesktop.Protocol;

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
