using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Sqlite;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host;

/// <summary>
/// Represents the default composition of a <see cref="VpnHoodClient"/>: it turns <see cref="VpnServiceOptions"/> into
/// the services the client needs — the split filters, the proxy connector, the tracker — and makes the client.
/// </summary>
/// <remarks>
/// <see cref="Create"/> is the skeleton and each step is virtual, so a derived factory replaces a single piece and
/// inherits the rest. The host gets the factory for each connection from
/// <see cref="IVpnServiceHandler.CreateClientFactory"/>, composes the client's filter, and keeps ownership of the made
/// client. The filters update themselves — their stages read the filter folders' manifests again when a reconfigure
/// rolls down them — so nothing here is kept for live updates.
/// </remarks>
public class VpnHoodClientFactory
{
    /// <summary>Makes the client for a connection.</summary>
    /// <param name="clientParams">What the host prepared for the connection.</param>
    /// <param name="netFilter">The client's filter, which the host composed and the client owns once it is made.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the client.</returns>
    /// <exception cref="FormatException">The access key is not one.</exception>
    /// <exception cref="NotSupportedException">The access key's version is not 4.</exception>
    /// <exception cref="System.Text.Json.JsonException">The access key's JSON is not valid.</exception>
    /// <exception cref="InvalidDataException">The access key's JSON is <c>null</c>.</exception>
    /// <exception cref="OverflowException">A duration in the access key is out of range.</exception>
    /// <exception cref="InvalidOperationException">The access key has no farm secret.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The options' longest packet channel life, where finite, is below their shortest.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public virtual async Task<VpnHoodClient> Create(VpnHoodClientParams clientParams, NetFilter netFilter,
        CancellationToken cancellationToken)
    {
        // the client owns the connector only once made, so one that refuses its options or key leaves it to be closed
        // here: a managed one holds its store and runs its update
        var proxyConnector = await CreateProxyConnector(clientParams, cancellationToken).Vhc();
        try {
            return new VpnHoodClient(
                vpnAdapter: clientParams.VpnAdapter,
                socketFactory: clientParams.SocketFactory,
                netFilter: netFilter,
                proxyConnector: proxyConnector,
                tracker: CreateTracker(clientParams),
                // this device's, so the config folder: the data folder travels with a backup
                workingFolder: clientParams.ConfigFolder,
                options: clientParams.ServiceOptions.ClientOptions,
                knownHostAddresses: clientParams.KnownHostAddresses);
        }
        catch {
            await proxyConnector.TryDisposeAsync().Vhc();
            throw;
        }
    }

    /// <summary>
    /// Makes the filter the split-IP gates chain over, which is evaluated first and whose veto wins: a derived factory's
    /// own policy under the gates. By default there is none.
    /// </summary>
    /// <param name="clientParams">What the host prepared for the connection.</param>
    /// <returns>The filter, or <see langword="null"/> for none under the gates.</returns>
    protected virtual IIpFilter? CreateInnerIpFilter(VpnHoodClientParams clientParams) => null;

    /// <summary>
    /// Makes the domain filter under the split-domain gates. Unlike the IP one (<see cref="CreateInnerIpFilter"/>), it
    /// comes second: it hears only of a domain the gates leave to it, so a gate's include wins over its block. By
    /// default there is none.
    /// </summary>
    /// <param name="clientParams">What the host prepared for the connection.</param>
    /// <returns>The filter, or <see langword="null"/> for none under the gates.</returns>
    protected virtual IDomainFilter? CreateInnerDomainFilter(VpnHoodClientParams clientParams) => null;

    /// <summary>
    /// Makes the mapper of the client's destinations, a derived factory's, as tests map fake addresses to mock servers.
    /// By default there is none.
    /// </summary>
    /// <param name="clientParams">What the host prepared for the connection.</param>
    /// <returns>The mapper, or <see langword="null"/> for none.</returns>
    public virtual IIpMapper? CreateIpMapper(VpnHoodClientParams clientParams) => null;

    // Builds the ip filter pipe the client filters with. Each split-ip db (country, via-app) is a lean
    // self-describing read-only SQLite gate, so the (former ~97MB) split ranges never enter memory. The
    // gates live inside the self-updating SqliteIpFilterChain stage: it re-reads its filter folder's
    // manifest (the app rewrites it before signaling a reconfigure), so when the Reconfigure command
    // rolls down the pipe the stage finds the current signature-versioned dbs there and swaps its own
    // gates — no db path ever travels through vpn.config or the reconfigure request.
    // The inner filter is the chain's once the chain is made: till then a failure closes it here, a failure of its close
    // logged, the one that came first thrown
    internal async Task<IIpFilter> CreateIpFilter(VpnHoodClientParams clientParams,
        CancellationToken cancellationToken)
    {
        var innerIpFilter = CreateInnerIpFilter(clientParams);
        try {
            return await SqliteIpFilterChain.Create(innerIpFilter,
                Path.Combine(clientParams.ConfigFolder, SplitDbManifest.IpFiltersFolderName),
                autoDisposeNextFilter: true, cancellationToken).Vhc();
        }
        catch {
            if (innerIpFilter != null)
                VhInvoker.TryInvoke("close an inner IP filter", LogLevel.Error, innerIpFilter.Dispose);

            throw;
        }
    }

    internal async Task<IDomainFilter> CreateDomainFilter(VpnHoodClientParams clientParams,
        CancellationToken cancellationToken)
    {
        var innerDomainFilter = CreateInnerDomainFilter(clientParams);
        try {
            return await SqliteDomainFilterChain.Create(innerDomainFilter,
                Path.Combine(clientParams.ConfigFolder, SplitDbManifest.DomainFiltersFolderName),
                autoDisposeNextFilter: true, cancellationToken).Vhc();
        }
        catch {
            if (innerDomainFilter != null)
                VhInvoker.TryInvoke("close an inner domain filter", LogLevel.Error, innerDomainFilter.Dispose);

            throw;
        }
    }

    /// <summary>
    /// Makes the connector of the person's proxies: a simple one for a single proxy, without a store; a managed one over
    /// the SQLite store the app's process shares; or none, so connections go direct.
    /// </summary>
    /// <param name="clientParams">What the host prepared for the connection.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task whose result is the connector, or <see langword="null"/> for none.</returns>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    protected virtual async Task<IProxyConnector?> CreateProxyConnector(VpnHoodClientParams clientParams,
        CancellationToken cancellationToken)
    {
        var serviceOptions = clientParams.ServiceOptions;
        var proxyOptions = serviceOptions.ProxyOptions ?? new ProxyOptions();
        return proxyOptions.Mode switch {
            ProxyMode.Simple when proxyOptions.ProxyEndPoint != null =>
                new SimpleProxyConnector(proxyOptions.ProxyEndPoint),
            ProxyMode.Managed => await ManagedProxyConnector.Create(
                proxyOptions: proxyOptions,
                store: new ProxyEndPointStore(VpnServiceFolders.GetProxyStoreFilePath(clientParams.DataFolder)),
                // under the kill switch alone, as the server search asks the network's resolvers there alone: its block
                // lets only the VPN's own traffic out, so the list's first download goes over the VPN's sockets
                socketFactory: serviceOptions.ClientOptions.UseKillSwitch ? clientParams.SocketFactory : null,
                serverCheckTimeout: serviceOptions.ClientOptions.Transport.ServerQueryTimeout,
                cancellationToken: cancellationToken).Vhc(),
            _ => null
        };
    }

    /// <summary>
    /// Makes the app's trackers again in this process, from their factories' types
    /// (<see cref="VpnHood.Core.Client.Abstractions.ClientOptions.TrackerFactoryInfos"/>), switched on or off as
    /// <see cref="VpnHood.Core.Client.Abstractions.ClientOptions.AllowAnonymousTracker"/> says.
    /// </summary>
    /// <param name="clientParams">What the host prepared for the connection.</param>
    /// <returns>The tracker, or <see langword="null"/> where there are none.</returns>
    protected virtual ITracker? CreateTracker(VpnHoodClientParams clientParams)
    {
        var clientOptions = clientParams.ServiceOptions.ClientOptions;
        var trackerFactories = clientOptions.TrackerFactoryInfos
            .Select(TrackerFactorySerializer.TryDeserialize)
            .OfType<ITrackerFactory>()
            .ToArray();

        return trackerFactories.TryCreateTracker(new TrackerCreateParams {
            ClientId = clientOptions.ClientId,
            ClientVersion = clientOptions.TrackerClientVersion,
            UserAgent = clientOptions.TrackerUserAgent,
            IsEnabled = clientOptions.AllowAnonymousTracker
        });
    }
}
