using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Proxies.Management.Checks;

// A run of checks' way to the connector's places for TLS handshakes: those beside the session (places) once the run
// goes beside it, from its start, or once a proxy reached a server, which a pass elsewhere gives too and a failed
// connection takes back; before that, those of the checks before a server (placesBeforeServer). It reads which as each
// check comes, and again as one that waited gets its place. A place for each check that comes; those under way go on
// beside them. Open where the connector gives no places for the case
internal sealed class HandshakeGate(
    SemaphoreSlim? places,
    SemaphoreSlim? placesBeforeServer,
    bool isBesideSession,
    Func<bool> isServerReached)
{
    // whether a check that comes now takes a place
    public bool IsLimited => GetPlaces() != null;

    // a place free now, taken at once, or null where the gate is open now: true for both; false where the check is to
    // wait for one
    public bool TryEnter(out SemaphoreSlim? place)
    {
        place = GetPlaces();
        if (place == null || place.Wait(0))
            return true;

        place = null;
        return false;
    }

    // a place, which the caller releases, or null where the gate is open
    public async Task<SemaphoreSlim?> Enter(CancellationToken cancellationToken)
    {
        while (GetPlaces() is { } currentPlaces) {
            await currentPlaces.WaitAsync(cancellationToken).Vhc();
            if (GetPlaces() == currentPlaces)
                return currentPlaces;

            // a server reached, or taken back, while it waited: it takes its turn where the checks that come now do
            currentPlaces.Release();
        }

        return null;
    }

    private SemaphoreSlim? GetPlaces() => isBesideSession || isServerReached() ? places : placesBeforeServer;
}
