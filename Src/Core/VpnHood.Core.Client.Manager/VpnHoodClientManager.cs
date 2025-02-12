using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.ApiRequests;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Client.Manager;

public class VpnHoodClientManager : IJob, IAsyncDisposable
{
    private readonly TimeSpan _requestVpnServiceTimeout = TimeSpan.FromSeconds(120);
    private readonly TimeSpan _startVpnServiceTimeout = TimeSpan.FromSeconds(15);
    private readonly TimeSpan _connectionInfoTimeout = TimeSpan.FromSeconds(1);
    private readonly IDevice _device;
    private readonly IAdService _adService;
    private readonly string _vpnConfigFilePath;
    private readonly string _vpnStatusFilePath;
    private CancellationToken _cancellationToken = CancellationToken.None;
    private ConnectionInfo _connectionInfo;
    private DateTime? _connectionInfoTime;
    private TcpClient? _tcpClient;

    public event EventHandler? StateChanged;
    public JobSection JobSection { get; }
    public VpnHoodClientManager(IDevice device, IAdService adService, TimeSpan? eventWatcherInterval)
    {
        Directory.CreateDirectory(device.VpnServiceSharedFolder);
        _vpnConfigFilePath = Path.Combine(device.VpnServiceSharedFolder, ClientOptions.VpnConfigFileName);
        _vpnStatusFilePath = Path.Combine(device.VpnServiceSharedFolder, ClientOptions.VpnStatusFileName);
        _device = device;
        _adService = adService;
        _connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath) ?? SetConnectionInfo(ClientState.None);
        JobSection = new JobSection(eventWatcherInterval ?? TimeSpan.MaxValue);
        JobRunner.Default.Add(this);
    }

    public ConnectionInfo ConnectionInfo {
        get {
            _ = UpdateConnectionInfo(false, _cancellationToken);
            return _connectionInfo;
        }
    }

    private ConnectionInfo SetConnectionInfo(ClientState clientState, Exception? ex = null)
    {
        _connectionInfo = new ConnectionInfo {
            SessionInfo = null,
            SessionStatus = null,
            ApiEndPoint = null,
            ApiKey = null,
            ClientState = clientState,
            Error = ex?.ToApiError()
        };

        File.WriteAllText(_vpnStatusFilePath, JsonSerializer.Serialize(_connectionInfo));
        return _connectionInfo;

    }

    public async Task ClearState()
    {
        await ClearState(null);
    }

    public async Task ClearState(Exception? ex)
    {
        var connectionInfo = await UpdateConnectionInfo(true, CancellationToken.None);
        if (connectionInfo.ClientState == ClientState.Disposed)
            SetConnectionInfo(ClientState.None, ex);
    }

    public async Task Start(ClientOptions clientOptions, CancellationToken cancellationToken)
    {
        _cancellationToken = cancellationToken;
        _connectionInfo = SetConnectionInfo(ClientState.Connecting);

        // delete last config file
        if (File.Exists(_vpnStatusFilePath))
            File.Delete(_vpnStatusFilePath);

        // save vpn config
        await File.WriteAllTextAsync(_vpnConfigFilePath, JsonSerializer.Serialize(clientOptions), cancellationToken);

        // prepare vpn service
        VhLogger.Instance.LogInformation("Requesting VpnService ...");
        await _device.RequestVpnService(ActiveUiContext.Context, _requestVpnServiceTimeout, cancellationToken)
            .VhConfigureAwait();

        // start vpn service
        VhLogger.Instance.LogInformation("Starting VpnService ...");
        await _device.StartVpnService(cancellationToken)
            .VhConfigureAwait();

        // wait for vpn service
        await WaitForVpnService(cancellationToken);

        // wait for connection or error
        await WaitForConnection(cancellationToken);
    }

    private async Task WaitForVpnService(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Waiting for VpnService ...");
        var timeoutTask = Task.Delay(_startVpnServiceTimeout, cancellationToken);
        while (ConnectionInfo.ClientState == ClientState.None) {
            if (await Task.WhenAny(Task.Delay(100, cancellationToken), timeoutTask) == timeoutTask)
                throw new TimeoutException("VpnService did not start in time.");
        }
    }

    private async Task WaitForConnection(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Waiting for connection ...");
        var connectionInfo = ConnectionInfo;
        while (true) {
            // check for error
            if (connectionInfo.Error != null) 
                throw CreateException(connectionInfo.Error);

            // make sure it is not disposed
            if (connectionInfo.ClientState is ClientState.Disposed)
                throw new Exception("VpnService has been stopped unexpectedly.");

            if (connectionInfo.ClientState == ClientState.Connected)
                break;

            await Task.Delay(_connectionInfoTimeout, cancellationToken);
        }
    }

    private static Exception CreateException(ApiError apiError)
    {
        if (apiError.Is<SessionException>())
            return new SessionException(apiError);

        return apiError.ToException();
    }

    public async Task Stop()
    {
        if (!ConnectionInfo.IsStarted())
            return;

        try {
            await SendRequest(new ApiDisconnectRequest(), CancellationToken.None);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in Stopping VpnService.");
        }
    }

    public async Task Reconfigure(ClientReconfigureParams reconfigureParams, CancellationToken cancellationToken)
    {
        await SendRequest(new ApiReconfigureRequest { ReconfigureParams = reconfigureParams }, cancellationToken);

        // make sure the connection info is updated before caller gets the result of configuration
        await UpdateConnectionInfo(true, cancellationToken);
    }

    public Task ForceUpdateState(CancellationToken cancellationToken) => UpdateConnectionInfo(true, cancellationToken);

    private readonly AsyncLock _connectionInfoLock = new();

    private async Task<ConnectionInfo> UpdateConnectionInfo(bool force, CancellationToken cancellationToken)
    {
        // lock to prevent multiple updates
        using var scopeLock = await _connectionInfoLock.LockAsync(cancellationToken).ConfigureAwait(false);

        // read from cache if not expired
        if (!force && FastDateTime.Now - _connectionInfoTime < _connectionInfoTimeout)
            return _connectionInfo;
        _connectionInfoTime = FastDateTime.Now;

        // update from file to make sure there is no error
        // VpnClient always update the file when ConnectionState changes
        _connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath) ?? SetConnectionInfo(ClientState.None);
        if (_connectionInfo.Error != null || !_connectionInfo.IsStarted()) {
            CheckForEvents(_connectionInfo, cancellationToken);
            return _connectionInfo;
        }

        // connect to the server and get the connection info
        try {
            _connectionInfo = await SendRequest<ConnectionInfo>(new ApiConnectionInfoRequest(), cancellationToken);
        }
        catch (Exception ex) {
            // update connection info and set error
            _connectionInfo = SetConnectionInfo(ClientState.Disposed, new Exception("VpnService stopped unexpectedly.", ex));
            VhLogger.Instance.LogError(ex, "Error in UpdateConnectionInfo.");
        }

        CheckForEvents(_connectionInfo, cancellationToken);
        return _connectionInfo;
    }


    private Task SendRequest(IApiRequest request, CancellationToken cancellationToken)
    {
        return SendRequest<bool>(request, cancellationToken);
    }

    private readonly AsyncLock _sendLock = new();
    private async Task<T> SendRequest<T>(IApiRequest request, CancellationToken cancellationToken)
    {
        // for simplicity, we send one request at a time
        using var scopeLock = await _sendLock.LockAsync(cancellationToken).VhConfigureAwait();

        if (_connectionInfo == null)
            throw new InvalidOperationException("VpnService is not active.");

        if (_connectionInfo.Error != null)
            throw new InvalidOperationException("VpnService is not active.");

        if (_connectionInfo.ApiEndPoint == null || _connectionInfo.ClientState is ClientState.Disposed)
            throw new InvalidOperationException("ApiEndPoint is not available.");

        try {
            // establish and set the api key
            if (_tcpClient is not { Connected: true }) {
                _tcpClient?.Dispose();
                _tcpClient = new TcpClient(_connectionInfo.ApiEndPoint);
                await StreamUtils
                    .WriteObjectAsync(_tcpClient.GetStream(), _connectionInfo.ApiKey ?? [], cancellationToken)
                    .AsTask().VhConfigureAwait();
            }

            await StreamUtils.WriteObjectAsync(_tcpClient.GetStream(), request.GetType().Name, cancellationToken).AsTask().VhConfigureAwait();
            await StreamUtils.WriteObjectAsync(_tcpClient.GetStream(), request, cancellationToken).AsTask().VhConfigureAwait();
            var ret = await StreamUtils.ReadObjectAsync<T>(_tcpClient.GetStream(), cancellationToken).VhConfigureAwait();
            if (request is ApiDisconnectRequest) {
                _tcpClient.Dispose();
                _tcpClient = null;
            }

            return ret;
        }
        catch {
            _tcpClient?.Dispose();
            _tcpClient = null;
            throw;
        }
    }

    private ConnectionInfo? _lastConnectionInfo;
    private Guid? _lastAdRequestId;
    private void CheckForEvents(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
        // show ad if needed (Protect double show by RequestId)
        var adRequest = connectionInfo.SessionStatus?.AdRequest;
        if (adRequest != null && _lastAdRequestId != adRequest.RequestId) {
            _lastAdRequestId = adRequest.RequestId;
            _ = ShowAd(adRequest, cancellationToken);
        }

        // check if the state has changed
        if (_lastConnectionInfo?.ClientState != connectionInfo.ClientState) {
            Task.Run(() => StateChanged?.Invoke(this, EventArgs.Empty), CancellationToken.None);
        }

        _lastConnectionInfo = connectionInfo;
    }

    private async Task ShowAd(AdRequest adRequest, CancellationToken cancellationToken)
    {
        try {
            //todo: exclude ad from VPN
            var adRequestResult = adRequest.AdRequestType switch {
                AdRequestType.Rewarded => await _adService.ShowRewarded(ActiveUiContext.RequiredContext,
                    adRequest.SessionId, cancellationToken),
                AdRequestType.Interstitial => await _adService.ShowInterstitial(ActiveUiContext.RequiredContext,
                    adRequest.SessionId, cancellationToken),
                _ => throw new NotSupportedException(
                    $"The requested ad is not supported. AdRequestType={adRequest.AdRequestType}")
            };
            await SendRequest<ApiRequestedAdResult>(new ApiRequestedAdResult {
                ApiError = null,
                AdResult = adRequestResult
            }, cancellationToken);
        }
        catch (Exception ex) {
            await SendRequest<ApiRequestedAdResult>(new ApiRequestedAdResult {
                ApiError = ex.ToApiError(),
                AdResult = null
            }, cancellationToken);
        }
    }

    public async Task SendRewardedAdResult(AdResult adResult, CancellationToken cancellationToken)
    {
        await SendRequest<ApiRequestedAdResult>(new ApiRewardedAdResult {
            AdResult = adResult
        }, cancellationToken);
    }

    public async Task RunJob()
    {
        await UpdateConnectionInfo(false, CancellationToken.None);
    }

    public async ValueTask DisposeAsync()
    {
        await Stop();
        _tcpClient?.Dispose();
    }
}

