using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Proxies.Management;

// A run of checks' way to the connector's places for TLS handshakes: open until the run goes beside the session, from
// its start, or once a proxy reached a server, which it reads as each check comes, as a pass elsewhere counts too. Then
// a place for each check that comes; those under way go on beside them. Always open where the connector has no places
internal sealed class HandshakeGate(SemaphoreSlim? places, bool isBesideSession, Func<bool> isServerReached)
{
    // whether a check that comes now takes a place
    public bool IsLimited => places != null && (isBesideSession || isServerReached());

    // a place, which the caller releases, or null where the gate is open
    public async Task<SemaphoreSlim?> Enter(CancellationToken cancellationToken)
    {
        if (places == null || !(isBesideSession || isServerReached()))
            return null;

        await places.WaitAsync(cancellationToken).Vhc();
        return places;
    }
}
