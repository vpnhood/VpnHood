using System.Net;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.ConnectorServices;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Tokens;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Messaging;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Client;

public class ServerFinder(
    ISocketFactory socketFactory,
    ServerToken serverToken,
    string? serverLocation,
    TimeSpan serverQueryTimeout,
    ITracker? tracker,
    int maxDegreeOfParallelism = 10)
{
    private class HostStatus
    {
        public required IPEndPoint TcpEndPoint { get; init; }
        public bool? Available { get; set; }
    }

    private HostStatus[] _hostEndPointStatuses = [];
    public bool IncludeIpV6 { get; set; } = true;
    public string? ServerLocation => serverLocation;

    // There are much work to be done here
    public async Task<IPEndPoint> FindReachableServerAsync(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Session, "Finding a reachable server... QueryTimeout: {QueryTimeout}", 
            serverQueryTimeout);

        // get all endpoints from serverToken
        var hostEndPoints = await serverToken.ResolveHostEndPoints(cancellationToken);

        // exclude ip v6 if not supported
        if (!IncludeIpV6)
            hostEndPoints = hostEndPoints.Where(x => !x.Address.IsV6() || x.Address.Equals(IPAddress.IPv6Loopback))
                .ToArray();

        // randomize endpoint 
        VhUtils.Shuffle(hostEndPoints);

        // find the best server
        _hostEndPointStatuses = await VerifyServersStatus(hostEndPoints, byOrder: false, cancellationToken: cancellationToken);
        var res = _hostEndPointStatuses.FirstOrDefault(x => x.Available == true)?.TcpEndPoint;

        VhLogger.Instance.LogInformation(GeneralEventId.Session,
            "ServerFinder result. Reachable:{Reachable}, Unreachable:{Unreachable}, Unknown: {Unknown}",
            _hostEndPointStatuses.Count(x => x.Available == true),
            _hostEndPointStatuses.Count(x => x.Available == false),
            _hostEndPointStatuses.Count(x => x.Available == null));

        _ = TryTrackEndPointsAvailability([], _hostEndPointStatuses).Vhc();
        if (res != null)
            return res;

        _ = tracker?.TryTrack(ClientTrackerBuilder.BuildConnectionFailed(serverLocation: ServerLocation,
            isIpV6Supported: IncludeIpV6, hasRedirected: false));

        throw new UnreachableServerException(BuildExceptionMessage(ServerLocation));
    }

    public async Task<IPEndPoint> FindBestRedirectedServerAsync(IPEndPoint[] hostEndPoints,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation(GeneralEventId.Session, "Finding best server from redirected endpoints...");

        if (!hostEndPoints.Any())
            throw new Exception("There is no server endpoint. Please check server configuration.");

        var hostStatuses = hostEndPoints
            .Select(x => new HostStatus { TcpEndPoint = x })
            .ToArray();

        // exclude ip v6 if not supported (IPv6Loopback is for tests)
        if (!IncludeIpV6)
            hostEndPoints = hostEndPoints.Where(x => !x.Address.IsV6() || x.Address.Equals(IPAddress.IPv6Loopback))
                .ToArray();

        // merge old values
        foreach (var hostStatus in hostStatuses)
            hostStatus.Available = _hostEndPointStatuses
                .FirstOrDefault(x => x.TcpEndPoint.Equals(hostStatus.TcpEndPoint))?.Available;

        var endpointStatuses = await VerifyServersStatus(hostEndPoints, byOrder: true, cancellationToken: cancellationToken);
        var res = endpointStatuses.FirstOrDefault(x => x.Available == true)?.TcpEndPoint;

        VhLogger.Instance.LogInformation(GeneralEventId.Session,
            "ServerFinder result. Reachable:{Reachable}, Unreachable:{Unreachable}, Unknown: {Unknown}, Best: {Best}",
            endpointStatuses.Count(x => x.Available == true), endpointStatuses.Count(x => x.Available == false),
            endpointStatuses.Count(x => x.Available == null),
            VhLogger.Format(res));

        // track new endpoints availability 
        _ = TryTrackEndPointsAvailability(_hostEndPointStatuses, endpointStatuses).Vhc();
        if (res != null)
            return res;

        _ = tracker?.TryTrack(ClientTrackerBuilder.BuildConnectionFailed(serverLocation: ServerLocation,
            isIpV6Supported: IncludeIpV6, hasRedirected: true));

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
                !oldStatuses.Any(y => y.Available == x.Available && y.TcpEndPoint.Equals(x.TcpEndPoint)))
            .ToArray();

        var trackEvents = changesStatus
            .Where(x => x.Available != null)
            .Select(x => ClientTrackerBuilder.BuildEndPointStatus(x.TcpEndPoint, x.Available!.Value))
            .ToArray();

        // report endpoints
        var endPointReport = string.Join(", ",
            changesStatus.Select(x => $"{VhLogger.Format(x.TcpEndPoint)} => {x.Available}"));
        VhLogger.Instance.LogInformation(GeneralEventId.Session, "HostEndPoints: {EndPoints}", endPointReport);

        return tracker?.TryTrack(trackEvents) ?? Task.CompletedTask;
    }

    private async Task<HostStatus[]> VerifyServersStatus(IPEndPoint[] hostEndPoints, bool byOrder,
        CancellationToken cancellationToken)
    {
        var hostStatuses = hostEndPoints
            .Select(x => new HostStatus { TcpEndPoint = x })
            .ToArray();

        using var searchingCts = new CancellationTokenSource(); // this will be cancelled when a server is found
        using var parallelCts = CancellationTokenSource.CreateLinkedTokenSource(searchingCts.Token, cancellationToken);
        try {
            // check all servers
            var parallelOptions = new ParallelOptions {
                CancellationToken = parallelCts.Token,
                MaxDegreeOfParallelism = maxDegreeOfParallelism
            };
            await Parallel.ForEachAsync(hostStatuses, parallelOptions, async (hostStatus, ct) => {
                using var connector = CreateConnector(hostStatus.TcpEndPoint);
                hostStatus.Available = await VerifyServerStatus(connector, serverQueryTimeout, ct).Vhc();

                // ReSharper disable once AccessToDisposedClosure
                if (hostStatus.Available == true && !byOrder)
                    await searchingCts.CancelAsync().Vhc(); // no need to continue, we find a server

                // search by order
                if (byOrder) {
                    // ReSharper disable once LoopCanBeConvertedToQuery (It can not! [false, false, null, true] is not accepted )
                    foreach (var item in hostStatuses) {
                        if (item.Available == null)
                            break; // wait to get the result in order

                        if (item.Available.Value) {
                            // ReSharper disable once AccessToDisposedClosure
                            await searchingCts.CancelAsync().Vhc();
                            break;
                        }
                    }
                }
            }).Vhc();
        }
        catch (OperationCanceledException) when (searchingCts.IsCancellationRequested) {
            // it means a server has been found
        }


        return hostStatuses;
    }

    private static async Task<bool> VerifyServerStatus(ConnectorService connector, TimeSpan queryTimeout,
        CancellationToken cancellationToken)
    {
        try {
            using var queryTimeoutCts = new CancellationTokenSource(queryTimeout); // timeout for each server query
            using var requestCts = CancellationTokenSource.CreateLinkedTokenSource(queryTimeoutCts.Token, cancellationToken);
            var requestResult = await connector
                .SendRequest<SessionResponse>(new ServerCheckRequest { RequestId = UniqueIdFactory.Create() }, requestCts.Token)
                .Vhc();

            // this should be already handled by the connector and never happen
            if (requestResult.Response.ErrorCode != SessionErrorCode.Ok)
                throw new SessionException(requestResult.Response.ErrorCode);

            return true;
        }
        catch (UnauthorizedAccessException) {
            return true; // server is available but not authorized
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) {
            throw; // query cancelled due to discovery cancellationToken
        }
        catch (Exception ex) {
            VhLogger.Instance.LogInformation(ex, "Could not get server status. EndPoint: {EndPoint}",
                VhLogger.Format(connector.EndPointInfo.TcpEndPoint));

            return false;
        }
    }

    private ConnectorService CreateConnector(IPEndPoint tcpEndPoint)
    {
        var endPointInfo = new ConnectorEndPointInfo {
            CertificateHash = serverToken.CertificateHash,
            HostName = serverToken.HostName,
            TcpEndPoint = tcpEndPoint
        };
        var connector = new ConnectorService(endPointInfo, socketFactory, serverQueryTimeout, false);
        connector.Init(
            protocolVersion: connector.ProtocolVersion, serverSecret: null,
            requestTimeout: serverQueryTimeout,
            tcpReuseTimeout: TimeSpan.Zero,
            useWebSocket: false);
        return connector;
    }
}