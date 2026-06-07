namespace VpnHood.Core.VpnAdapters.IosTun;

/// <summary>
/// Implemented by the host NEPacketTunnelProvider (e.g. the Network Extension's IosVpnService)
/// so that <see cref="IosVpnAdapter"/> can signal completion of the deferred start-tunnel
/// handler without taking a direct dependency on the device-provider project.
/// </summary>
public interface IIosPacketTunnelProvider
{
    /// <summary>
    /// Called by <see cref="IosVpnAdapter.AdapterOpen"/> after SetTunnelNetworkSettings succeeds
    /// (error == null) or fails (error != null). Safe to call multiple times — only the first wins.
    /// </summary>
    void CompleteStartTunnel(Foundation.NSError? error);
}
