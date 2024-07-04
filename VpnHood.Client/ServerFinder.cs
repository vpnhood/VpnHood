using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Client.ConnectorServices;
using VpnHood.Client.Exceptions;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Client;

public class ServerFinder(ISocketFactory socketFactory, ServerToken serverToken, 
    TimeSpan serverQueryTimeout, int maxDegreeOfParallelism = 10)
{
    private HostStatus[] _hostEndPointStatus = [];

    private static void Shuffle<T>(T[] array)
    {
        var rng = new Random();
        var n = array.Length;
        for (var i = n - 1; i > 0; i--)
        {
            var j = rng.Next(i + 1);
            (array[i], array[j]) = (array[j], array[i]);
        }
    }

    // There are much work to be done here
    public async Task<IPEndPoint> FindBestServerAsync(CancellationToken cancellationToken)
    {
        // get all endpoints from serverToken
        var hostEndPoints = await ServerTokenHelper.ResolveHostEndPoints(serverToken);
        if (!hostEndPoints.Any())
            throw new Exception("Could not find any server endpoint. Please check your access key.");

        // for compatibility don't query server for single endpoint
        if (hostEndPoints.Length == 1)
            return hostEndPoints.First();

        // randomize endpoint 
        Shuffle(hostEndPoints); //todo check

        _hostEndPointStatus = await VerifyServersStatus(hostEndPoints, byOrder: false, cancellationToken: cancellationToken);
        var res = _hostEndPointStatus.FirstOrDefault(x => x.Available == true)?.TcpEndPoint;
        return res ?? throw new NoReachableServer();
    }

    public async Task<IPEndPoint> FindBestServerAsync(IPEndPoint[] hostEndPoints, CancellationToken cancellationToken)
    {
        if (!hostEndPoints.Any())
            throw new Exception("There is no server endpoint. Please check server configuration.");

        var hostStatuses = hostEndPoints
            .Select(x => new HostStatus { TcpEndPoint = x })
            .ToArray();

        // merge old values
        foreach (var hostStatus in hostStatuses)
            hostStatus.Available = _hostEndPointStatus
                .FirstOrDefault(x => x.TcpEndPoint.Equals(hostStatus.TcpEndPoint))?.Available;

        var result = await VerifyServersStatus(hostEndPoints, byOrder: true, cancellationToken: cancellationToken);
        var res = result.FirstOrDefault(x => x.Available == true)?.TcpEndPoint;
        return res ?? throw new NoReachableServer();
    }

    private class HostStatus
    {
        public required IPEndPoint TcpEndPoint { get; init; }
        public bool? Available { get; set; }
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
        catch (UnauthorizedAccessException)
        {
            throw;
        }
        catch (SessionException)
        {
            throw;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not get server status. EndPoint: {EndPoint}",
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