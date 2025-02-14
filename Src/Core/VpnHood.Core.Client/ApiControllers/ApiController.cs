using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.ApiRequests;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Client.ApiControllers;

public class ApiController : IDisposable
{
    private readonly VpnHoodService _vpnHoodService;
    private readonly TcpListener _tcpListener;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private VpnHoodClient VpnHoodClient => _vpnHoodService.Client;
    public IPEndPoint ApiEndPoint => (IPEndPoint)_tcpListener.LocalEndpoint;
    public byte[] ApiKey { get; } = VhUtils.GenerateKey(128);
    public VpnHoodService? ServiceContext { get; set; }

    public ApiController(VpnHoodService vpnHoodService)
    {
        _vpnHoodService = vpnHoodService;
        _tcpListener = new TcpListener(IPAddress.Loopback, 0);
        _ = Start();
    }


    private async Task Start()
    {
        try {
            _tcpListener.Start();
            while (!_cancellationTokenSource.IsCancellationRequested) {
                var client = await _tcpListener.AcceptTcpClientAsync();
                _ = ProcessClientAsync(client, _cancellationTokenSource.Token);
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Client API Listener has been stopped.");
        }
        finally {
            _cancellationTokenSource.Cancel();
            _tcpListener.Stop();
        }
    }

    private async Task ProcessClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        try {
            await using var stream = client.GetStream();

            // read api key and compare
            var apiKey = StreamUtils.ReadObject<byte[]>(stream);
            if (!ApiKey.SequenceEqual(apiKey))
                throw new Exception("Invalid API key.");

            while (!cancellationToken.IsCancellationRequested)
                await ProcessRequests(stream, cancellationToken);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not handle API request.");
        }
        finally {
            client.Dispose();
        }
    }

    private async Task ProcessRequests(Stream stream, CancellationToken cancellationToken)
    {
        object result = true;

        // read request type
        var requestType = await StreamUtils.ReadObjectAsync<string>(stream, cancellationToken);
        switch (requestType) {

            // handle connection info request
            case nameof(ApiConnectionInfoRequest):
                result = await GetConnectionInfo(
                    await StreamUtils.ReadObjectAsync<ApiConnectionInfoRequest>(stream, cancellationToken), cancellationToken);
                break;

            // handle disconnect request
            case nameof(ApiDisconnectRequest):
                await Disconnect(
                    await StreamUtils.ReadObjectAsync<ApiDisconnectRequest>(stream, cancellationToken), cancellationToken);
                break;

            // handle ad request
            case nameof(ApiSetAdResultRequest):
                await SetAdResult(
                    await StreamUtils.ReadObjectAsync<ApiSetAdResultRequest>(stream, cancellationToken), cancellationToken);
                break;

            // handle ad reward request
            case nameof(ApiSendRewardedAdResultRequest):
                await SendRewardedAdResult(
                    await StreamUtils.ReadObjectAsync<ApiSendRewardedAdResultRequest>(stream, cancellationToken), cancellationToken);
                break;


            default:
                throw new InvalidOperationException($"Unknown request type: {requestType}");
        }

        // write the result
        await StreamUtils.WriteObjectAsync(stream, result, cancellationToken);
    }

    public async Task<ConnectionInfo> GetConnectionInfo(ApiConnectionInfoRequest request, CancellationToken cancellationToken)
    {
        var connectionInfo = VpnHoodClient.ToConnectionInfo(this);
        await _vpnHoodService.Context.SaveConnectionInfo(connectionInfo);
        return connectionInfo;
    }

    public Task Disconnect(ApiDisconnectRequest request, CancellationToken cancellationToken)
    {
        _ = VpnHoodClient.DisposeAsync();
        return Task.CompletedTask;
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


    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        _tcpListener.Stop();
    }
}