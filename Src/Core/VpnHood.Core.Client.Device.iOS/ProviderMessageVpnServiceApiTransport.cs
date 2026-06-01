using System.IO;
using Foundation;
using NetworkExtension;
using VpnHood.Core.Client.Device.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Toolkit.Streams;

namespace VpnHood.Core.Client.Device.iOS;

internal sealed class ProviderMessageVpnServiceApiTransport(NETunnelProviderManager vpnManager)
    : IVpnServiceApiTransport
{
    private const int MaxResultLength = 0xFFFFFF;

    public async Task<ApiResponse<T>> SendRequestAsync<T>(ConnectionInfo connectionInfo, IApiRequest request,
        CancellationToken cancellationToken)
    {
        if (request is ApiGetConnectionInfoRequest) {
            return new ApiResponse<T> {
                ConnectionInfo = connectionInfo,
                ApiError = null,
                Result = default
            };
        }

        if (vpnManager.Connection is not NETunnelProviderSession providerSession) {
            throw new VpnServiceUnreachableException("VpnService is unreachable.",
                new InvalidOperationException("NETunnelProviderSession is not available."));
        }

        await using var requestStream = new MemoryStream();
        await StreamUtils.WriteObjectAsync(requestStream, request.GetType().Name,
            ApiTransportJsonContext.For<string>(), cancellationToken);
        await StreamUtils.WriteObjectAsync(requestStream, request,
            ApiTransportJsonContext.Default.GetTypeInfo(request.GetType())!, cancellationToken);

        var responseDataTask = new TaskCompletionSource<NSData?>(TaskCreationOptions.RunContinuationsAsynchronously);
        using var cancellationRegistration = cancellationToken.Register(
            () => responseDataTask.TrySetCanceled(cancellationToken));

        var sent = providerSession.SendProviderMessage(NSData.FromArray(requestStream.ToArray()), out var sendError,
            responseData => responseDataTask.TrySetResult(responseData));

        if (!sent) {
            throw new VpnServiceUnreachableException("VpnService is unreachable.",
                sendError == null ? null : new Exception(sendError.LocalizedDescription));
        }

        var responseData = await responseDataTask.Task;
        if (responseData == null) {
            throw new VpnServiceUnreachableException("VpnService is unreachable.",
                new InvalidOperationException("Provider response was null."));
        }

        await using var responseStream = new MemoryStream(responseData.ToArray(), writable: false);
        return typeof(T) == typeof(object)
            ? (ApiResponse<T>)(object)await StreamUtils.ReadObjectAsync(responseStream,
                ApiTransportJsonContext.For<ApiResponse<object>>(), MaxResultLength, cancellationToken)
            : await StreamUtils.ReadObjectAsync<ApiResponse<T>>(responseStream, MaxResultLength, cancellationToken);
    }

    public void Reset()
    {
    }

    public void Dispose()
    {
    }
}
