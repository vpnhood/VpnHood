namespace VpnHood.Core.Client.VpnServices.Host;

// Whoever holds the machine's one tunnel (IVpnTunnelLock).
public interface IVpnTunnelHolder
{
    // Another app asked for the tunnel, and the hold lets it go once this returns: the holder's adapter
    // must be gone by then, or the next app's routes would meet its own. The rest of its disconnect
    // follows in its own time.
    void OnTunnelRevoked();
}
