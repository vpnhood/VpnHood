using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions.ApiRequests;
using VpnHood.Core.Client.Device.Exceptions;
using VpnHood.Core.Common.Exceptions;
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
        _tcpListener.Start();

        try {
            while (true) {
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

            while (await ProcessRequests(stream, cancellationToken)) ;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not handle API request.");
        }
        finally {
            client.Dispose();
        }
    }

    private async Task<bool> ProcessRequests(Stream stream, CancellationToken cancellationToken)
    {
        // read request type
        var requestType = await StreamUtils.ReadObjectAsync<string>(stream, cancellationToken);
        switch (requestType) {

            // handle connection info request
            case nameof(ApiConnectionInfoRequest):
                await StreamUtils.ReadObjectAsync<ApiConnectionInfoRequest>(stream, cancellationToken);
                var connectionInfo = VpnHoodClient.ToConnectionInfo(this);
                _vpnHoodService.Context.SaveConnectionInfo(connectionInfo);
                await StreamUtils.WriteObjectAsync(stream, connectionInfo, cancellationToken);
                return true;

            // handle disconnect request
            case nameof(ApiDisconnectRequest):
                await StreamUtils.ReadObjectAsync<ApiDisconnectRequest>(stream, cancellationToken);
                await StreamUtils.WriteObjectAsync(stream, true, cancellationToken);
                _ = VpnHoodClient.DisposeAsync();
                return false;

            // handle ad request
            case nameof(ApiRequestedAdResult):
                var adResultRequest = await StreamUtils.ReadObjectAsync<ApiRequestedAdResult>(stream, cancellationToken);
                if (adResultRequest.AdResult != null)
                    VpnHoodClient.AdService.AdRequestTaskCompletionSource?.TrySetResult(adResultRequest.AdResult);
                else if (adResultRequest.ApiError?.Is<UiContextNotAvailableException>() == true ||
                         adResultRequest.ApiError?.Is<ShowAdNoUiException>() == true)
                    VpnHoodClient.AdService.AdRequestTaskCompletionSource?.TrySetException(new ShowAdNoUiException());
                else
                    VpnHoodClient.AdService.AdRequestTaskCompletionSource?.TrySetException(
                        adResultRequest.ApiError?.ToException() ?? new InvalidOperationException("Invalid ApiAdResultRequest."));
                return true;

            // handle ad reward request
            case nameof(ApiRewardedAdResult):
                var apiRewardedAdResult = await StreamUtils.ReadObjectAsync<ApiRewardedAdResult>(stream, cancellationToken);
                if (string.IsNullOrEmpty(apiRewardedAdResult.AdResult.AdData))
                    throw new InvalidOperationException("There is no AdData in ad reward.");
                await VpnHoodClient.AdService.SendRewardedAdResult(apiRewardedAdResult.AdResult.AdData, cancellationToken);
                return true;


            default:
                throw new InvalidOperationException($"Unknown request type: {requestType}");
        }
    }

    public void Dispose()
    {
        _tcpListener.Stop();
    }
}