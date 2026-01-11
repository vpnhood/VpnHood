using System.Net;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Proxies.EndPointManagement;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Monitoring;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

public class ServerFinder(
    ISocketFactory socketFactory,
    string? serverLocation,
    TimeSpan serverQueryTimeout,
    EndPointStrategy endPointStrategy,
    IPEndPoint[] customServerEndpoints,
    ITracker? tracker,
    ProxyEndPointManager proxyEndPointManager,
    int maxDegreeOfParallelism = 10)
{
    private ProgressMonitor? _progressMonitor;
    private HostStatus[] _hostEndPointStatuses = [];
    public bool IncludeIpV6 { get; set; } = true;
    public string? ServerLocation => serverLocation;
    public IPEndPoint[] CustomServerEndpoints => customServerEndpoints;
    public ProgressStatus? Progress => _progressMonitor?.Progress;

    private class HostStatus
    {
        public required VpnEndPoint VpnEndPoint { get; init; }
        public bool? Available { get; set; }
    }

    public async Task<VpnEndPoint[]> ResolveVpnEndPoints(IEnumerable<ServerToken> serverTokens, bool includeIpV6,
        CancellationToken cancellationToken)
    {
        // log warning if there are some forced endpoints
        if (customServerEndpoints.Any()) {
            VhLogger.Instance.LogWarning("There are forced endpoints in the configuration. EndPoints: {EndPoints}",
                string.Join(", ", customServerEndpoints.Select(VhLogger.Format)));

            // select forced endpoints for each server token
            return serverTokens
                .SelectMany(serverToken => customServerEndpoints.Select(ep =>
                    new VpnEndPoint(ep, serverToken.HostName, serverToken.CertificateHash)))
                .Where(x => includeIpV6 || x.TcpEndPoint.IsV6() || x.TcpEndPoint.Address.IsLoopback())
                .ToArray();
        }

        // resolve endpoints for each server token in parallel
        var itemExceptions = new List<(Exception exception, ServerToken serverToken)>();
        var tasks = serverTokens.Select(async serverToken => {
            try {
                var endpoints = await EndPointResolver.ResolveHostEndPoints(
                    serverToken, endPointStrategy, cancellationToken);

                return endpoints.Select(ep => new VpnEndPoint(ep, serverToken.HostName, serverToken.CertificateHash));
            }
            catch (Exception ex) {
                itemExceptions.Add((ex, serverToken));
                return [];
            }
        });

        // wait for all tasks to complete
        var resolved = await Task.WhenAll(tasks);

        // flatten the results into a single enumerable
        var results = resolved.SelectMany(x => x)
            .Where(x => 
                x.TcpEndPoint.IsV4() || 
                (x.TcpEndPoint.IsV6() && includeIpV6) || 
                x.TcpEndPoint.Address.IsLoopback()) // loopback addresses are for tests
            .ToArray();

        // throw the first error if there is no resolved endpoints
        if (!results.Any() && itemExceptions.Any())
            throw itemExceptions.First().exception;

        // log all exceptions
        foreach (var itemException in itemExceptions) {
            VhLogger.Instance.LogWarning(itemException.exception,
                "Failed to resolve endpoints for server token. HostName: {HostName}, HostPort: {HostPort}",
                itemException.serverToken.HostName, itemException.serverToken.HostPort);
        }

        return results;
    }

    // There is much work to be done here
    public async Task<VpnEndPoint> FindReachableServerAsync(IEnumerable<ServerToken> serverTokens,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "Finding a reachable server... QueryTimeout: {QueryTimeout}",
            serverQueryTimeout);

        // create server finder items from ServerToken.HostEndPoints using SelectMany
        var vpnEndPoints = await ResolveVpnEndPoints(serverTokens, IncludeIpV6, cancellationToken);

        // create array of host statuses and shuffle them
        // We should not let client always try the same server first to avoid overloading specific servers
        var hostStatuses = vpnEndPoints
            .Select(x => new HostStatus { VpnEndPoint = x })
            .Shuffle()
            .ToArray();

        // find the best server
        _hostEndPointStatuses =
            await VerifyHostsStatus(hostStatuses, byOrder: false, cancellationToken: cancellationToken);
        var res = _hostEndPointStatuses.FirstOrDefault(x => x.Available == true)?.VpnEndPoint;

        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "ServerFinder result. Reachable: {Reachable}, Unreachable: {Unreachable}, Unknown: {Unknown}",
            _hostEndPointStatuses.Count(x => x.Available == true),
            _hostEndPointStatuses.Count(x => x.Available == false),
            _hostEndPointStatuses.Count(x => x.Available == null));

        _ = TryTrackEndPointsAvailability([], _hostEndPointStatuses).Vhc();
        if (res != null)
            return res;

        _ = tracker?.TryTrack(ClientTrackerBuilder.BuildConnectionFailed(serverLocation: ServerLocation,
            isIpV6Supported: IncludeIpV6, hasRedirected: false));

        // throw specific exception for proxy server issues
        if (proxyEndPointManager is { IsEnabled: true, Status.IsAnySucceeded: false })
            throw new UnreachableProxyServerException();

        throw new UnreachableServerException();
    }

    public async Task<VpnEndPoint> FindBestRedirectedServerAsync(ServerToken[] serverTokens,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Request, "Finding best server from redirected endpoints...");

        if (!serverTokens.Any())
            throw new Exception("There is no server endpoint. Please check server configuration.");

        var vpnEndPoints = await ResolveVpnEndPoints(serverTokens, IncludeIpV6, cancellationToken);

        // create host statuses and merge the previous availability statuses
        var hostStatuses = vpnEndPoints.Select(x => new HostStatus { VpnEndPoint = x }).ToArray();
        foreach (var hostStatus in hostStatuses)
            hostStatus.Available = _hostEndPointStatuses
                .FirstOrDefault(x => x.VpnEndPoint.Equals(hostStatus.VpnEndPoint))?.Available;

        // find the best server by order
        var endpointStatuses =
            await VerifyHostsStatus(hostStatuses, byOrder: true, cancellationToken: cancellationToken);
        var res = endpointStatuses.FirstOrDefault(x => x.Available == true)?.VpnEndPoint;

        VhLogger.Instance.LogInformation(GeneralEventId.Session,
            "ServerFinder result. Reachable:{Reachable}, Unreachable:{Unreachable}, Unknown: {Unknown}, Best: {Best}",
            endpointStatuses.Count(x => x.Available == true), endpointStatuses.Count(x => x.Available == false),
            endpointStatuses.Count(x => x.Available == null),
            VhLogger.Format(res?.TcpEndPoint));

        // track new endpoints availability 
        _ = TryTrackEndPointsAvailability(_hostEndPointStatuses, endpointStatuses).Vhc();
        if (res != null)
            return res;

        _ = tracker?.TryTrack(ClientTrackerBuilder.BuildConnectionFailed(serverLocation: ServerLocation,
            isIpV6Supported: IncludeIpV6, hasRedirected: true));

        // throw specific exception for proxy server issues
        if (proxyEndPointManager is { IsEnabled: true, Status.IsAnySucceeded: false })
            throw new UnreachableProxyServerException();

        throw new UnreachableServerLocationException(BuildExceptionMessage(ServerLocation));
    }

    private static string BuildExceptionMessage(string? serverLocation)
    {
        var location = serverLocation is null || !ServerLocationInfo.IsAutoLocation(serverLocation)
            ? "Auto"
            : serverLocation;

        return $"There is no reachable server at this moment. Please try again later. Location: {location}";
    }


    private Task TryTrackEndPointsAvailability(HostStatus[] oldStatuses, HostStatus[] newStatuses)
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

        return tracker?.TryTrack(trackEvents) ?? Task.CompletedTask;
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
        _progressMonitor = new ProgressMonitor(hostStatuses.Length + maxDegreeOfParallelism, serverQueryTimeout,
            maxDegreeOfParallelism);

        try {
            // first attempt the first server, take it simple. If it fails, do parallel checks.
            // this help prevent false DDOS attack on servers as most client can simply connect to the first server
            await VerifyHostStatus(firstHostStatus, serverQueryTimeout, cancellationToken);
            if (firstHostStatus.Available == true)
                return [firstHostStatus];

            // mark first host as completed in progress
            // We added maxDegreeOfParallelism to total for first unparallel check, so we need to increment completed count here
            for (var i = 0; i < maxDegreeOfParallelism; i++)
                _progressMonitor.IncrementCompleted();

            // proceed with parallel verification
            return await VerifyHostStatusParallel(hostStatuses, byOrder, _progressMonitor, cancellationToken);
        }
        finally {
            VhLogger.Instance.LogInformation(GeneralEventId.Request,
                "Server verification completed. ElapsedTime: {ElapsedTime}, CompletedEndpoints: {CompletedEndpoints}/{TotalEndpoints}",
                FastDateTime.Now - _progressMonitor.Progress.StartedTime,
                hostStatuses.Count(x => x.Available is not null), hostStatuses.Length);

            // Ensure progress is complete at the end of the operation
            _progressMonitor = null;
        }
    }


    private async Task<HostStatus[]> VerifyHostStatusParallel(HostStatus[] hostStatuses, bool byOrder,
        ProgressMonitor progressMonitor, CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Request,
            "Starting server verification. Endpoints: {EndpointCount}, MaxParallelism: {MaxParallelism}",
            hostStatuses.Length, maxDegreeOfParallelism);

        using var searchingCts = new CancellationTokenSource(); // this will be canceled when a server is found
        using var parallelCts = CancellationTokenSource.CreateLinkedTokenSource(searchingCts.Token, cancellationToken);
        var oldUseRecentSucceeded = proxyEndPointManager.UseRecentSucceeded;
        try {
            // Optimize proxy selection for server verification
            // We should not try proxies that may fail as it will slow down the process
            proxyEndPointManager.UseRecentSucceeded = true;

            // Use SemaphoreSlim to control max degree of parallelism
            using var semaphore = new SemaphoreSlim(maxDegreeOfParallelism, maxDegreeOfParallelism);

            // Create all verification tasks
            var verificationTasks = hostStatuses.Select(async hostStatus => {
                await semaphore.WaitAsync(parallelCts.Token);
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
                await Task.WhenAll(verificationTasks);
            }
            catch (OperationCanceledException) when (searchingCts.IsCancellationRequested) {
                // A server has been found, this is expected
            }
        }
        catch (OperationCanceledException) when (searchingCts.IsCancellationRequested) {
            // A server has been found, this is expected
        }
        finally {
            proxyEndPointManager.UseRecentSucceeded = oldUseRecentSucceeded;
        }

        return hostStatuses;
    }

    private async Task VerifyHostStatus(HostStatus hostStatus, TimeSpan queryTimeout,
        CancellationToken cancellationToken)
    {
        // if first server is already marked as available, return it directly
        if (hostStatus.Available is not null)
            return;

        using var connector = CreateConnector(hostStatus.VpnEndPoint);
        hostStatus.Available = await VerifyServerStatus(connector, queryTimeout, cancellationToken).Vhc();
    }

    private static async Task<bool> VerifyServerStatus(ConnectorService connector, TimeSpan queryTimeout,
        CancellationToken cancellationToken)
    {
        try {
            VhLogger.Instance.LogInformation(GeneralEventId.Request,
                "Starting the first server verification. EndPoint: {EndPoint}",
                VhLogger.Format(connector.VpnEndPoint.TcpEndPoint));

            using var queryTimeoutCts = new CancellationTokenSource(queryTimeout); // timeout for each server query
            using var requestCts =
                CancellationTokenSource.CreateLinkedTokenSource(queryTimeoutCts.Token, cancellationToken);

            var requestResult = await connector
                .SendRequest<SessionResponse>(new ServerCheckRequest { RequestId = UniqueIdFactory.Create() },
                    requestCts.Token)
                .Vhc();

            // this should be already handled by the connector and never happen
            return requestResult.Response.ErrorCode is SessionErrorCode.Ok
                ? true
                : throw new SessionException(requestResult.Response.ErrorCode);
        }
        catch (UnauthorizedAccessException) {
            return true; // the server is available but not authorized
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw; // query cancelled due to discovery cancellationToken
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "Could not get server status. EndPoint: {EndPoint}",
                VhLogger.Format(connector.VpnEndPoint.TcpEndPoint));

            return false;
        }
    }

    private ConnectorService CreateConnector(VpnEndPoint vpnEndPoint)
    {
        var connector = new ConnectorService(
            options: new ConnectorServiceOptions(
                VpnEndPoint: vpnEndPoint,
                ProxyEndPointManager: proxyEndPointManager,
                SocketFactory: socketFactory,
                RequestTimeout: serverQueryTimeout,
                AllowTcpReuse: false)
        );

        connector.Init(
            protocolVersion: connector.ProtocolVersion, serverSecret: null,
            requestTimeout: serverQueryTimeout,
            tcpReuseTimeout: TimeSpan.Zero,
            useWebSocket: false);

        return connector;
    }
}