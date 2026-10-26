using System.Text.RegularExpressions;

namespace MaxDesktop.Protocol;

/// <summary>
/// Validate the user-selected OPEN_APP destination before WebView2 navigation.
/// This is not a MAX-signed WebAppData generator.
/// </summary>
public static class MaxMiniAppLink
{
    private static readonly Regex Handle = new(@"^@?[A-Za-z0-9_]{3,64}$", RegexOptions.Compiled);
    private static readonly Regex StartParameter = new(@"^[A-Za-z0-9_-]{1,512}$", RegexOptions.Compiled);

    public static bool TryResolve(string? webApp, string? payload, out Uri? destination)
    {
        destination = null;
        if (string.IsNullOrWhiteSpace(webApp) || webApp.Length > 2048) return false;
        string candidate = webApp.Trim();
        if (Handle.IsMatch(candidate))
            candidate = "https://max.ru/" + candidate.TrimStart('@');

        if (!Uri.TryCreate(candidate, UriKind.Absolute, out var url) ||
            url.Scheme != Uri.UriSchemeHttps || string.IsNullOrWhiteSpace(url.Host) ||
            url.UserInfo.Length != 0 || url.Port != 443)
            return false;

        // A MAX bot deep link needs ?startapp to request mini-app launch. Do
        // not invent an authenticated initData string or contact identity.
        if (url.Host.Equals("max.ru", StringComparison.OrdinalIgnoreCase) &&
            url.AbsolutePath.Trim('/').Length > 0 &&
            !url.Query.Contains("startapp", StringComparison.OrdinalIgnoreCase))
        {
            string param = !string.IsNullOrEmpty(payload) && StartParameter.IsMatch(payload)
                ? Uri.EscapeDataString(payload) : "";
            string divider = string.IsNullOrEmpty(url.Query) ? "?" : "&";
            candidate = candidate + divider + "startapp" + (param.Length == 0 ? "" : "=" + param);
            if (!Uri.TryCreate(candidate, UriKind.Absolute, out url)) return false;
        }
        destination = url;
        return true;
    }
}
