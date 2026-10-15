namespace VpnHood.Core.Client.VpnServices.Host;

// The machine's one tunnel, which every app built on VpnHood takes before it brings an adapter up, so a
// new connect ends the tunnel already up, whichever app made it. As on Android, the new app waits for
// nothing of the old one but its adapter: the holder drops it and lets go at once. A crash lets it go
// with the process, the adapter with it. Only the desktops have one: Android and iOS hand the tunnel from
// one VPN app to another themselves.
public interface IVpnTunnelLock
{
    // The tunnel is the holder's until the returned hold is disposed, or until an app asks for it: the
    // holder hears of that ask once, and the hold lets go as soon as the holder returns.
    Task<IDisposable> Take(IVpnTunnelHolder holder, CancellationToken cancellationToken);
}
