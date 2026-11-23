using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// A stop, as it was marked: what it found of the session it ends, so nothing of a newer connect is taken for it
internal sealed class RequestedDisconnect
{
    // the connect the stop cancels
    public required CancellationTokenSource ConnectCts { get; init; }

    // the end of every connect begun so far, which the stop waits for
    public required Task ConnectsEnded { get; init; }

    public required int SessionId { get; init; }

    // the session's client, where made, which closes first
    public required VpnHoodClient? Client { get; init; }

    // the last registered adapter, which may be an earlier session's: a revoke takes it down at once
    public required IVpnAdapter? VpnAdapter { get; init; }

    // why, or null for a person's own stop
    public required Exception? Exception { get; init; }
}
