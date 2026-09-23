using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

public interface IVpnServiceHandler
{
    IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData);

    /// <summary>
    /// Names the factory that builds the VpnHoodClient for each connection. Return
    /// <see cref="VpnHoodClientFactory"/> for the default composition, or a derived factory to replace a
    /// single piece (filters, proxy connector, tracker). The host calls this per connection and keeps
    /// ownership of the created client (state wiring and dispose) — the factory must not hold a reference
    /// to it.
    /// </summary>
    VpnHoodClientFactory CreateClientFactory();

    void ShowNotification(ConnectionInfo connectionInfo);
    void StopNotification();
    void StopSelf();
}