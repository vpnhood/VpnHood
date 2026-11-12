using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

/// <summary>
/// Defines the platform's part of the VPN service the host runs: its adapter, its client factory, its notification,
/// and its own end.
/// </summary>
public interface IVpnServiceHandler
{
    /// <summary>Makes the VPN adapter for a connection.</summary>
    /// <param name="adapterSettings">The adapter's settings.</param>
    /// <param name="debugData">
    /// The developer's debug switches (<see cref="VpnHood.Core.Client.Abstractions.ClientOptions.DebugData1"/>), or <see langword="null"/>.
    /// </param>
    /// <returns>
    /// The adapter, which the client disposes with itself where
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientOptions.AutoDisposeVpnAdapter"/> says so, the default.
    /// </returns>
    /// <exception cref="InvalidOperationException">Windows has given the VPN plug-in no channel yet.</exception>
    IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData);

    /// <summary>
    /// Names the factory that builds the VpnHoodClient for each connection. Return
    /// <see cref="VpnHoodClientFactory"/> for the default composition, or a derived factory to replace a
    /// single piece (filters, proxy connector, tracker). The host calls this per connection and keeps
    /// ownership of the created client (state wiring and dispose) — the factory must not hold a reference
    /// to it.
    /// </summary>
    /// <returns>The factory.</returns>
    VpnHoodClientFactory CreateClientFactory();

    /// <summary>
    /// Gets a value indicating whether the service may connect again by itself, with no start from the app or the
    /// system: <see langword="false"/> where only the system's start may bring an adapter up.
    /// </summary>
    bool CanReconnect { get; }

    /// <summary>Shows or updates the service's notification, where the platform has one.</summary>
    /// <param name="connectionInfo">The connection's info.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    void ShowNotification(ConnectionInfo connectionInfo, CancellationToken cancellationToken);

    /// <summary>Removes the service's notification, where the platform has one.</summary>
    void StopNotification();

    /// <summary>Ends the service itself, once its connection has ended with no other to follow.</summary>
    void StopSelf();
}