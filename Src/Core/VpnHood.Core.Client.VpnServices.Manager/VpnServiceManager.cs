using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Client.VpnServices.Manager.Exceptions;
using VpnHood.Core.Common.Exceptions;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using FastDateTime = VpnHood.Core.Toolkit.Utils.FastDateTime;

namespace VpnHood.Core.Client.VpnServices.Manager;

public class VpnServiceManager : IJob, IDisposable
{
    private const int VpnServiceUnreachableThreshold = 1; // after this count we stop the service
    private readonly TimeSpan _requestVpnServiceTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(120);
    private readonly TimeSpan _startVpnServiceTimeout = Debugger.IsAttached ? Timeout.InfiniteTimeSpan : TimeSpan.FromSeconds(20);

    private readonly TimeSpan _connectionInfoTimeSpan = TimeSpan.FromSeconds(1);
    private readonly IDevice _device;
    private readonly IAdService? _adService;
    private readonly string _vpnConfigFilePath;
    private readonly string _vpnStatusFilePath;
    private ConnectionInfo _connectionInfo;
    private DateTime? _connectionInfoTime;
    private TcpClient? _tcpClient;
    private bool _isInitializing;
    private int _vpnServiceUnreachableCount;
    private CancellationTokenSource _updateConnectionInfoCts = new();
    private ConnectionInfo? _lastConnectionInfo;
    private Guid? _lastAdRequestId;

    public event EventHandler? StateChanged;
    public string LogFilePath => Path.Combine(_device.VpnServiceConfigFolder, ClientOptions.VpnLogFileName);
    public JobSection JobSection { get; }

    public VpnServiceManager(IDevice device, IAdService? adService, TimeSpan? eventWatcherInterval)
    {
        Directory.CreateDirectory(device.VpnServiceConfigFolder);
        _vpnConfigFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnConfigFileName);
        _vpnStatusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        _device = device;
        _adService = adService;
        _connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath) ??
                          BuildConnectionInfo(ClientState.None);

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
            HasSetByService = false,
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

    public bool IsStarted => _isInitializing || ConnectionInfo.IsStarted();

    public async Task Start(ClientOptions clientOptions, CancellationToken cancellationToken)
    {
        // wait for vpn service
        try {
            if (IsStarted)
                await Stop().VhConfigureAwait();

            _isInitializing = true;
            _vpnServiceUnreachableCount = 0;
            _tcpClient = null; // reset the tcp client to make sure we create a new one
            _updateConnectionInfoCts.Cancel();
            _updateConnectionInfoCts = new CancellationTokenSource();

            _connectionInfo = SetConnectionInfo(ClientState.Initializing);

            // save vpn config
            await File.WriteAllTextAsync(_vpnConfigFilePath, JsonSerializer.Serialize(clientOptions), cancellationToken)
                .VhConfigureAwait();

            // prepare vpn service
            VhLogger.Instance.LogInformation("Requesting VpnService...");
            if (!clientOptions.UseNullCapture)
                await _device.RequestVpnService(AppUiContext.Context, _requestVpnServiceTimeout, cancellationToken)
                    .VhConfigureAwait();

            // start vpn service
            VhLogger.Instance.LogInformation("Starting VpnService...");
            await _device.StartVpnService(cancellationToken).VhConfigureAwait();

            // wait for vpn service to start
            await WaitForVpnService(cancellationToken).VhConfigureAwait();
        }
        catch (Exception ex) {
            // It looks like the service is not running, set the state to disposed if it is still in initializing state
            var connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath);
            if (connectionInfo?.ClientState == ClientState.Initializing)
                SetConnectionInfo(ClientState.Disposed, ex);
            throw;
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
                throw new TimeoutException(
                    $"VpnService did not respond within {_startVpnServiceTimeout.TotalSeconds} seconds.");

            await Task.Delay(1000, cancellationToken);
            connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath);
        }

        _connectionInfo = connectionInfo;

        // success
        VhLogger.Instance.LogInformation(
            "VpnService has started. EndPoint: {EndPoint}, ConnectionState: {ConnectionState}",
            connectionInfo.ApiEndPoint, connectionInfo.ClientState);
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
        var connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath) ?? _connectionInfo;
        var serviceStopByItself = connectionInfo.HasSetByService && (connectionInfo.Error != null || !connectionInfo.IsStarted());
        if (_isInitializing || serviceStopByItself) {
            CheckForEvents(connectionInfo, cancellationToken);
            _connectionInfoTime = FastDateTime.Now;
            _connectionInfo = connectionInfo;
            return connectionInfo;
        }

        // connect to the server and get the connection info
        try {
            await SendRequest(new ApiGetConnectionInfoRequest(), cancellationToken);
            _vpnServiceUnreachableCount = 0; // reset the count if we successfully get the connection info
        }
        catch (Exception ex) when (updateConnectionInfoCt.IsCancellationRequested) {
            // it is a dead request from previous session
            VhLogger.Instance.LogWarning(ex, "Previous UpdateConnection Info has been ignored due to the new service.");
        }
        catch (VpnServiceUnreachableException ex) {
            // increment the count to stop the service if it is unreachable for too long
            _vpnServiceUnreachableCount++;

            // update connection info and set error
            if (_vpnServiceUnreachableCount == VpnServiceUnreachableThreshold)
                _connectionInfo = SetConnectionInfo(ClientState.Disposed, ex: new Exception("VpnService has stopped.", ex));

            // report it first time
            if (_vpnServiceUnreachableCount == 1)
                VhLogger.Instance.LogError(ex, "Could not update connection info.");
        }
        catch (Exception ex) {
            _vpnServiceUnreachableCount = 0; // reset the count if it is not VpnServiceUnreachableException
            VhLogger.Instance.LogError(ex, "Could not update connection info.");
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

        var ret = await SendRequestCore<T>(_connectionInfo.ApiEndPoint, request, cancellationToken);

        // update the last connection info
        _connectionInfo = ret.ConnectionInfo;
        _connectionInfoTime = FastDateTime.Now;

        // convert to error. 
        if (ret.ApiError != null)
            throw ClientExceptionConverter.ApiErrorToException(ret.ApiError);

        return ret.Result;
    }

    private async Task<ApiResponse<T>> SendRequestCore<T>(IPEndPoint hostEndPoint, IApiRequest request, CancellationToken cancellationToken)
    {
        var tcpClient = _tcpClient;
        try {
            // establish and set the api key
            if (tcpClient is not { Connected: true }) {
                VhLogger.Instance.LogDebug("Connecting to VpnService Host... EndPoint: {EndPoint}", hostEndPoint);
                tcpClient?.Dispose();
                tcpClient = new TcpClient();
                await tcpClient.VhConnectAsync(hostEndPoint, cancellationToken);
                await StreamUtils
                    .WriteObjectAsync(tcpClient.GetStream(), _connectionInfo.ApiKey ?? [], cancellationToken)
                    .AsTask().VhConfigureAwait();
                VhLogger.Instance.LogDebug("Connected to VpnService Host.");
            }

            await StreamUtils.WriteObjectAsync(tcpClient.GetStream(), request.GetType().Name, cancellationToken)
                .AsTask().VhConfigureAwait();
            await StreamUtils.WriteObjectAsync(tcpClient.GetStream(), request, cancellationToken).AsTask()
                .VhConfigureAwait();
            var ret = await StreamUtils.ReadObjectAsync<ApiResponse<T>>(tcpClient.GetStream(), cancellationToken)
                .VhConfigureAwait();

            return ret;
        }
        catch (Exception ex) {
            tcpClient?.Dispose();
            tcpClient = null;
            throw new VpnServiceUnreachableException("VpnService is unreachable.", ex);
        }
        finally {
            _tcpClient = tcpClient;
        }
    }

    /// <summary>
    /// Stop the VPN service and disconnect from the server if running. This method is idempotent.
    /// No exception will be thrown
    /// </summary>
    public async Task Stop(TimeSpan? timeout = null)
    {
        // stop the service
        if (!ConnectionInfo.IsStarted())
            return;

        using var cancellationTokenSource = new CancellationTokenSource(
            Debugger.IsAttached ? Timeout.InfiniteTimeSpan : timeout ?? TimeSpan.FromSeconds(5));

        // send disconnect request
        try {
            VhLogger.Instance.LogDebug("Sending disconnect request...");
            await SendRequest(new ApiDisconnectRequest(), cancellationTokenSource.Token).VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not send disconnect request.");
        }

        // wait for the service to stop
        VhLogger.Instance.LogDebug("Waiting for VpnService to stop.");
        try {
            while (ConnectionInfo.IsStarted()) {
                await UpdateConnectionInfo(true, cancellationTokenSource.Token);
                await Task.Delay(200, cancellationTokenSource.Token);
            }

            VhLogger.Instance.LogDebug("VpnService has been stopped.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not stop the VpnService.");
        }
    }

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
            if (_adService == null)
                throw new NotSupportedException("There is no AdService available in this app.");

            var adRequestResult = adRequest.AdRequestType switch {
                AdRequestType.Rewarded => await _adService.ShowRewarded(AppUiContext.RequiredContext,
                    adRequest.SessionId, cancellationToken),
                AdRequestType.Interstitial => await _adService.ShowInterstitial(AppUiContext.RequiredContext,
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
        JobRunner.Default.Remove(this);
        _updateConnectionInfoCts.Cancel();
        _tcpClient?.Dispose();
    }
}