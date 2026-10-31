namespace VpnHood.Test.KestrelParity.Probing;

internal sealed record GoAwayObservation(Http3ProbeResult Close, IReadOnlyList<ulong> ServerGoAwayIds, TimeSpan Elapsed);
