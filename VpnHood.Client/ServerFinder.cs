using System.Net;
using Microsoft.Extensions.Logging;
using VpnHood.Client.ConnectorServices;
using VpnHood.Common;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Factory;
using VpnHood.Tunneling.Messaging;

namespace VpnHood.Client;

public class ServerFinder(int maxDegreeOfParallelism = 10)
{
    private readonly Dictionary<IPEndPoint, bool> _hostEndPointStatus = new();

    // There are much work to be done here
    public async Task<IPAddress?> FindBestServerAsync(ServerToken serverToken, ISocketFactory socketFactory, CancellationToken cancellationToken)
    {
        // create a connector for each endpoint
        var hostEndPoint = serverToken.HostEndPoints ?? [];
        var connectors = hostEndPoint.Select(x =>
        {
            var endPointInfo = new ConnectorEndPointInfo
            {
                CertificateHash = serverToken.CertificateHash,
                HostName = serverToken.HostName,
                TcpEndPoint = serverToken.HostEndPoints![0] //todo
            };
            var connector = new ConnectorService(endPointInfo, socketFactory, TimeSpan.FromSeconds(10), false);
            connector.Init(0, TimeSpan.FromSeconds(10), serverToken.Secret, TimeSpan.FromSeconds(10));
            return connector;
        });

        await VhUtil.ParallelForEachAsync(connectors, async connector =>
        {
            _hostEndPointStatus[connector.EndPointInfo.TcpEndPoint] = await CheckServerStatus(connector);
        }, maxDegreeOfParallelism, cancellationToken);

        throw new NotImplementedException();
    }

    private static async Task<bool> CheckServerStatus(ConnectorService connectorService)
    {
        try
        {
            var requestResult = await connectorService.SendRequest<ServerStatusResponse>(
                new ServerStatusRequest
                {
                    RequestId = Guid.NewGuid().ToString(),
                    Message = "Hi, How are you?"
                }, CancellationToken.None);

            return requestResult.Response.ErrorCode == SessionErrorCode.Ok;
        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogError(ex, "Could not get server status. EndPoint: {EndPoint}",
                VhLogger.Format(connectorService.EndPointInfo.TcpEndPoint));

            return false;
        }
    }
}