using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Host;

internal class ApiController : IDisposable
{
    private int _isDisposed;
    private readonly VpnServiceHost _vpnHoodService;
    private readonly TcpListener _tcpListener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private VpnHoodClient VpnHoodClient => _vpnHoodService.RequiredClient;
    public IPEndPoint? ApiEndPoint { get; private set; }
    public byte[] ApiKey { get; } = VhUtils.GenerateKey(128);
    public VpnServiceHost? ServiceContext { get; set; }

    public ApiController(VpnServiceHost vpnHoodService)
    {
        _vpnHoodService = vpnHoodService;
        _tcpListener = new TcpListener(IPAddress.Loopback, 0);
        _ = Start(_cancellationTokenSource.Token);
    }


    private async Task Start(CancellationToken cancellationToken)
    {
        try {
            _tcpListener.Start();
            ApiEndPoint = (IPEndPoint)_tcpListener.LocalEndpoint;

            // write initial connection info after setting ApiEndPoint
            await _vpnHoodService.Context.TryWriteConnectionInfo(BuildConnectionInfo(ClientState.None, null), cancellationToken);
            VhLogger.Instance.LogInformation("VpnService host Listener has started. EndPoint: {EndPoint}", ApiEndPoint);

            while (!cancellationToken.IsCancellationRequested) {
                var client = await _tcpListener.AcceptTcpClientAsync(cancellationToken);
                _ = ProcessClientAsync(client, cancellationToken);
            }
        }
        catch (Exception ex) {
            if (_isDisposed == 0)
                VhLogger.Instance.LogError(ex, "VpnService host Listener has stopped.");
        }
        finally {
            _tcpListener.Stop();
            VhLogger.Instance.LogDebug("VpnService host Listener has been stopped. EndPoint: {EndPoint}", ApiEndPoint);
            Dispose();
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try {
            await using var stream = client.GetStream();

            // read api key and compare
            var apiKey = await StreamUtils.ReadObjectAsync<byte[]>(stream, cancellationToken);
            if (!ApiKey.SequenceEqual(apiKey))
                throw new Exception("Invalid API key.");

            while (!cancellationToken.IsCancellationRequested)
                await ProcessRequests(stream, cancellationToken);
        }
        catch (Exception ex) {
            if (_isDisposed == 0)
                VhLogger.Instance.LogError(ex, "Could not handle API request.");
        }
        finally {
            client.Dispose();
        }
    }

    private async Task<ConnectionInfo> GetConnectionInfoOrDefault()
    {
        return _vpnHoodService.Client?.ToConnectionInfo(this) ??
               await _vpnHoodService.Context.ReadConnectionInfoOrDefault(ApiKey, ApiEndPoint);
    }

    private async Task ProcessRequests(Stream stream, CancellationToken cancellationToken)
    {
        try {
            var result = await ProcessRequestsInternal(stream, cancellationToken);

            // write response
            var response = new ApiResponse<object> {
                ApiError = null,
                ConnectionInfo = await GetConnectionInfoOrDefault(),
                Result = result
            };
            await StreamUtils.WriteObjectAsync(stream, response, cancellationToken);
        }
        catch (Exception ex) when (_isDisposed == 0) {
            var response = new ApiResponse<object> {
                ApiError = ex.ToApiError(),
                ConnectionInfo = await GetConnectionInfoOrDefault(),
                Result = null
            };
            await StreamUtils.WriteObjectAsync(stream, response, cancellationToken);
        }
    }

    private async Task<object?> ProcessRequestsInternal(Stream stream, CancellationToken cancellationToken)
    {
        // read request type
        var requestType = await StreamUtils.ReadObjectAsync<string>(stream, cancellationToken);
        switch (requestType) {

            // handle connection info request
            case nameof(ApiGetConnectionInfoRequest):
                await GetConnectionInfo(
                    await StreamUtils.ReadObjectAsync<ApiGetConnectionInfoRequest>(stream, cancellationToken),
                    cancellationToken);
                return null;

            // handle ad request
            case nameof(ApiSetAdResultRequest):
                await SetAdResult(
                    await StreamUtils.ReadObjectAsync<ApiSetAdResultRequest>(stream, cancellationToken), cancellationToken);
                return null;

            // handle ad reward request
            case nameof(ApiSendRewardedAdResultRequest):
                await SendRewardedAdResult(
                    await StreamUtils.ReadObjectAsync<ApiSendRewardedAdResultRequest>(stream, cancellationToken), cancellationToken);
                return null;

            // handle disconnect request
            case nameof(ApiDisconnectRequest):
                // don't await and let dispose in the background so we can return the response quickly
                _ =  Disconnect(
                    await StreamUtils.ReadObjectAsync<ApiDisconnectRequest>(stream, cancellationToken), cancellationToken);
                return null;


            default:
                throw new InvalidOperationException($"Unknown request type: {requestType}");
        }
    }

    public Task GetConnectionInfo(ApiGetConnectionInfoRequest request, CancellationToken cancellationToken)
    {
        var connectionInfo = _vpnHoodService.Client?.ToConnectionInfo(this);
        return connectionInfo != null ?
            _vpnHoodService.Context.TryWriteConnectionInfo(connectionInfo, cancellationToken)
            : Task.CompletedTask;
    }

    public async Task Disconnect(ApiDisconnectRequest request, CancellationToken cancellationToken)
    {
        await Task.Delay(300, cancellationToken);
        await _vpnHoodService.TryDisconnect();
    }

    public Task SetAdResult(ApiSetAdResultRequest request, CancellationToken cancellationToken)
    {
        if (request.AdResult != null) {
            VpnHoodClient.AdService.AdRequestTaskCompletionSource?.TrySetResult(request.AdResult);
            return Task.CompletedTask;
        }

        // handle error
        if (request.ApiError is null)
            throw new InvalidOperationException("Invalid ApiAdResultRequest. There is no ApiError nor AdResult");


        VpnHoodClient.AdService.AdRequestTaskCompletionSource?
            .TrySetException(ClientExceptionConverter.ApiErrorToException(request.ApiError));

        return Task.CompletedTask;
    }

    public Task SendRewardedAdResult(ApiSendRewardedAdResultRequest request, CancellationToken cancellationToken)
    {
        if (string.IsNullOrEmpty(request.AdResult.AdData))
            throw new InvalidOperationException("There is no AdData in ad reward.");

        return VpnHoodClient.AdService.SendRewardedAdResult(request.AdResult.AdData, cancellationToken);
    }

    public ConnectionInfo BuildConnectionInfo(ClientState clientState, Exception? ex)
    {
        var connectionInfo = new ConnectionInfo {
            SessionInfo = null,
            SessionStatus = null,
            ApiEndPoint = ApiEndPoint,
            ApiKey = ApiKey,
            ClientState = clientState,
            Error = ex?.ToApiError(),
            HasSetByService = true
        };

        return connectionInfo;
    }


    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        _cancellationTokenSource.Cancel();
        _tcpListener.Stop();
    }
}