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
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;

namespace VpnHood.Core.Client.VpnServices.Host;

/// <summary>
/// The default VpnHoodClient composition: resolves <see cref="VpnServiceOptions"/> into the services the
/// client needs (split-db filter gates, proxy connector, tracker) and constructs the client.
/// <see cref="Create"/> is the skeleton and each step is virtual, so a derived factory replaces a single
/// piece and inherits the rest. The host obtains the factory per connection via
/// <see cref="IVpnServiceHandler.CreateClientFactory"/> and keeps ownership of the created client.
/// </summary>
public class VpnHoodClientFactory
{
    public virtual async Task<VpnHoodClient> Create(VpnHoodClientParams clientParams,
        CancellationToken cancellationToken)
    {
        return new VpnHoodClient(
            vpnAdapter: clientParams.VpnAdapter,
            socketFactory: clientParams.SocketFactory,
            netFilter: new NetFilter {
                IpFilter = CreateIpFilter(clientParams),
                DomainFilter = CreateDomainFilter(clientParams),
                IpMapper = CreateIpMapper(clientParams)
            },
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
    protected virtual IIpMapper? CreateIpMapper(VpnHoodClientParams clientParams) => null;

    // Builds the ip gate chain the client filters with. Each split-ip db (country, via-app) is a lean
    // self-describing read-only SQLite gate chained over the inner filter, so the (former ~97MB) split
    // ranges never enter memory. A gate holding no rules yet is still chained: it may be filled later,
    // and it simply defers to the next filter until then.
    private IIpFilter? CreateIpFilter(VpnHoodClientParams clientParams)
    {
        var ipFilter = CreateInnerIpFilter(clientParams);
        foreach (var splitIpDbPath in clientParams.ServiceOptions.SplitIpDbPaths)
            ipFilter = new SqliteIpFilter(ipFilter, splitIpDbPath);
        return ipFilter;
    }

    private IDomainFilter? CreateDomainFilter(VpnHoodClientParams clientParams)
    {
        var domainFilter = CreateInnerDomainFilter(clientParams);
        foreach (var splitDomainDbPath in clientParams.ServiceOptions.SplitDomainDbPaths)
            domainFilter = new SqliteDomainFilter(domainFilter, splitDomainDbPath);
        return domainFilter;
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
                serverCheckTimeout: serviceOptions.ClientOptions.ServerQueryTimeout,
                cancellationToken: cancellationToken).Vhc(),
            _ => null
        };
    }

    protected virtual ITracker? CreateTracker(VpnHoodClientParams clientParams)
    {
        var clientOptions = clientParams.ServiceOptions.ClientOptions;
        var trackerFactory = TryCreateTrackerFactory(clientOptions.TrackerFactoryAssemblyQualifiedName);
        return trackerFactory?.TryCreateTracker(new TrackerCreateParams {
            ClientId = clientOptions.ClientId,
            ClientVersion = clientOptions.Version,
            Ga4MeasurementId = clientOptions.Ga4MeasurementId,
            UserAgent = clientOptions.UserAgent
        });
    }

    private static ITrackerFactory? TryCreateTrackerFactory(string? assemblyQualifiedName)
    {
        if (string.IsNullOrEmpty(assemblyQualifiedName))
            return null;

        try {
            var type = Type.GetType(assemblyQualifiedName);
            if (type == null)
                return null;

            var trackerFactory = Activator.CreateInstance(type) as ITrackerFactory;
            return trackerFactory;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create tracker factory. ClassName: {className}",
                assemblyQualifiedName);
            return null;
        }
    }
}
