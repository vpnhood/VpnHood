using System.Collections.Concurrent;
using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Client.ConnectorServices;
using VpnHood.Common;
using VpnHood.Common.Exceptions;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Client;

public class ServerFinder(int maxDegreeOfParallelism = 10)
{
    public IReadOnlyDictionary<IPEndPoint, bool>? HostEndPointStatus { get; private set; }

    // There are much work to be done here
    public async Task<IPEndPoint?> FindBestServerAsync(ServerToken serverToken, ISocketFactory socketFactory, CancellationToken cancellationToken)
    {
        // create a connector for each endpoint
        var hostEndPoints = serverToken.HostEndPoints ?? [];
        var connectors = hostEndPoints.Select(tcpEndPoint =>
        {
            var endPointInfo = new ConnectorEndPointInfo
            {
                CertificateHash = serverToken.CertificateHash,
                HostName = serverToken.HostName,
                TcpEndPoint = tcpEndPoint
            };
            var connector = new ConnectorService(endPointInfo, socketFactory, TimeSpan.FromSeconds(10), false);
            connector.Init(0, TimeSpan.FromSeconds(10), serverToken.Secret, TimeSpan.FromSeconds(10));
            return connector;
        });

        // find endpoint status
        HostEndPointStatus = await VerifyServersStatus(connectors, cancellationToken);
        return HostEndPointStatus.FirstOrDefault(x=>x.Value).Key; //todo check if it is null
    }

    private async Task<IReadOnlyDictionary<IPEndPoint, bool>> VerifyServersStatus(IEnumerable<ConnectorService> connectors,
        CancellationToken cancellationToken)
    {
        var hostEndPointStatus = new ConcurrentDictionary<IPEndPoint, bool>();

        try
        {
            // check all servers
            using var cancellationTokenSource = new CancellationTokenSource();
            using var linkedCancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationTokenSource.Token, cancellationToken);
            await VhUtil.ParallelForEachAsync(connectors, async connector =>
            {
                var serverStatus = await VerifyServerStatus(connector, linkedCancellationTokenSource.Token);
                hostEndPointStatus[connector.EndPointInfo.TcpEndPoint] = serverStatus;
                if (serverStatus)
                    linkedCancellationTokenSource.Cancel(); // no need to continue, we find a server

            }, maxDegreeOfParallelism, linkedCancellationTokenSource.Token);

        }
        catch (OperationCanceledException)
        {
            // it means a server has been found
        }

        return hostEndPointStatus;
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
            }, cancellationToken);

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
}