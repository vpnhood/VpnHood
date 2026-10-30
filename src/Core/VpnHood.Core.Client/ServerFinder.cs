using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using System.Collections.Concurrent;
using System.Net;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.Management.Abstractions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Monitoring;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

public class ServerFinder(
    ISocketFactory socketFactory,
    byte[] farmSecret,
    string? serverLocation,
    TimeSpan serverQueryTimeout,
    EndPointStrategy endPointStrategy,
    IReadOnlyList<IPEndPoint> customServerEndpoints,
    ITracker? tracker,
    IProxyConnector? proxyConnector,
    bool includeIpV6,
    VpnEndPoint? connectedServer,
    int maxDegreeOfParallelism = 10)
{
    private ProgressMonitor? _progressMonitor;
    private HostStatus[] _hostEndPointStatuses = [];
    // Kept beside discovery, never in the public endpoint record or its saved/logged representation.
    // Two tokens may name the same endpoint during a key rotation: keep the selected token's key.
    private readonly ConcurrentDictionary<VpnEndPoint, byte[]> _serverSecrets = new(ReferenceEqualityComparer.Instance);
    public bool IncludeIpV6 { get; set; } = includeIpV6;
    public string? ServerLocation => serverLocation;
    public IReadOnlyList<IPEndPoint> CustomServerEndpoints => customServerEndpoints;
    public ProgressStatus? Progress => _progressMonitor?.Progress;

    internal byte[] GetServerSecret(VpnEndPoint vpnEndPoint) => _serverSecrets[vpnEndPoint];

    private VpnEndPoint CreateVpnEndPoint(IPEndPoint endPoint, ServerToken serverToken)
    {
        var vpnEndPoint = new VpnEndPoint(endPoint, serverToken.HostName, serverToken.CertificateHash, serverToken.PathBase);
        // Redirects stay in the access key's farm, even when they name another certificate's host.
        _serverSecrets[vpnEndPoint] = serverToken.Secret ?? farmSecret;
        return vpnEndPoint;
    }

    private class HostStatus
    {
        public required VpnEndPoint VpnEndPoint { get; init; }
        public bool? Available { get; set; }
    }

    public async Task<IReadOnlyList<VpnEndPoint>> ResolveVpnEndPoints(IReadOnlyList<ServerToken> serverTokens, bool includeIpV6,
        CancellationToken cancellationToken)
    {
        // log warning if there are some forced endpoints
        if (customServerEndpoints.Any())
            VhLogger.Instance.LogWarning("There are forced endpoints in the configuration. EndPoints: {EndPoints}",
                string.Join(", ", customServerEndpoints.Select(VhLogger.Format)));

        // resolve endpoints for each server token in parallel
        var itemExceptions = new ConcurrentBag<(Exception exception, ServerToken serverToken)>();
        var tasks = serverTokens.Select(async serverToken => {
            try {
                var endpoints = customServerEndpoints.Any()
                    ? customServerEndpoints
                    : await EndPointResolver.ResolveHostEndPoints(
                        serverToken, endPointStrategy, cancellationToken).Vhc();

                return endpoints.Select(ep => CreateVpnEndPoint(ep, serverToken)).ToArray();
            }
            catch (OperationCanceledException) {
                throw;
            }
            catch (Exception ex) {
                itemExceptions.Add((ex, serverToken));
                return [];
            }
        });

        // wait for all tasks to complete
        var resolved = await Task.WhenAll(tasks).Vhc();

        // flatten the results into a single enumerable
        var results = resolved.SelectMany(x => x)
            .Where(x =>
                includeIpV6 || // accept any IPv6 if allowed
                x.TcpEndPoint.IsV4() ||
                x.TcpEndPoint.Address.IsLoopback()) // loopback addresses are for tests
            .ToArray();

        // throw the first error if there is no resolved endpoints
        if (!results.Any() && itemExceptions.Any())
            throw itemExceptions.First().exception;

        // log all exceptions
        foreach (var itemException in itemExceptions) {
            VhLogger.Instance.LogWarning(itemException.exception,
                "Failed to resolve endpoints for server token. HostName: {HostName}, HostPort: {HostPort}",
                VhLogger.FormatHostName(itemException.serverToken.HostName), itemException.serverToken.HostPort);
        }

        return results;
    }

    // There is much work to be done here
    public async Task<VpnEndPoint> FindReachableServerAsync(IReadOnlyList<ServerToken> serverTokens,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "Finding a reachable server... " +
            "QueryTimeout: {QueryTimeout}, ServerLocation: {ServerLocation}",
            serverQueryTimeout, ServerLocation);

        // before any lookup: after a VPN host's crash, the dead connection's routes let only this server through
        if (connectedServer != null) {
            _serverSecrets[connectedServer] = farmSecret;
            var connectedServerStatus = new HostStatus { VpnEndPoint = connectedServer };
            await VerifyHostStatus(connectedServerStatus, serverQueryTimeout, cancellationToken).Vhc();
            if (connectedServerStatus.Available == true)
                return connectedServer;
        }

        // create server finder items from ServerToken.HostEndPoints using SelectMany
        var vpnEndPoints = await ResolveVpnEndPoints(serverTokens, IncludeIpV6, cancellationToken).Vhc();

        // create array of host statuses and shuffle them
        // We should not let client always try the same server first to avoid overloading specific servers
        var hostStatuses = vpnEndPoints
            .Select(x => new HostStatus { VpnEndPoint = x })
            .Shuffle()
            .ToArray();

        // find the best server
        _hostEndPointStatuses =
            await VerifyHostsStatus(hostStatuses, byOrder: false, cancellationToken: cancellationToken).Vhc();
        var res = _hostEndPointStatuses.FirstOrDefault(x => x.Available == true)?.VpnEndPoint;

        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "ServerFinder result. Reachable: {Reachable}, Unreachable: {Unreachable}, Unknown: {Unknown}",
            _hostEndPointStatuses.Count(x => x.Available == true),
            _hostEndPointStatuses.Count(x => x.Available == false),
            _hostEndPointStatuses.Count(x => x.Available == null));

        _ = TryTrackEndPointsAvailability([], _hostEndPointStatuses, CancellationToken.None).Vhc();
        if (res != null)
            return res;

        _ = tracker?.TryTrack(ClientTrackerBuilder.BuildConnectionFailed(serverLocation: ServerLocation,
            isIpV6Supported: IncludeIpV6, hasRedirected: false), CancellationToken.None);

        // throw specific exception for proxy server issues
        if (proxyConnector is { IsEnabled: true, Status.IsAnySucceeded: false })
            throw new UnreachableProxyServerException();

        throw new UnreachableServerException();
    }

    public async Task<VpnEndPoint> FindBestRedirectedServerAsync(IReadOnlyList<ServerToken> serverTokens,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "Finding best server from redirected endpoints... ServerLocation: {ServerLocation}", ServerLocation);

        if (!serverTokens.Any())
            throw new Exception("There is no server endpoint. Please check server configuration.");

        var vpnEndPoints = await ResolveVpnEndPoints(serverTokens, IncludeIpV6, cancellationToken).Vhc();

        // create host statuses and merge the previous availability statuses
        var hostStatuses = vpnEndPoints.Select(x => new HostStatus { VpnEndPoint = x }).ToArray();
        foreach (var hostStatus in hostStatuses)
            hostStatus.Available = _hostEndPointStatuses
                .FirstOrDefault(x => x.VpnEndPoint.Equals(hostStatus.VpnEndPoint))?.Available;

        // find the best server by order
        var endpointStatuses =
            await VerifyHostsStatus(hostStatuses, byOrder: true, cancellationToken: cancellationToken).Vhc();
        var res = endpointStatuses.FirstOrDefault(x => x.Available == true)?.VpnEndPoint;

        VhLogger.Instance.LogInformation(GeneralEventId.Session,
            "ServerFinder result. Reachable:{Reachable}, Unreachable:{Unreachable}, Unknown: {Unknown}, Best: {Best}",
            endpointStatuses.Count(x => x.Available == true), endpointStatuses.Count(x => x.Available == false),
            endpointStatuses.Count(x => x.Available == null),
            VhLogger.Format(res?.TcpEndPoint));

        // track new endpoints availability 
        _ = TryTrackEndPointsAvailability(_hostEndPointStatuses, endpointStatuses, CancellationToken.None).Vhc();
        if (res != null)
            return res;

        _ = tracker?.TryTrack(ClientTrackerBuilder.BuildConnectionFailed(serverLocation: ServerLocation,
            isIpV6Supported: IncludeIpV6, hasRedirected: true), CancellationToken.None);

        // throw specific exception for proxy server issues
        if (proxyConnector is { IsEnabled: true, Status.IsAnySucceeded: false })
            throw new UnreachableProxyServerException();

        // throw unreachable server location exception
        throw UnreachableServerLocationException.Create(ServerLocation);
    }

    private Task TryTrackEndPointsAvailability(HostStatus[] oldStatuses, HostStatus[] newStatuses,
        CancellationToken cancellationToken)
    {
        // find new discovered statuses
        var changesStatus = newStatuses
            .Where(x =>
                x.Available != null &&
                !oldStatuses.Any(y => y.Available == x.Available && y.VpnEndPoint.Equals(x.VpnEndPoint)))
            .ToArray();

        var trackEvents = changesStatus
            .Where(x => x.Available != null)
            .Select(x => ClientTrackerBuilder.BuildEndPointStatus(x.VpnEndPoint, x.Available!.Value))
            .ToArray();

        // report endpoints
        var endPointReport = string.Join(", ",
            changesStatus.Select(x => $"{VhLogger.Format(x.VpnEndPoint.TcpEndPoint)} => {x.Available}"));
        VhLogger.Instance.LogInformation(GeneralEventId.Request, "HostEndPoints: {EndPoints}", endPointReport);

        return tracker?.TryTrack(trackEvents, cancellationToken) ?? Task.CompletedTask;
    }

    private async Task<HostStatus[]> VerifyHostsStatus(HostStatus[] hostStatuses, bool byOrder,
        CancellationToken cancellationToken)
    {
        // handle empty list
        var firstHostStatus = hostStatuses.FirstOrDefault();
        if (firstHostStatus is null)
            return [];

        // Initialize time-based progress tracking
        // as we check the first server separately in unparallel mode, we add maxDegreeOfParallelism to total
        _progressMonitor = new ProgressMonitor(hostStatuses.Length + maxDegreeOfParallelism,
            serverQueryTimeout,
            maxDegreeOfParallelism);

        try {
            // first attempt the first server, take it simple. If it fails, do parallel checks.
            // this help prevent false DDOS attack on servers as most client can simply connect to the first server
            VhLogger.Instance.LogInformation(GeneralEventId.Request,
                "Starting endpoint check for the first endpoint... EndPoint: {EndPoint}",
                VhLogger.Format(firstHostStatus.VpnEndPoint.TcpEndPoint));

            await VerifyHostStatus(firstHostStatus, serverQueryTimeout, cancellationToken).Vhc();
            if (firstHostStatus.Available == true)
                return [firstHostStatus];

            // mark first host as completed in progress
            // We added maxDegreeOfParallelism to total for first unparallel check, so we need to increment completed count here
            for (var i = 0; i < maxDegreeOfParallelism; i++)
                _progressMonitor.IncrementCompleted();

            // proceed with parallel verification
            return await VerifyHostStatusParallel(hostStatuses, byOrder, _progressMonitor, cancellationToken).Vhc();
        }
        finally {
            VhLogger.Instance.LogInformation(GeneralEventId.Request,
                "Endpoint reachability check completed. ElapsedTime: {ElapsedTime}, CompletedEndpoints: {CompletedEndpoints}/{TotalEndpoints}",
                FastDateTime.UtcNow - _progressMonitor.Progress.StartedTime,
                hostStatuses.Count(x => x.Available is not null), hostStatuses.Length);

            // Ensure progress is complete at the end of the operation
            _progressMonitor = null;
        }
    }


    private async Task<HostStatus[]> VerifyHostStatusParallel(HostStatus[] hostStatuses, bool byOrder,
        ProgressMonitor progressMonitor, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "Starting endpoints reachability. EndpointCount: {EndpointCount}, MaxParallelism: {MaxParallelism}",
            hostStatuses.Length, maxDegreeOfParallelism);

        using var searchingCts = new CancellationTokenSource(); // this will be canceled when a server is found
        using var parallelCts = CancellationTokenSource.CreateLinkedTokenSource(searchingCts.Token, cancellationToken);
        var oldUseRecentSucceeded = proxyConnector?.UseRecentSucceeded;
        try {
            // Optimize proxy selection for server verification
            // We should not try proxies that may fail as it will slow down the process
            proxyConnector?.UseRecentSucceeded = true;

            // Use SemaphoreSlim to control max degree of parallelism
            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);

            // Create all verification tasks
            var verificationTasks = hostStatuses.Select(async hostStatus => {
                await semaphore.WaitAsync(parallelCts.Token).Vhc();
                try {
                    await VerifyHostStatus(hostStatus, serverQueryTimeout, parallelCts.Token).Vhc();

                    // Cancel search if we found a reachable server and not searching by order
                    if (hostStatus.Available == true && !byOrder)
                        await searchingCts.CancelAsync().Vhc();

                    // For ordered search, cancel if we found the first reachable server in order
                    if (byOrder) {
                        // Check if we should stop based on order
                        foreach (var item in hostStatuses) {
                            if (item.Available == null)
                                break; // wait to get the result in order

                            if (item.Available.Value) {
                                await searchingCts.CancelAsync().Vhc();
                                break;
                            }
                        }
                    }

                    return hostStatus;
                }
                catch (OperationCanceledException) when (searchingCts.IsCancellationRequested) {
                    // Server was found, stop processing this endpoint
                    return hostStatus;
                }
                finally {
                    progressMonitor.IncrementCompleted();
                    semaphore.Release();
                }
            }).ToArray();

            // Wait for all tasks to complete or until a server is found
            try {
                await Task.WhenAll(verificationTasks).Vhc();
            }
            catch (OperationCanceledException) when (searchingCts.IsCancellationRequested) {
                // A server has been found, this is expected
            }
        }
        catch (OperationCanceledException) when (searchingCts.IsCancellationRequested) {
            // A server has been found, this is expected
        }
        finally {
            if (oldUseRecentSucceeded != null)
                proxyConnector?.UseRecentSucceeded = oldUseRecentSucceeded.Value;
        }

        return hostStatuses;
    }

    private async Task VerifyHostStatus(HostStatus hostStatus, TimeSpan queryTimeout,
        CancellationToken cancellationToken)
    {
        // if first server is already marked as available, return it directly
        if (hostStatus.Available is not null)
            return;

        var requestSender = CreateRequestSender(hostStatus.VpnEndPoint);
        try {
            hostStatus.Available = await VerifyServerStatus(requestSender, queryTimeout, cancellationToken).Vhc();
        }
        finally {
            // not waited for: its connections' closes may wait for their peers, and the search would wait with them
            _ = requestSender.TryDisposeAsync();
        }
    }

    private static async Task<bool> VerifyServerStatus(RequestSender requestSender, TimeSpan queryTimeout,
        CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogInformation(GeneralEventId.Request,
                "Check an endpoint reachability. EndPoint: {EndPoint}",
                VhLogger.Format(requestSender.ConnectorService.VpnEndPoint.TcpEndPoint));

            using var queryTimeoutCts = new CancellationTokenSource(queryTimeout); // timeout for each server query
            using var requestCts =
                CancellationTokenSource.CreateLinkedTokenSource(queryTimeoutCts.Token, cancellationToken);

            var requestResult = await requestSender
                .SendRequest<SessionResponse>(new ServerCheckRequest { RequestId = UniqueIdFactory.Create() },
                    requestCts.Token)
                .Vhc();

            // closed in the background, as its request sender is
            var errorCode = requestResult.Response.ErrorCode;
            _ = requestResult.TryDisposeAsync();

            // this should be already handled by the connector and never happen
            return errorCode is SessionErrorCode.Ok
                ? true
                : throw new SessionException(errorCode);
        }
        catch (UnauthorizedAccessException) {
            // ServerCheck returns 401 even for a valid proof; it establishes reachability only.
            // If a saved server refuses the hello, the client's close clears it for the next connect.
            return true;
        }
        catch (OperationCanceledException ex) {
            if (cancellationToken.IsCancellationRequested)
                throw;

            VhLogger.Instance.LogInformation(ex, "Endpoint reachability check timed out. EndPoint: {EndPoint}",
                VhLogger.Format(requestSender.ConnectorService.VpnEndPoint.TcpEndPoint));
            return false;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "Could not get server status. EndPoint: {EndPoint}",
                VhLogger.Format(requestSender.ConnectorService.VpnEndPoint.TcpEndPoint));

            return false;
        }
    }

    private RequestSender CreateRequestSender(VpnEndPoint vpnEndPoint)
    {
        var connectorService = new ConnectorServiceOptions {
            VpnEndPoint = vpnEndPoint,
            ProxyConnector = proxyConnector,
            SocketFactory = socketFactory,
            RequestTimeout = serverQueryTimeout,
            AllowChannelReuse = false
        };

        var requestSigner = new ClientRequestSigner(GetServerSecret(vpnEndPoint), vpnEndPoint.HostName);
        var requestSender = new RequestSender(new ConnectorService(connectorService, requestSigner), requestSigner);

        requestSender.ConnectorService.Init(
            channelIdleTimeout: TimeSpan.Zero,
            useWebSocket: false,
            requestTimeout: serverQueryTimeout,
            useQuic: false,
            quicEndPoint: null);

        return requestSender;
    }
}
