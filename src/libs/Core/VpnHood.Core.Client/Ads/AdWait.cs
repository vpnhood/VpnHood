using VpnHood.Core.Client.Abstractions;

namespace VpnHood.Core.Client.Ads;

// A wait for an ad and the routes it tries in turn; it moves on only under its handler's lock (SessionAdHandler).
// isFollowed: a later wait tries the ad again, as a connect's routes after the VPN follow its try before it, so the
// wait's last route is not the ad's
internal class AdWait(IReadOnlyList<AdRoute> routes, bool isFollowed)
{
    private int _index;

    // null where the ad showed
    public TaskCompletionSource<AdFailure?> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public AdRoute Route => routes[Volatile.Read(ref _index)];
    public bool HasNextRoute => Volatile.Read(ref _index) < routes.Count - 1;
    public bool IsLastRoute => !HasNextRoute && !isFollowed;

    public void MoveNext()
    {
        Volatile.Write(ref _index, _index + 1);
    }
}
