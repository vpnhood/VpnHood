namespace VpnHood.Core.Client.ConnectorServices;

// Retain the server's clock and the proof's correction number, never its token, for a single retry.
internal sealed class RequestAuthorizationException(DateTimeOffset? serverTime, long clockCorrectionVersion)
    : UnauthorizedAccessException
{
    public DateTimeOffset? ServerTime { get; } = serverTime;
    public long ClockCorrectionVersion { get; } = clockCorrectionVersion;
}
