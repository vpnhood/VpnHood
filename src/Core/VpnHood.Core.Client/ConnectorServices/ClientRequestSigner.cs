using VpnHood.Core.Tunneling.ProbeResistance;

namespace VpnHood.Core.Client.ConnectorServices;

internal sealed class ClientRequestSigner(byte[] farmSecret, string hostName)
{
    private readonly RequestProof _requestProof = new(farmSecret);
    private readonly Lock _proofLock = new();
    private string? _proofToken;
    private long _proofCreatedTick;
    private DateTimeOffset _proofIssuedAt;
    private TimeSpan _clockOffset;
    private long _clockCorrectionVersion;

    public long ClockCorrectionVersion {
        get {
            lock (_proofLock)
                return _clockCorrectionVersion;
        }
    }

    public string Sign()
    {
        lock (_proofLock) {
            // Share a proof across streams, renewed by whichever clock says so first: the tick, so a wall-clock change
            // never delays it, and the wall clock, as the tick stops while the machine sleeps - a session that lives
            // through a sleep would otherwise send the server a proof it has expired until the tick catches up.
            var nowTick = Environment.TickCount64;
            var now = DateTimeOffset.UtcNow + _clockOffset;
            var renewAge = RequestProof.Lifetime - TimeSpan.FromMinutes(1);
            if (_proofToken == null ||
                TimeSpan.FromMilliseconds(nowTick - _proofCreatedTick) >= renewAge ||
                now - _proofIssuedAt >= renewAge) {
                _proofToken = _requestProof.Create(hostName, RequestProof.ProtocolVersion, now);
                _proofCreatedTick = nowTick;
                _proofIssuedAt = now;
            }
            return _proofToken;
        }
    }

    public bool TryCorrectClock(DateTimeOffset? serverTime, long clockCorrectionVersion)
    {
        lock (_proofLock) {
            // Another request corrected the clock after this attempt started. Retry with that correction;
            // an older response must not overwrite it with a stale Date.
            if (clockCorrectionVersion < _clockCorrectionVersion)
                return true;
            if (serverTime == null)
                return false;

            var offset = serverTime.Value - DateTimeOffset.UtcNow;
            if ((offset - _clockOffset).Duration() <= RequestProof.ClockTolerance)
                return false;

            // Trust the verified TLS peer even after another device-clock change or a large offset.
            _clockOffset = offset;
            _clockCorrectionVersion++;
            _proofToken = null;
            return true;
        }
    }
}
