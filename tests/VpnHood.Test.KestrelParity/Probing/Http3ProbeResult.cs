using System.Net.Quic;

namespace VpnHood.Test.KestrelParity.Probing;

// QuicError distinguishes a request reset from a connection close; matching only the numeric code loses that.
internal sealed record Http3ProbeResult(string Bytes, bool Fin, QuicError? Error, long? ApplicationErrorCode);
