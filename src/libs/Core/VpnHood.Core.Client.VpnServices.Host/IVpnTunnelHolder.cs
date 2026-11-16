namespace VpnHood.Core.Client.VpnServices.Host;

/// <summary>
/// Defines whoever holds the machine's one tunnel (<see cref="IVpnTunnelLock"/>).
/// </summary>
public interface IVpnTunnelHolder
{
    /// <summary>
    /// Tells the holder another app asked for the tunnel, which the hold lets go once this returns: the holder's adapter
    /// must be gone by then, or the next app's routes would meet its own.
    /// </summary>
    /// <remarks>The rest of its disconnect follows in its own time.</remarks>
    void OnTunnelRevoked();
}
