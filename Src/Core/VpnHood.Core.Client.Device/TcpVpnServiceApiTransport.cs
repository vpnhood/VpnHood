using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.Device;

public sealed class TcpVpnServiceApiTransport : IVpnServiceApiTransport
{
    private const int MaxResultLength = 0xFFFFFF;
    private TcpClient? _tcpClient;

    public async Task<ApiResponse<T>> SendRequestAsync<T>(ConnectionInfo connectionInfo, IApiRequest request,
        CancellationToken cancellationToken)
    {
        var tcpClient = _tcpClient;
        if (tcpClient != null) {
            try {
                return await SendOverClientAsync<T>(tcpClient, request, cancellationToken).Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(ex,
                    "Could not send request to VpnService Host. EndPoint: {EndPoint}",
                    tcpClient.TryGetRemoteEndPoint());
                Reset();
            }
        }

        if (connectionInfo.ApiEndPoint == null)
            throw new VpnServiceNotReadyException("ApiEndPoint is not available.");
        if (connectionInfo.ApiKey == null)
            throw new VpnServiceNotReadyException("ApiKey is not available.");

        _tcpClient = await ConnectToVpnService(connectionInfo.ApiEndPoint, connectionInfo.ApiKey, cancellationToken)
            .Vhc();

        try {
            return await SendOverClientAsync<T>(_tcpClient, request, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            Reset();
            throw new VpnServiceUnreachableException(
                $"VpnService is unreachable. EndPoint: {connectionInfo.ApiEndPoint}", ex);
        }
    }

    private static async Task<ApiResponse<T>> SendOverClientAsync<T>(TcpClient tcpClient, IApiRequest request,
        CancellationToken cancellationToken)
    {
        var stream = tcpClient.GetStream();
        await StreamUtils.WriteObjectAsync(stream, request.GetType().Name,
            ApiTransportJsonContext.For<string>(), cancellationToken).Vhc();
        await StreamUtils.WriteObjectAsync(stream, request,
            ApiTransportJsonContext.Default.GetTypeInfo(request.GetType())!, cancellationToken).Vhc();

        return typeof(T) == typeof(object)
            ? (ApiResponse<T>)(object)await StreamUtils
                .ReadObjectAsync(stream, ApiTransportJsonContext.For<ApiResponse<object>>(),
                    MaxResultLength, cancellationToken).Vhc()
            : await StreamUtils.ReadObjectAsync<ApiResponse<T>>(stream, MaxResultLength, cancellationToken).Vhc();
    }

    private static async Task<TcpClient> ConnectToVpnService(IPEndPoint apiEndPoint, byte[] apiKey,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Connecting to VpnService Host... EndPoint: {EndPoint}", apiEndPoint);
        var tcpClient = new TcpClient { NoDelay = true };
        try {
            await tcpClient.ConnectAsync(apiEndPoint, cancellationToken).Vhc();
            await StreamUtils.WriteObjectAsync(tcpClient.GetStream(), apiKey,
                ApiTransportJsonContext.For<byte[]>(), cancellationToken).Vhc();
            VhLogger.Instance.LogDebug("Connected to VpnService Host. LocalEp: {LocalEp}, RemoteEp: {RemoteEp}",
                tcpClient.TryGetLocalEndPoint(), tcpClient.TryGetRemoteEndPoint());
            return tcpClient;
        }
        catch (Exception ex) {
            tcpClient.Dispose();
            throw new VpnServiceUnreachableException($"VpnService is unreachable. EndPoint: {apiEndPoint}", ex);
        }
    }

    public void Reset()
    {
        _tcpClient?.Dispose();
        _tcpClient = null;
    }

    public void Dispose()
    {
        Reset();
    }
}
