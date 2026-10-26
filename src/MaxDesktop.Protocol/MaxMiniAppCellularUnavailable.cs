using System.Text.Json;

namespace MaxDesktop.Protocol;

/// <summary>
/// Implements only the original Android MAX negative Mobile ID response when
/// cellular service is unavailable. This is not an SMS request or a proof of
/// device trust. The mini-app/server alone decide whether SMS is offered.
/// </summary>
public static class MaxMiniAppCellularUnavailable
{
    public const string EventName = "WebAppVerifyMobileId";
    public const string ErrorCode = "client.verify_mobile_id.no_cellular";

    public static bool TryCreateResponse(string? requestId, out string responseJson)
    {
        responseJson = string.Empty;
        if (string.IsNullOrWhiteSpace(requestId) || requestId.Length > 256 ||
            requestId.Any(char.IsControl))
            return false;

        // The verified Android APK constructs the same requestId/error envelope
        // for WebAppVerifyMobileId when its cellular HTTP network is unavailable.
        responseJson = JsonSerializer.Serialize(new
        {
            requestId,
            error = new { code = ErrorCode }
        });
        return true;
    }
}
