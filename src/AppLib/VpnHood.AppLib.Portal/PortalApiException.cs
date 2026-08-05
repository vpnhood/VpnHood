namespace VpnHood.AppLib.Portal;

/// <summary>A Portal API action answered the error envelope (or not the envelope at all).</summary>
public class PortalApiException(string message, int statusCode) : Exception(message)
{
    public int StatusCode { get; } = statusCode;
}
