using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host;

internal class ApiController : IDisposable
{
    private bool _disposed;
    private readonly VpnServiceHost _vpnHoodService;
    private readonly IMessageListener _messageListener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private VpnHoodClient VpnHoodClient => _vpnHoodService.RequiredClient;

    public ApiController(VpnServiceHost vpnHoodService, IMessageListener messageListener)
    {
        _vpnHoodService = vpnHoodService;
        _messageListener = messageListener;

        // start listening for messages; the transport owns connection/endpoint/key concerns
        _ = _messageListener.Start(ProcessMessageAsync, _cancellationTokenSource.Token);
        VhLogger.Instance.LogDebug("VpnService ApiController has been started.");
    }

    // Handle a single request message and return the response payload. The transport is
    // responsible for framing, connections, endpoints and authentication.
    public async Task<Memory<byte>> ProcessMessageAsync(Memory<byte> request, CancellationToken cancellationToken)
    {
        try {
            // the request payload carries [len][requestType][len][requestBody] (StreamUtils framing)
            using var requestStream = new MemoryStream(request.Length);
            await requestStream.WriteAsync(request, cancellationToken).Vhc();
            requestStream.Position = 0;

            var result = await ProcessRequestInternal(requestStream, cancellationToken).Vhc();
            return BuildResponseBuffer(await UpdateConnectionInfo(cancellationToken).Vhc(), result, apiError: null);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not handle API request.");
            return BuildResponseBuffer(await UpdateConnectionInfo(cancellationToken).Vhc(),
                result: null, apiError: ex.ToApiError());
        }
    }

    private static Memory<byte> BuildResponseBuffer(ConnectionInfo connectionInfo, object? result, ApiError? apiError)
    {
        var response = new ApiResponse<object> {
            ConnectionInfo = connectionInfo,
            ApiError = apiError,
            Result = result
        };

        // raw JSON bytes; the transport adds its own framing
        var jsonTypeInfo = ApiTransportJsonContext.For<ApiResponse<object>>();
        return System.Text.Json.JsonSerializer.SerializeToUtf8Bytes(response, jsonTypeInfo);
    }

    private async Task<ConnectionInfo> UpdateConnectionInfo(CancellationToken cancellationToken)
    {
        var client = _vpnHoodService.Client;
        if (client != null)
            await _vpnHoodService.UpdateConnectionInfo(client, cancellationToken: cancellationToken);

        return _vpnHoodService.Context.ConnectionInfo;
    }

    private async Task<object?> ProcessRequestInternal(Stream stream, CancellationToken cancellationToken)
    {
        // read request type
        var requestType = await StreamUtils.ReadObjectAsync<string>(stream, cancellationToken).Vhc();
        VhLogger.Instance.LogTrace("ApiController is reading a request: {RequestType}", requestType);

        switch (requestType) {
            // handle connection info request
            case nameof(ApiGetConnectionInfoRequest):
                await GetConnectionInfo(
                    await StreamUtils.ReadObjectAsync<ApiGetConnectionInfoRequest>(stream, cancellationToken).Vhc(),
                    cancellationToken).Vhc();
                return null;

            case nameof(ApiSetAdOkRequest):
                await SetAdOk(
                    await StreamUtils.ReadObjectAsync<ApiSetAdOkRequest>(stream, cancellationToken).Vhc(),
                    cancellationToken).Vhc();
                return null;

            // handle ad request
            case nameof(ApiAdFailedRequest):
                await SetAdFailed(
                    await StreamUtils.ReadObjectAsync<ApiAdFailedRequest>(stream, cancellationToken).Vhc(),
                    cancellationToken).Vhc();
                return null;

            // handle ad reward request
            case nameof(ApiSetWaitForAdRequest):
                await SetWaitForAd(
                    await StreamUtils.ReadObjectAsync<ApiSetWaitForAdRequest>(stream, cancellationToken).Vhc(),
                    cancellationToken).Vhc();
                return null;

            case nameof(ApiReconfigureRequest):
                await Reconfigure(
                    await StreamUtils.ReadObjectAsync<ApiReconfigureRequest>(stream, maxLength: 0xFFFFFF,
                        cancellationToken).Vhc(),
                    cancellationToken).Vhc();
                return null;

            // handle disconnect request
            case nameof(ApiDisconnectRequest):
                await Disconnect(
                    await StreamUtils.ReadObjectAsync<ApiDisconnectRequest>(stream, cancellationToken).Vhc(),
                    cancellationToken).Vhc();
                return null;

            default:
                throw new InvalidOperationException($"Unknown request type: {requestType}");
        }
    }

    private Task GetConnectionInfo(ApiGetConnectionInfoRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        return UpdateConnectionInfo(cancellationToken);
    }

    private Task Disconnect(ApiDisconnectRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _ = request;

        // don't await; let disconnect run in the background so we can return the response quickly
        _ = _vpnHoodService.TryDisconnect();
        return Task.CompletedTask;
    }

    private Task Reconfigure(ApiReconfigureRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        VpnHoodClient.UseTcpProxy = request.Params.UseTcpProxy;
        VpnHoodClient.DropUdp = request.Params.DropUdp;
        VpnHoodClient.DropQuic = request.Params.DropQuic;
        VpnHoodClient.ChannelProtocol = request.Params.ChannelProtocol;
        VpnHoodClient.ProxyEndPointManager.UpdateOptions(request.Params.ProxyOptions);

        return Task.CompletedTask;
    }

    private Task SetWaitForAd(ApiSetWaitForAdRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _ = request;

        // Do not await, we should not hold up the API response, the client will call SetAdOk or SetAdFailed when the ad is done
        // Do not use request cancellation token
        var adHandler = VpnHoodClient.RequiredSession.AdHandler;
        _ = adHandler.WaitForAd(CancellationToken.None); 
        return Task.CompletedTask;
    }

    private async Task SetAdOk(ApiSetAdOkRequest request, CancellationToken cancellationToken)
    {
        var adHandler = VpnHoodClient.RequiredSession.AdHandler;
        
        // Send rewarded ad result if it exists
        if (request.IsRewarded && !string.IsNullOrEmpty(request.AdResult.AdData))
            await adHandler.SendRewardedAdData(request.AdResult.AdData, cancellationToken);
        else
            adHandler.SetAdOk();
    }

    private Task SetAdFailed(ApiAdFailedRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        var adHandler = VpnHoodClient.RequiredSession.AdHandler;
        adHandler.SetAdFailed(request.ApiError?.ToException() ?? new Exception("Failed to load ad."));
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _messageListener.Dispose();
    }
}