namespace VpnHood.Test.KestrelParity.Probing;

internal sealed record ActiveGoAwayObservation(Http3ProbeResult RejectedRequest, Http3ProbeResult CompletedRequest,
    Http3ProbeResult Close, IReadOnlyList<ulong> ServerGoAwayIds, bool ClosedBeforeOriginalCompleted, TimeSpan IdleElapsed);
