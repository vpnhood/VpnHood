namespace VpnHood.AppLib.Portal;

/// <summary>A Portal API call failed. <see cref="Code"/> is the contract to branch on;
/// the message is prose the portal may reword.</summary>
public class PortalApiException(string message, int statusCode, string? code = null) : Exception(message)
{
    /// <summary>Stable machine code from the problem+json body; null when the failure
    /// carried no portal error (a proxy page, a web-server 404, a transport error).</summary>
    public string? Code { get; } = code;

    public int StatusCode { get; } = statusCode;
}
