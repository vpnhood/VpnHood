using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Sockets;
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
        VhLogger.Instance.LogDebug("VpnService ApiController has been started. EndPoint: {EndPoint}", ApiEndPoint);
    }


    private async Task Start(CancellationToken cancellationToken)
    {
        try {
            _tcpListener.Start();
            ApiEndPoint = (IPEndPoint)_tcpListener.LocalEndpoint;

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

    private async Task<ConnectionInfo> UpdateConnectionInfo(CancellationToken cancellationToken)
    {
        var client = _vpnHoodService.Client;
        if (client != null)
            await _vpnHoodService.UpdateConnectionInfo(client, cancellationToken: cancellationToken);

        return _vpnHoodService.Context.ConnectionInfo;
    }

    private async Task ProcessRequests(Stream stream, CancellationToken cancellationToken)
    {
        try {
            await ProcessRequestsInternal(stream, cancellationToken);
        }
        catch (Exception ex) when (_isDisposed == 0) {
            var response = new ApiResponse<object> {
                ConnectionInfo = await UpdateConnectionInfo(cancellationToken),
                ApiError = ex.ToApiError(),
                Result = null
            };

            await StreamUtils.WriteObjectAsync(stream, response, cancellationToken);
        }
    }

    private async Task ProcessRequestsInternal(Stream stream, CancellationToken cancellationToken)
    {
        // read request type
        var requestType = await StreamUtils.ReadObjectAsync<string>(stream, cancellationToken);
        switch (requestType) {

            // handle connection info request
            case nameof(ApiGetConnectionInfoRequest):
                await GetConnectionInfo(
                    await StreamUtils.ReadObjectAsync<ApiGetConnectionInfoRequest>(stream, cancellationToken),
                    cancellationToken);
                await WriteResponseResult(stream, null, cancellationToken);
                return;

            // handle ad request
            case nameof(ApiSetAdResultRequest):
                await SetAdResult(
                    await StreamUtils.ReadObjectAsync<ApiSetAdResultRequest>(stream, cancellationToken),
                    cancellationToken);
                await WriteResponseResult(stream, null, cancellationToken);
                return;

            // handle ad reward request
            case nameof(ApiSetWaitForAdRequest):
                await SetWaitForAd(
                    await StreamUtils.ReadObjectAsync<ApiSetWaitForAdRequest>(stream, cancellationToken),
                    cancellationToken);
                await WriteResponseResult(stream, null, cancellationToken);
                return;

            case nameof(ApiReconfigureRequest):
                await Reconfigure(
                    await StreamUtils.ReadObjectAsync<ApiReconfigureRequest>(stream, cancellationToken),
                    cancellationToken);
                await WriteResponseResult(stream, null, cancellationToken);
                return;

            // handle disconnect request
            case nameof(ApiDisconnectRequest):
                // write response before disconnecting
                await WriteResponseResult(stream, null, cancellationToken);

                // don't await and let dispose in the background so we can return the response quickly
                await Disconnect(
                    await StreamUtils.ReadObjectAsync<ApiDisconnectRequest>(stream, cancellationToken), cancellationToken);
                return;

            default:
                throw new InvalidOperationException($"Unknown request type: {requestType}");
        }
    }

    private async Task WriteResponseResult(Stream stream, object? result, CancellationToken cancellationToken)
    {
        var response = new ApiResponse<object> {
            ConnectionInfo = await UpdateConnectionInfo(cancellationToken),
            ApiError = null,
            Result = result
        };

        await StreamUtils.WriteObjectAsync(stream, response, cancellationToken);
    }

    private Task GetConnectionInfo(ApiGetConnectionInfoRequest request, CancellationToken cancellationToken)
    {
        _ = request;
        var connectionInfo = UpdateConnectionInfo(cancellationToken);
        return connectionInfo;
    }

    private Task Disconnect(ApiDisconnectRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _ = request;
        return _vpnHoodService.TryDisconnect();
    }

    private Task Reconfigure(ApiReconfigureRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;

        VpnHoodClient.Settings.DropUdp = request.Params.DropUdp;
        VpnHoodClient.Settings.DropQuic = request.Params.DropQuic;
        VpnHoodClient.Settings.UseTcpOverTun = request.Params.UseTcpOverTun;
        VpnHoodClient.UseUdpChannel = request.Params.UseUdpChannel;

        return Task.CompletedTask;
    }

    private ValueTask SetWaitForAd(ApiSetWaitForAdRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        _ = request;
        VpnHoodClient.IsWaitingForAd = true;
        return ValueTask.CompletedTask;
    }


    private async Task SetAdResult(ApiSetAdResultRequest request, CancellationToken cancellationToken)
    {
        _ = cancellationToken;
        VpnHoodClient.IsWaitingForAd = false;

        // Send rewarded ad result if it exists
        if (request is { IsRewarded: true, AdResult.AdData: not null })
            await VpnHoodClient.SendRewardedAdResult(request.AdResult.AdData, cancellationToken);
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
            return;

        _cancellationTokenSource.Cancel();
        _tcpListener.Stop();
    }
}