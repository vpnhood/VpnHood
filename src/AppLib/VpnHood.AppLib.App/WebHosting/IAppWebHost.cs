using System.Net;

namespace VpnHood.AppLib.WebHosting;

// A place the app's own UI and API are served from. There are two, and they are the same thing bound
// differently: the local one on loopback, which the app's web view loads, and the remote one on the
// LAN addresses, which a phone pairs with. One shape for both, so the app asks the same questions of
// either. A head hands in an IAppWebHostFactory through AppOptions; a head that hands in none has no
// web host, AppFeatures.IsRemoteAccessSupported is false, and the pairing calls throw.
//
// This and its factory are one contract in two types - nobody implements either alone - so they live
// together, and next to the app whose API they serve rather than among the providers a head supplies.
public interface IAppWebHost : IDisposable
{
    // The address to load, the host bound and answering by the time it returns. The first call binds;
    // a later one is the caller's own "unreachable" signal - a failed load, a resume - and costs one
    // real connect. The remote host also re-reads its addresses, since a network can move under an
    // open pairing screen. Nothing is bound until someone asks.
    Task<Uri> EnsureStarted(CancellationToken cancellationToken);

    // Let the listeners go. The local host is never stopped before Dispose; the remote one is, by the
    // screen that opened it or by a phone unpairing itself. Does nothing while IsAlwaysOn.
    Task Stop(CancellationToken cancellationToken);

    // A listener was rebound under a caller that had already loaded from it, so what it loaded may be
    // half fetched. The web view reloads on this; a phone simply retries.
    event EventHandler? Restarted;

    // Bound right now, or held open for the life of the process.
    bool IsActive { get; }

    // A debug build or /remote-access holds the remote host open for the whole process and asks for
    // no pairing, so no screen has to stay open for it. Always false on the local host.
    bool IsAlwaysOn { get; }

    // Where to dial in, best guess first, each carrying whatever the listener asks for on the first
    // hit. Those of the last EnsureStarted; empty before it and after a Stop.
    IReadOnlyList<Uri> Urls { get; }

    // Devices seen within the last few seconds - presence, not sessions. Always empty on the local
    // host, which serves nothing but the device's own web view.
    IReadOnlyList<IPAddress> ConnectedDevices { get; }
}
