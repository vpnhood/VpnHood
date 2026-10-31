namespace VpnHood.Test.KestrelParity.Probing;

internal sealed record RequestEnding(Http3ProbeResult Response, Http3ProbeResult Write,
    TimeSpan SentAt, TimeSpan ResponseEndedAt, TimeSpan WriteEndedAt);
