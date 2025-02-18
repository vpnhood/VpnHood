using System.Diagnostics;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.ToolKit;
using FastDateTime = VpnHood.Core.Common.Utils.FastDateTime;

namespace VpnHood.Core.Client.VpnServices.Manager;

public class VpnServiceManager : IJob, IDisposable
{
    private readonly TimeSpan _requestVpnServiceTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(120);
    private readonly TimeSpan _startVpnServiceTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(15);
    private readonly TimeSpan _connectionInfoTimeSpan = TimeSpan.FromSeconds(1);
    private readonly IDevice _device;
    private readonly IAdService _adService;
    private readonly string _vpnConfigFilePath;
    private readonly string _vpnStatusFilePath;
    private ConnectionInfo _connectionInfo;
    private DateTime? _connectionInfoTime;
    private TcpClient? _tcpClient;
    private bool _isInitializing;

    public event EventHandler? StateChanged;
    public string LogFilePath => Path.Combine(_device.VpnServiceConfigFolder, ClientOptions.VpnLogFileName);
    public JobSection JobSection { get; }
    public VpnServiceManager(IDevice device, IAdService adService, TimeSpan? eventWatcherInterval)
    {
        Directory.CreateDirectory(device.VpnServiceConfigFolder);
        _vpnConfigFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnConfigFileName);
        _vpnStatusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        _device = device;
        _adService = adService;
        _connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath) ?? BuildConnectionInfo(ClientState.None);

        JobSection = new JobSection(eventWatcherInterval ?? TimeSpan.MaxValue);
        JobRunner.Default.Add(this);
    }

    public ConnectionInfo ConnectionInfo {
        get {
            _ = UpdateConnectionInfo(false, CancellationToken.None);
            return _connectionInfo;
        }
    }

    private static ConnectionInfo BuildConnectionInfo(ClientState clientState, Exception? ex = null)
    {
        return new ConnectionInfo {
            SessionInfo = null,
            SessionStatus = null,
            ApiEndPoint = null,
            ApiKey = null,
            ClientState = clientState,
            Error = ex?.ToApiError()
        };
    }

    private ConnectionInfo SetConnectionInfo(ClientState clientState, Exception? ex = null)
    {
        _connectionInfo = BuildConnectionInfo(clientState, ex);
        try {
            File.WriteAllText(_vpnStatusFilePath, JsonSerializer.Serialize(_connectionInfo));
        }
        catch {
            // it is ok if we can't write to the file
            // It means the service is overwriting the file
        }

        return _connectionInfo;

    }

    public async Task Start(ClientOptions clientOptions, CancellationToken cancellationToken)
    {
        // wait for vpn service
        try {
            _isInitializing = true;
            using var autoDispose = new AutoDispose(() => _isInitializing = false);
            _updateConnectionInfoCts.Cancel();
            _updateConnectionInfoCts = new CancellationTokenSource();

            _connectionInfo = SetConnectionInfo(ClientState.Initializing);

            // save vpn config
            await File.WriteAllTextAsync(_vpnConfigFilePath, JsonSerializer.Serialize(clientOptions), cancellationToken)
                .VhConfigureAwait();

            // prepare vpn service
            VhLogger.Instance.LogInformation("Requesting VpnService...");
            await _device.RequestVpnService(ActiveUiContext.Context, _requestVpnServiceTimeout, cancellationToken)
                .VhConfigureAwait();

            // start vpn service
            VhLogger.Instance.LogInformation("Starting VpnService...");
            await _device.StartVpnService(cancellationToken).VhConfigureAwait();

            // wait for vpn service to start
            await WaitForVpnService(cancellationToken).VhConfigureAwait();
        }
        catch (Exception ex) {
            // It looks like the service is not running however UpdateConnectionInfo will recover it if it is running
            SetConnectionInfo(ClientState.Disposed, ex);
            throw;
        }
        finally {
            // VpnService may be launched by the system. We should make sure update connection info is running
            _updateConnectionInfoCts = new CancellationTokenSource();
        }

        // wait for connection or error
        await WaitForConnection(cancellationToken).VhConfigureAwait();
    }

    private async Task WaitForVpnService(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Waiting for VpnService to start...");
        using var timeoutCts = new CancellationTokenSource(_startVpnServiceTimeout);

        // directly read file because UpdateConnection will set the state to disposed if it could not connect to the service
        // UpdateConnection will fail due to it cancellation 
        var connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath);

        // wait for vpn service to start
        while (connectionInfo == null || connectionInfo.ClientState is ClientState.None or ClientState.Initializing) {
            cancellationToken.ThrowIfCancellationRequested();
            if (timeoutCts.IsCancellationRequested)
                throw new TimeoutException($"VpnService did not respond within {_startVpnServiceTimeout.TotalSeconds} seconds.");

            await Task.Delay(300, cancellationToken);
            connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath);
        }
        _connectionInfo = connectionInfo;
        VhLogger.Instance.LogInformation("VpnService has started. EndPoint: {EndPoint}", connectionInfo.ApiEndPoint);
    }

    private async Task WaitForConnection(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Waiting for VpnService to establish a connection ...");
        while (true) {
            var connectionInfo = ConnectionInfo;

            // check for error
            if (connectionInfo.Error != null)
                throw ClientExceptionConverter.ApiErrorToException(connectionInfo.Error);

            // make sure it is not disposed
            if (connectionInfo.ClientState is ClientState.Disposed or ClientState.Disconnecting)
                throw new Exception("VpnService could not establish any connection.");

            if (connectionInfo.ClientState == ClientState.Connected)
                break;

            await Task.Delay(_connectionInfoTimeSpan, cancellationToken);
        }
        VhLogger.Instance.LogDebug("The VpnService has established a connection.");
    }

    public Task ForceUpdateState(CancellationToken cancellationToken) => UpdateConnectionInfo(true, cancellationToken);


    private readonly AsyncLock _connectionInfoLock = new();
    private CancellationTokenSource _updateConnectionInfoCts = new();

    private async Task<ConnectionInfo> UpdateConnectionInfo(bool force, CancellationToken cancellationToken)
    {
        // read from cache if not expired
        if (_isInitializing || (!force && FastDateTime.Now - _connectionInfoTime < _connectionInfoTimeSpan))
            return _connectionInfo;

        // build a new token source to cancel the previous request
        var updateConnectionInfoCt = _updateConnectionInfoCts.Token;
        using var linedTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, updateConnectionInfoCt);
        cancellationToken = linedTokenSource.Token;

        // lock to prevent multiple updates
        using var scopeLock = await _connectionInfoLock.LockAsync(cancellationToken).ConfigureAwait(false);

        // read from cache if not expired
        if (_isInitializing || (!force && FastDateTime.Now - _connectionInfoTime < _connectionInfoTimeSpan))
            return _connectionInfo;

        // update from file to make sure there is no error
        // VpnClient always update the file when ConnectionState changes
        // Should send request if service is in initializing state, because SendRequest will set the state to disposed if failed
        _connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath) ?? _connectionInfo;
        if (_isInitializing || _connectionInfo.Error != null || !_connectionInfo.IsStarted()) {
            CheckForEvents(_connectionInfo, cancellationToken);
            _connectionInfoTime = FastDateTime.Now;
            return _connectionInfo;
        }

        // connect to the server and get the connection info
        try {
            await SendRequest(new ApiGetConnectionInfoRequest(), cancellationToken);
        }
        catch (Exception ex) when (updateConnectionInfoCt.IsCancellationRequested) {
            VhLogger.Instance.LogWarning(ex, "Previous UpdateConnection Info has been ignored due to the new service.");
        }
        catch (Exception ex) {
            // update connection info and set error
            _connectionInfo = SetConnectionInfo(ClientState.Disposed, new Exception("VpnService has been stopped.", ex));
            VhLogger.Instance.LogDebug(ex, "Could not update connection info.");
        }

        CheckForEvents(_connectionInfo, cancellationToken);
        _connectionInfoTime = FastDateTime.Now;
        return _connectionInfo;
    }


    private Task SendRequest(IApiRequest request, CancellationToken cancellationToken)
    {
        return SendRequest<object>(request, cancellationToken);
    }

    private readonly AsyncLock _sendLock = new();
    private async Task<T?> SendRequest<T>(IApiRequest request, CancellationToken cancellationToken)
    {
        // for simplicity, we send one request at a time
        using var scopeLock = await _sendLock.LockAsync(TimeSpan.FromSeconds(5), cancellationToken).VhConfigureAwait();

        if (_connectionInfo == null)
            throw new InvalidOperationException("VpnService is not active.");

        if (_connectionInfo.Error != null)
            throw new InvalidOperationException("VpnService is not active.");

        if (_connectionInfo.ApiEndPoint == null)
            throw new InvalidOperationException("ApiEndPoint is not available.");

        var hostEndPoint = _connectionInfo.ApiEndPoint;
        try {
            // establish and set the api key
            if (_tcpClient is not { Connected: true }) {
                VhLogger.Instance.LogDebug("Connecting to VpnService Host... EndPoint: {EndPoint}", hostEndPoint);
                _tcpClient?.Dispose();
                _tcpClient = new TcpClient();
                await _tcpClient.VhConnectAsync(hostEndPoint, cancellationToken);
                await StreamUtils
                    .WriteObjectAsync(_tcpClient.GetStream(), _connectionInfo.ApiKey ?? [], cancellationToken)
                    .AsTask().VhConfigureAwait();
                VhLogger.Instance.LogDebug("Connected to VpnService Host.");
            }

            await StreamUtils.WriteObjectAsync(_tcpClient.GetStream(), request.GetType().Name, cancellationToken).AsTask().VhConfigureAwait();
            await StreamUtils.WriteObjectAsync(_tcpClient.GetStream(), request, cancellationToken).AsTask().VhConfigureAwait();
            var ret = await StreamUtils.ReadObjectAsync<ApiResponse<T>>(_tcpClient.GetStream(), cancellationToken).VhConfigureAwait();
            if (request is ApiDisconnectRequest) {
                _tcpClient.Dispose();
                _tcpClient = null;
            }

            // update the last connection info
            _connectionInfo = ret.ConnectionInfo;
            _connectionInfoTime = FastDateTime.Now;

            // convert to error. 
            if (ret.ApiError != null)
                throw ClientExceptionConverter.ApiErrorToException(ret.ApiError);

            return ret.Result;
        }
        catch {
            _tcpClient?.Dispose();
            _tcpClient = null;
            throw;
        }
    }

    /// <summary>
    /// Stop the VPN service and disconnect from the server if running. This method is idempotent.
    /// No exception will be thrown
    /// </summary>
    public async Task Stop(TimeSpan? timeSpan = null)
    {
        using var timeoutCts = new CancellationTokenSource(
            Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(5));

        // stop the service
        if (!ConnectionInfo.IsStarted())
            return;

        // send disconnect request
        try {
            await SendRequest(new ApiDisconnectRequest(), timeoutCts.Token).VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not send disconnect request.");
        }

        // wait for the service to stop
        VhLogger.Instance.LogDebug("Waiting for VpnService to stop.");
        try {
            while (ConnectionInfo.IsStarted()) {
                await UpdateConnectionInfo(true, timeoutCts.Token);
                await Task.Delay(200, timeoutCts.Token);
            }
            VhLogger.Instance.LogDebug("VpnService has been stopped.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not stop the VpnService.");
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
            VhLogger.Instance.LogDebug("The VpnService state has been changed. {OldSate} => {NewState}",
                _lastConnectionInfo?.ClientState, connectionInfo.ClientState);

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
            await SendRequest(new ApiSetAdResultRequest {
                ApiError = null,
                AdResult = adRequestResult
            }, cancellationToken);
        }
        catch (Exception ex) {
            if (ex is UiContextNotAvailableException)
                ex = new ShowAdNoUiException();

            await SendRequest(new ApiSetAdResultRequest {
                ApiError = ex.ToApiError(),
                AdResult = null
            }, cancellationToken);
        }
    }

    public Task Reconfigure(ClientReconfigureParams reconfigureParams, CancellationToken cancellationToken)
    {
        return SendRequest(new ApiReconfigureRequest { Params = reconfigureParams }, cancellationToken);
    }

    public Task SendRewardedAdResult(AdResult adResult, CancellationToken cancellationToken)
    {
        return SendRequest(new ApiSendRewardedAdResultRequest { AdResult = adResult }, cancellationToken);
    }

    public async Task RunJob()
    {
        // let _updateConnectionInfoCts inside UpdateConnection info handle the cancellation
        await UpdateConnectionInfo(false, CancellationToken.None);
    }

    public void Dispose()
    {
        // do not stop, lets service keep running until user explicitly stop it
        _updateConnectionInfoCts.Cancel();
        _tcpClient?.Dispose();
        JobRunner.Default.Remove(this);
    }
}

