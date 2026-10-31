namespace VpnHood.Net.Http3;

internal readonly record struct Http3RequestHead(bool Completed, bool ExceedsRequestLimits,
    Http3Request Request);
