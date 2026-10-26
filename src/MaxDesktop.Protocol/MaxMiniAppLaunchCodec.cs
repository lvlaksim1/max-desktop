using System.Globalization;
using System.Net;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MaxDesktop.Protocol;

/// <summary>
/// BOT_WEB_APP (opcode 160) returns a signed, short-lived login URL.
/// The URL is a credential: never log, persist, report or disclose it.
/// </summary>
public static class MaxMiniAppLaunchCodec
{
    public const ushort Opcode = 160;
    public const int MaxAddressLength = 8192;
    private static readonly Regex StartPattern = new(@"^[A-Za-z0-9_-]{1,512}$",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    public static Dictionary<string, object?> CreateNativeRequest(
        long botId, long chatId, string? startParam)
    {
        ValidateIdsAndStart(botId, chatId, startParam);
        var request = new Dictionary<string, object?>
        {
            ["botId"] = botId,
            ["chatId"] = chatId
        };
        if (!string.IsNullOrEmpty(startParam)) request["startParam"] = startParam;
        return request;
    }

    public static object CreateWebRequest(long botId, long chatId, string? startParam)
    {
        ValidateIdsAndStart(botId, chatId, startParam);
        if (string.IsNullOrEmpty(startParam)) return new { botId, chatId };
        return new { botId, chatId, startParam };
    }

    private static void ValidateIdsAndStart(long botId, long chatId, string? startParam)
    {
        if (botId <= 0) throw new ArgumentOutOfRangeException(nameof(botId));
        if (chatId == 0) throw new ArgumentOutOfRangeException(nameof(chatId));
        if (startParam is not null && startParam.Length > 0 && !StartPattern.IsMatch(startParam))
            throw new ArgumentException("MAX вернул недопустимый параметр запуска мини-приложения.",
                nameof(startParam));
    }

    public static Uri DecodeNative(Dictionary<string, object?> payload)
    {
        if (!payload.TryGetValue("url", out object? raw) || raw is not string url)
            throw new InvalidDataException("MAX не предоставил адрес запуска мини-приложения.");
        return ValidateSignedAddress(url);
    }

    public static Uri DecodeWeb(JsonElement payload)
    {
        if (payload.ValueKind != JsonValueKind.Object ||
            !payload.TryGetProperty("url", out JsonElement raw) ||
            raw.ValueKind != JsonValueKind.String)
            throw new InvalidDataException("MAX не предоставил адрес запуска мини-приложения.");
        return ValidateSignedAddress(raw.GetString());
    }

    private static Uri ValidateSignedAddress(string? raw)
    {
        // Never put the rejected address, its query or fragment into exceptions.
        if (string.IsNullOrWhiteSpace(raw) || raw.Length > MaxAddressLength ||
            !Uri.TryCreate(raw, UriKind.Absolute, out Uri? url) ||
            url.Scheme != Uri.UriSchemeHttps || url.UserInfo.Length != 0 ||
            string.IsNullOrWhiteSpace(url.Host) || url.Port != 443 ||
            url.Host.Equals("localhost", StringComparison.OrdinalIgnoreCase) ||
            url.Host.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase) ||
            url.Host.EndsWith(".local", StringComparison.OrdinalIgnoreCase) ||
            (IPAddress.TryParse(url.Host, out IPAddress? ip) && !IsPublic(ip)))
            throw new InvalidDataException("MAX предоставил недопустимый HTTPS-адрес мини-приложения.");
        return url;
    }

    private static bool IsPublic(IPAddress ip)
    {
        if (ip.AddressFamily != System.Net.Sockets.AddressFamily.InterNetwork)
            return false;
        byte[] bytes = ip.GetAddressBytes();
        return MiniAppNetworkProxy.IsPublicIPv4(ip) &&
            !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 0) &&
            !(bytes[0] == 192 && bytes[1] == 0 && bytes[2] == 2) &&
            !(bytes[0] == 198 && bytes[1] == 51 && bytes[2] == 100) &&
            !(bytes[0] == 203 && bytes[1] == 0 && bytes[2] == 113);
    }
}
