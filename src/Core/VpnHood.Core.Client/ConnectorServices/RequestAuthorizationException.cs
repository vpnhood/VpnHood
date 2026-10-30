namespace VpnHood.Core.Client.ConnectorServices;

// The sender tracks clock corrections; only the server's clock crosses the transport boundary.
internal sealed class RequestAuthorizationException(DateTimeOffset? serverTime)
    : UnauthorizedAccessException
{
    public DateTimeOffset? ServerTime { get; } = serverTime;
}
