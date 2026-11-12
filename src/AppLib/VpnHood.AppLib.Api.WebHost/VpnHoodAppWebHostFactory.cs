using VpnHood.AppLib.App.WebHosting;

namespace VpnHood.AppLib.Api.WebHost;

/// <summary>
/// Provides the app's web hosts, which serve the app's API over HTTP, and the UI's files where they are supplied: the
/// local one, on the loopback, for the app itself, and the remote one, on the local network, for another device, which
/// pairs where a token is required.
/// </summary>
/// <remarks>
/// A head names this factory in its app options; the app decides when each host is first needed and tells it what to
/// serve in <see cref="WebHostCreateParams"/>. Nothing is configured here. A host binds nothing until it is first
/// started.
/// </remarks>
public class VpnHoodAppWebHostFactory : IAppWebHostFactory
{
    /// <summary>Creates the local web host, which binds the loopback for the app itself.</summary>
    /// <param name="createParams">What the host serves, and the settings it serves it with.</param>
    /// <returns>The host, not started yet.</returns>
    public IAppWebHost CreateLocal(WebHostCreateParams createParams)
    {
        return new VpnHoodAppWebHost(createParams, isRemote: false);
    }

    /// <summary>
    /// Creates the remote web host, which tries to bind the device's IPv4 local network addresses, skipping any that
    /// fail, for another device on the network.
    /// </summary>
    /// <param name="createParams">What the host serves, and the settings it serves it with.</param>
    /// <returns>The host, not started yet.</returns>
    public IAppWebHost CreateRemote(WebHostCreateParams createParams)
    {
        return new VpnHoodAppWebHost(createParams, isRemote: true);
    }
}
