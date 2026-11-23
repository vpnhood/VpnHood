namespace VpnHood.Core.Client.VpnServices.Host.Sessions;

// A connect that has begun, with what it needs of the connect it replaces
internal sealed class BegunConnect
{
    // taken when the connect began: a newer connect disposes its source
    public required CancellationToken ConnectToken { get; init; }

    public required int SessionId { get; init; }

    // the connect's end, which a stop of its session waits for
    public required TaskCompletionSource Ended { get; init; }

    // the end of every connect begun before it and of the client it replaced, which its own failure's end waits for
    public required Task PredecessorsEnded { get; init; }

    // the connect it replaces: that one's token source, and its client with that client's close
    public required CancellationTokenSource OldConnectCts { get; init; }
    public required VpnHoodClient? OldClient { get; init; }
    public required TaskCompletionSource OldClientClosed { get; init; }
}
