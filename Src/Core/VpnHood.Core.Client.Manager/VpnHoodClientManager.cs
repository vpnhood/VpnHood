using System.Text.Json;
using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client.Manager;

public class VpnHoodClientManager : IJob, IAsyncDisposable
{
    private CancellationToken _cancellationToken = CancellationToken.None;
    private readonly IAdService _adService;

    public JobSection JobSection { get; }
    public event EventHandler? StateChanged;
    public VpnHoodClient Client { get; } // temporary

    private VpnHoodClientManager(VpnHoodClient client, IAdService adService, TimeSpan? eventWatcherInterval)
    {
        Client = client;
        _adService = adService;
        JobSection = new JobSection(eventWatcherInterval ?? TimeSpan.MaxValue);
        JobRunner.Default.Add(this);
    }

    public static VpnHoodClientManager Create(
        IVpnAdapter vpnAdapter, ISocketFactory socketFactory, IAdService adService, ITracker? tracker,
        ClientOptions clientOptions, TimeSpan? eventWatcherInterval)
    {
        // save client options
        var vpnConfigFileName = Path.Combine(Directory.GetCurrentDirectory(), ClientOptions.VpnConfigFileName);
        File.WriteAllText(vpnConfigFileName, JsonSerializer.Serialize(clientOptions));

        var client = VpnHoodClientFactory.Create(vpnAdapter, socketFactory, tracker);
        return new VpnHoodClientManager(client, adService, eventWatcherInterval);
    }

    public async Task Start(CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        await Client.Connect(cancellationToken);
        await UpdateConnectionInfo(true);
    }

    public Task Stop()
    {
        return Client.DisposeAsync().AsTask();
    }

    public async Task Reconfigure(ClientUpdateParams updateParams)
    {
        Client.UseTcpOverTun = updateParams.UseTcpOverTun;
        Client.UseUdpChannel = updateParams.UseUdpChannel;
        Client.DropUdp = updateParams.DropUdp;
        Client.DropQuic = updateParams.DropQuic;

        // make sure the connection info is updated before caller get the result of configuration
        await UpdateConnectionInfo(true);
    }

    public ConnectionInfo? ConnectionInfo {
        get {
            _ = UpdateConnectionInfo();
            return _connectionInfo;
        }
    }

    public Task ForceUpdateState() => UpdateConnectionInfo(true);

    private ConnectionInfo? _connectionInfo;
    private DateTime? _connectionInfoTime;
    private readonly AsyncLock _connectionInfoLock = new();

    private async Task<ConnectionInfo> UpdateConnectionInfo(bool force = false)
    {
        using var scopeLock = await _connectionInfoLock.LockAsync(_cancellationToken).ConfigureAwait(false);
        if (!force && _connectionInfo != null && FastDateTime.Now - _connectionInfoTime < TimeSpan.FromSeconds(1))
            return _connectionInfo;

        // connect to the server and get the connection info
        var connectionInfo = Client.ApiController.GetConnectionInfo();

        _connectionInfoTime = FastDateTime.Now;
        _connectionInfo = connectionInfo;
        CheckForEvents(connectionInfo);
        return connectionInfo;
    }

    private ConnectionInfo? _lastConnectionInfo;
    private Guid? _lastAdRequestId;

    private void CheckForEvents(ConnectionInfo connectionInfo)
    {
        // show ad if needed (Protect double show by RequestId)
        var adRequest = connectionInfo.SessionStatus?.AdRequest;
        if (adRequest != null && _lastAdRequestId != adRequest.RequestId) {
            _lastAdRequestId = adRequest.RequestId;
            _ = ShowAd(adRequest);
        }

        // check if the state has changed
        if (_lastConnectionInfo?.ClientState != connectionInfo.ClientState) {
            StateChanged?.Invoke(this, EventArgs.Empty);
        }

        _lastConnectionInfo = connectionInfo;
    }

    private async Task ShowAd(AdRequest adRequest)
    {
        try {
            var adRequestResult = adRequest.AdRequestType switch {
                AdRequestType.Rewarded => await _adService.ShowRewarded(ActiveUiContext.RequiredContext,
                    adRequest.SessionId, _cancellationToken),
                AdRequestType.Interstitial => await _adService.ShowInterstitial(ActiveUiContext.RequiredContext,
                    adRequest.SessionId, _cancellationToken),
                _ => throw new NotSupportedException(
                    $"The requested ad is not supported. AdRequestType={adRequest.AdRequestType}")
            };
            Client.AdService.AdRequestTaskCompletionSource?.TrySetResult(adRequestResult);
        }
        catch (UiContextNotAvailableException) {
            Client.AdService.AdRequestTaskCompletionSource?.TrySetException(new ShowAdNoUiException());
        }
        catch (Exception ex) {
            Client.AdService.AdRequestTaskCompletionSource?.TrySetException(ex);
        }
    }

    public async Task SendAdRequest(AdRequest adRequest, CancellationToken cancellationToken)
    {
        await Client.AdService.Show(adRequest, cancellationToken);
        await UpdateConnectionInfo(true);
    }

    public async Task RunJob()
    {
        await UpdateConnectionInfo();
    }

    public ValueTask DisposeAsync()
    {
        return Client.DisposeAsync();
    }
}