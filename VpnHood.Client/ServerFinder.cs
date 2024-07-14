using System.Net;
using Ga4.Trackers;
using Microsoft.Extensions.Logging;
using VpnHood.Client.ConnectorServices;
using VpnHood.Client.Exceptions;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Net;
using VpnHood.Common.Utils;
using VpnHood.Tunneling;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Client;

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
    public async Task<IPEndPoint> FindBestServerAsync(CancellationToken cancellationToken)
    {
        // get all endpoints from serverToken
        var hostEndPoints = await ServerTokenHelper.ResolveHostEndPoints(serverToken);
        if (!hostEndPoints.Any())
            throw new Exception("Could not find any server endpoint. Please check your access key.");

        // exclude ip v6 if not supported
        if (!IncludeIpV6)
            hostEndPoints = hostEndPoints.Where(x => !x.Address.IsV6() || x.Address.Equals(IPAddress.IPv6Loopback)).ToArray();

        // for compatibility don't query server for single endpoint
        // todo: does not need on 535 or upper due to ServerStatusRequest
        if (hostEndPoints.Count(x => x.Address.IsV4()) == 1)
            return hostEndPoints.First(x=>x.Address.IsV4());

        // randomize endpoint 
        VhUtil.Shuffle(hostEndPoints);

        // find the best server
        _hostEndPointStatuses = await VerifyServersStatus(hostEndPoints, byOrder: false, cancellationToken: cancellationToken);
        var res = _hostEndPointStatuses.FirstOrDefault(x => x.Available == true)?.TcpEndPoint;

        _ = TrackEndPointsAvailability([], _hostEndPointStatuses).VhConfigureAwait();
        if (res != null)
            return res;

        _ = tracker?.Track(ClientTrackerBuilder.BuildConnectionAttempt(connected: false, serverLocation: ServerLocation, isIpV6Supported: IncludeIpV6));
        throw new UnreachableServer(serverLocation: ServerLocation);
    }

    public async Task<IPEndPoint> FindBestServerAsync(IPEndPoint[] hostEndPoints, CancellationToken cancellationToken)
    {
        if (!hostEndPoints.Any())
            throw new Exception("There is no server endpoint. Please check server configuration.");

        var hostStatuses = hostEndPoints
            .Select(x => new HostStatus { TcpEndPoint = x })
            .ToArray();

        // exclude ip v6 if not supported (IPv6Loopback is for tests)
        if (!IncludeIpV6)
            hostEndPoints = hostEndPoints.Where(x =>!x.Address.IsV6() || x.Address.Equals(IPAddress.IPv6Loopback)).ToArray();

        // merge old values
        foreach (var hostStatus in hostStatuses)
            hostStatus.Available = _hostEndPointStatuses
                .FirstOrDefault(x => x.TcpEndPoint.Equals(hostStatus.TcpEndPoint))?.Available;

        var results = await VerifyServersStatus(hostEndPoints, byOrder: true, cancellationToken: cancellationToken);
        var res = results.FirstOrDefault(x => x.Available == true)?.TcpEndPoint;

        // track new endpoints availability 
        _ = TrackEndPointsAvailability(_hostEndPointStatuses, results).VhConfigureAwait();
        if (res != null)
            return res;

        _ = tracker?.Track(ClientTrackerBuilder.BuildConnectionAttempt(connected: false, serverLocation: ServerLocation, isIpV6Supported: IncludeIpV6));
        throw new UnreachableServer(serverLocation: ServerLocation);
    }

    private Task TrackEndPointsAvailability(HostStatus[] oldStatuses, HostStatus[] newStatuses)
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
        var endPointReport = string.Join(", ", changesStatus.Select(x => $"{VhLogger.Format(x.TcpEndPoint)} => {x.Available}"));
        VhLogger.Instance.LogInformation(GeneralEventId.Session, "HostEndPoints: {EndPoints}", endPointReport);

        return tracker?.Track(trackEvents) ?? Task.CompletedTask;
    }

    private async Task<HostStatus[]> VerifyServersStatus(IPEndPoint[] hostEndPoints, bool byOrder, CancellationToken cancellationToken)
    {
        var hostStatuses = hostEndPoints
            .Select(x => new HostStatus { TcpEndPoint = x })
            .ToArray();

        try
        {
            // check all servers
            using var cancellationTokenSource = new CancellationTokenSource();
            using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);
            await VhUtil.ParallelForEachAsync(hostStatuses, async hostStatus =>
            {
                var connector = CreateConnector(hostStatus.TcpEndPoint);

                // ReSharper disable once AccessToDisposedClosure
                hostStatus.Available = await VerifyServerStatus(connector, linkedCancellationTokenSource.Token).VhConfigureAwait();

                // ReSharper disable once AccessToDisposedClosure
                if (hostStatus.Available == true && !byOrder)
                    linkedCancellationTokenSource.Cancel(); // no need to continue, we find a server

                // search by order
                if (byOrder)
                {
                    // ReSharper disable once LoopCanBeConvertedToQuery (It can not! [false, false, null, true] is not accepted )
                    foreach (var item in hostStatuses)
                    {
                        if (item.Available == null)
                            break; // wait to get the result in order

                        if (item.Available.Value)
                        {
                            // ReSharper disable once AccessToDisposedClosure
                            linkedCancellationTokenSource.Cancel();
                            break;
                        }
                    }
                }

            }, maxDegreeOfParallelism, linkedCancellationTokenSource.Token).VhConfigureAwait();

        }
        catch (OperationCanceledException)
        {
            // it means a server has been found
        }

        return hostStatuses;
    }

    private static async Task<bool> VerifyServerStatus(ConnectorService connector, CancellationToken cancellationToken)
    {
        try
        {
            var requestResult = await connector.SendRequest<ServerStatusResponse>(
                    new ServerStatusRequest
                    {
                        RequestId = Guid.NewGuid().ToString(),
                        Message = "Hi, How are you?"
                    },
                    cancellationToken)
                .VhConfigureAwait();

            // this should be already handled by the connector and never happen
            if (requestResult.Response.ErrorCode != SessionErrorCode.Ok)
                throw new SessionException(requestResult.Response.ErrorCode);

            return true;
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
            throw; // query cancelled due to discovery cancellationToken
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(ex, "Could not get server status. EndPoint: {EndPoint}",
                VhLogger.Format(connector.EndPointInfo.TcpEndPoint));

            return false;
        }
    }

    private ConnectorService CreateConnector(IPEndPoint tcpEndPoint)
    {
        var endPointInfo = new ConnectorEndPointInfo
        {
            CertificateHash = serverToken.CertificateHash,
            HostName = serverToken.HostName,
            TcpEndPoint = tcpEndPoint
        };
        var connector = new ConnectorService(endPointInfo, socketFactory, serverQueryTimeout, false);
        connector.Init(serverProtocolVersion: 0, tcpRequestTimeout: serverQueryTimeout, serverSecret: serverToken.Secret, tcpReuseTimeout: TimeSpan.Zero);
        return connector;
    }
}