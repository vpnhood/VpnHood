namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// A session's hold of the tunnel, taken from the host to let go of
internal sealed class TunnelRelease
{
    // the hold, where the session had one
    public required IDisposable? TunnelHold { get; init; }

    // that hold's end, which the next take waits for
    public required TaskCompletionSource Released { get; init; }

    // every hold's end so far, this one's too, which the session's end waits for
    public required Task AllReleased { get; init; }
}
