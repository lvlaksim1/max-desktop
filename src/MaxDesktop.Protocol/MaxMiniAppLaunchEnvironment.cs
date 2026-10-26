namespace MaxDesktop.Protocol;

/// <summary>
/// Reports only the nonsecret, declared WebAppPlatform from a signed MAX URL.
/// No rewriting of WebAppData, URL, platform, device identity or user-agent.
/// </summary>
public static class MaxMiniAppLaunchEnvironment
{
    public static string PlatformDescription(Uri serverAddress)
    {
        ArgumentNullException.ThrowIfNull(serverAddress);
        foreach (string source in new[] { serverAddress.Fragment.TrimStart('#'),
                                            serverAddress.Query.TrimStart('?') })
        {
            foreach (string entry in source.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int separator = entry.IndexOf('=');
                if (separator <= 0) continue;
                if (!entry[..separator].Equals("WebAppPlatform", StringComparison.OrdinalIgnoreCase))
                    continue;
                string value = entry[(separator + 1)..];
                return value switch
                {
                    "android" => "android",
                    "ios" => "ios",
                    "web" => "web",
                    "desktop" => "desktop",
                    _ => "не указана однозначно"
                };
            }
        }
        return "не указана";
    }
}
