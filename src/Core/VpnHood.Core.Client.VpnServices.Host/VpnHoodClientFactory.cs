using Ga4.Trackers;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Core.Filtering.Sqlite;
using VpnHood.Core.Proxies.Management;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Core.Proxies.Management.Sqlite;
using VpnHood.Net.Toolkit.Extensions;

namespace VpnHood.Core.Client.VpnServices.Host;

/// <summary>
/// The default VpnHoodClient composition: resolves <see cref="VpnServiceOptions"/> into the services the
/// client needs (split-db filter gates, proxy connector, tracker) and constructs the client.
/// <see cref="Create"/> is the skeleton and each step is virtual, so a derived factory replaces a single
/// piece and inherits the rest. The host obtains the factory per connection via
/// <see cref="IVpnServiceHandler.CreateClientFactory"/>, composes the NetFilter and keeps ownership of
/// the created client. The filter pipes are self-updating (their stages re-read the filter folder
/// manifests when Reconfigure rolls down them), so nothing here is retained for live updates.
/// </summary>
public class VpnHoodClientFactory
{
    public virtual async Task<VpnHoodClient> Create(VpnHoodClientParams clientParams, NetFilter netFilter,
        CancellationToken cancellationToken)
    {
        return new VpnHoodClient(
            vpnAdapter: clientParams.VpnAdapter,
            socketFactory: clientParams.SocketFactory,
            netFilter: netFilter,
            proxyConnector: await CreateProxyConnector(clientParams, cancellationToken).Vhc(),
            tracker: CreateTracker(clientParams),
            options: clientParams.ServiceOptions.ClientOptions);
    }

    // The filter the split-ip gates chain over: it is evaluated first and its veto wins. Null means no
    // filter under the gates; a derived factory overrides this to put its own policy under them.
    protected virtual IIpFilter? CreateInnerIpFilter(VpnHoodClientParams clientParams) => null;

    // the domain twin of CreateInnerIpFilter
    protected virtual IDomainFilter? CreateInnerDomainFilter(VpnHoodClientParams clientParams) => null;

    // no default mapper; an injection point for derived factories (e.g. tests map fake ips to mock servers)
    public virtual IIpMapper? CreateIpMapper(VpnHoodClientParams clientParams) => null;

    // Builds the ip filter pipe the client filters with. Each split-ip db (country, via-app) is a lean
    // self-describing read-only SQLite gate, so the (former ~97MB) split ranges never enter memory. The
    // gates live inside the self-updating SqliteIpFilterChain stage: it re-reads its filter folder's
    // manifest (the app rewrites it before signaling a reconfigure), so when the Reconfigure command
    // rolls down the pipe the stage finds the current signature-versioned dbs there and swaps its own
    // gates — no db path ever travels through vpn.config or the reconfigure request.
    public IIpFilter CreateIpFilter(VpnHoodClientParams clientParams)
    {
        return new SqliteIpFilterChain(CreateInnerIpFilter(clientParams),
            Path.Combine(clientParams.ConfigFolder, SplitDbManifest.IpFiltersFolderName));
    }

    public IDomainFilter CreateDomainFilter(VpnHoodClientParams clientParams)
    {
        return new SqliteDomainFilterChain(CreateInnerDomainFilter(clientParams),
            Path.Combine(clientParams.ConfigFolder, SplitDbManifest.DomainFiltersFolderName));
    }

    // External proxies. Simple mode is the lightweight path (no store); Managed mode uses the shared SQLite
    // endpoint store that the app process also reads/writes. Null means no proxy: connections go direct.
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
                store: new ProxyEndPointStore(Path.Combine(clientParams.ConfigFolder, "proxies", "proxies.db")),
                serverCheckTimeout: serviceOptions.ClientOptions.Transport.ServerQueryTimeout,
                cancellationToken: cancellationToken).Vhc(),
            _ => null
        };
    }

    // The app's trackers, made again here from their factories' types: null when there are none.
    protected virtual ITracker? CreateTracker(VpnHoodClientParams clientParams)
    {
        var clientOptions = clientParams.ServiceOptions.ClientOptions;
        var trackerFactories = clientOptions.TrackerFactoryInfos
            .Select(TrackerFactorySerializer.TryDeserialize)
            .OfType<ITrackerFactory>()
            .ToArray();

        return trackerFactories.TryCreateTracker(new TrackerCreateParams {
            ClientId = clientOptions.ClientId,
            ClientVersion = clientOptions.Version,
            UserAgent = clientOptions.UserAgent,
            IsEnabled = clientOptions.AllowAnonymousTracker
        });
    }
}
