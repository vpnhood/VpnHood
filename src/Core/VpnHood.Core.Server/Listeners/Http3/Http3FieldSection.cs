namespace VpnHood.Net.Http3;

// A peer can finish its QUIC stream before the declared HEADERS frame ends. Keep that observation
// separate from a complete field section so the request handler can preserve the reference's FIN.
internal sealed record Http3FieldSection(
    IReadOnlyList<KeyValuePair<string, string>> Fields,
    bool Completed,
    bool ExceedsRequestLimits);
