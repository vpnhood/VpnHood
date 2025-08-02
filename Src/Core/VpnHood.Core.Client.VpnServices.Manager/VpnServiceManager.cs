using Microsoft.Extensions.Logging;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Client.Device.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Client.VpnServices.Manager.Exceptions;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Manager;

public class VpnServiceManager : IDisposable
{
    private const int VpnServiceUnreachableThreshold = 1; // after this count we stop the service
    private readonly TimeSpan _requestVpnServiceTimeout = Debugger.IsAttached ? VhUtils.DebuggerTimeout : TimeSpan.FromSeconds(120);
    private readonly TimeSpan _startVpnServiceTimeout = Debugger.IsAttached ? VhUtils.DebuggerTimeout : TimeSpan.FromSeconds(20);
    private bool _disposed;

    private readonly TimeSpan _connectionInfoTimeSpan = TimeSpan.FromSeconds(1);
    private readonly IDevice _device;
    private readonly string _vpnConfigFilePath;
    private readonly string _vpnStatusFilePath;
    private ConnectionInfo _connectionInfo;
    private DateTime? _connectionInfoRefreshedTime;
    private TcpClient? _tcpClient;
    private bool _isInitializing;
    private int _vpnServiceUnreachableCount;
    private CancellationTokenSource _updateConnectionInfoCts = new();
    private ConnectionInfo? _lastConnectionInfo;
    private readonly Job _updateConnectionInfoJob;

    public event EventHandler? StateChanged;
    public string LogFilePath => Path.Combine(_device.VpnServiceConfigFolder, ClientOptions.VpnLogFileName);

    public VpnServiceManager(IDevice device, TimeSpan? eventWatcherInterval)
    {
        Directory.CreateDirectory(device.VpnServiceConfigFolder);
        _vpnConfigFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnConfigFileName);
        _vpnStatusFilePath = Path.Combine(device.VpnServiceConfigFolder, ClientOptions.VpnStatusFileName);
        _device = device;
        _connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath)
                          ?? BuildConnectionInfo(ClientState.None);

        _updateConnectionInfoJob = new Job(UpdateConnectionInfoJob,
            eventWatcherInterval ?? TimeSpan.MaxValue, nameof(UpdateConnectionInfoJob));
    }

    public ConnectionInfo ConnectionInfo {
        get {
            _ = TryRefreshConnectionInfo(false, CancellationToken.None);
            return _connectionInfo;
        }
    }

    private static ConnectionInfo BuildConnectionInfo(ClientState clientState, Exception? ex = null)
    {
        return new ConnectionInfo {
            CreatedTime = null,
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

    public bool IsStarted => _isInitializing || ConnectionInfo.IsStarted();

    public async Task Start(ClientOptions clientOptions, CancellationToken cancellationToken)
    {
        // wait for vpn service
        try {
            if (IsStarted)
                await TryStop().Vhc();

            _isInitializing = true;
            _vpnServiceUnreachableCount = 0;
            await _updateConnectionInfoCts.TryCancelAsync().Vhc();
            _updateConnectionInfoCts.Dispose();
            _updateConnectionInfoCts = new CancellationTokenSource();

            _connectionInfo = SetConnectionInfo(ClientState.Initializing);

            // save vpn config
            await File.WriteAllTextAsync(_vpnConfigFilePath, JsonSerializer.Serialize(clientOptions),
                    cancellationToken).Vhc();

            // prepare vpn service
            VhLogger.Instance.LogInformation("Requesting VpnService...");
            if (!clientOptions.UseNullCapture)
                await _device.RequestVpnService(AppUiContext.Context, _requestVpnServiceTimeout,
                    cancellationToken).Vhc();

            // start vpn service
            VhLogger.Instance.LogInformation("Starting VpnService...");
            VhUtils.TryDeleteFile(LogFilePath); // remove previous service log
            await _device.StartVpnService(cancellationToken).Vhc();

            // wait for vpn service to start
            await WaitForVpnService(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            // It looks like the service is not running, set the state to disposed if it is still in initializing state
            var connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath);
            if (connectionInfo?.ClientState == ClientState.Initializing)
                SetConnectionInfo(ClientState.Disposed, ex);
            throw;
        }
        finally {
            _isInitializing = false;
        }

        // wait for connection or error
        await WaitForConnection(cancellationToken).Vhc();
    }

    private async Task WaitForVpnService(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Waiting for VpnService to start...");
        using var timeoutCts = new CancellationTokenSource(_startVpnServiceTimeout);

        try {
            using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // directly read file because UpdateConnection will set the state to disposed if it could not connect to the service
            // UpdateConnection will fail due to it cancellation 
            var connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath);

            // wait for vpn service to start
            while (connectionInfo == null || connectionInfo.ClientState is ClientState.None or ClientState.Initializing) {
                await Task.Delay(1000, localCts.Token).Vhc();
                connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath);
            }

            _connectionInfo = connectionInfo;
            _tcpClient = null; // reset the tcp client to make sure we create a new one 
            VhLogger.Instance.LogInformation(
                "VpnService has started. EndPoint: {EndPoint}, ConnectionState: {ConnectionState}",
                connectionInfo.ApiEndPoint, connectionInfo.ClientState);
        }
        catch (Exception ex) when (timeoutCts.IsCancellationRequested) {
            var serviceTimeoutException = new VpnServiceTimeoutException(
                $"Could not start the VpnService in {_startVpnServiceTimeout.TotalSeconds} seconds.", ex) {
                TimeoutDuration = _startVpnServiceTimeout
            };
            throw serviceTimeoutException;
        }
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

            await Task.Delay(_connectionInfoTimeSpan, cancellationToken).Vhc();
        }

        VhLogger.Instance.LogDebug("The VpnService has established a connection.");
    }

    public Task ForceRefreshState(CancellationToken cancellationToken) => RefreshConnectionInfo(true, cancellationToken);

    private async Task<ConnectionInfo> TryRefreshConnectionInfo(bool force, CancellationToken cancellationToken)
    {
        try {
            return await RefreshConnectionInfo(force, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not update connection info.");
            return _connectionInfo;
        }
    }

    private readonly AsyncLock _connectionInfoLock = new();
    private async Task<ConnectionInfo> RefreshConnectionInfo(bool force, CancellationToken cancellationToken)
    {
        // build a new token source to cancel the previous request
        using var updateCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _updateConnectionInfoCts.Token);
        using var scopeLock = await _connectionInfoLock.LockAsync(updateCts.Token).Vhc();

        // read from cache if not expired
        if (_isInitializing || (!force && FastDateTime.Now - _connectionInfoRefreshedTime < _connectionInfoTimeSpan))
            return _connectionInfo;

        // update from file to make sure there is no error
        // VpnClient always update the file when ConnectionState changes
        // Should send request if service is in initializing state, because SendRequest will set the state to disposed if failed
        _connectionInfo = JsonUtils.TryDeserializeFile<ConnectionInfo>(_vpnStatusFilePath) ?? _connectionInfo;
        _connectionInfoRefreshedTime = FastDateTime.Now;
        if (_isInitializing || _connectionInfo.Error != null || !_connectionInfo.IsStarted()) {
            CheckForEvents();
            return _connectionInfo;
        }

        // connect to the server and get the connection info
        try {
            await SendRequest(new ApiGetConnectionInfoRequest(), updateCts.Token).Vhc();
            _vpnServiceUnreachableCount = 0; // reset the count if we successfully get the connection info
        }
        catch (Exception ex) when (_updateConnectionInfoCts.IsCancellationRequested) {
            // it is a dead request from previous session
            VhLogger.Instance.LogWarning(ex, "Previous UpdateConnection Info has been ignored due to the new service.");

        }
        catch (Exception) when (_isInitializing) {
            throw; // initializing state, discard any log or setting new connection info
        }
        catch (VpnServiceNotReadyException ex) {
            if (_connectionInfo.ClientState != ClientState.Disposed) {
                VhLogger.Instance.LogDebug(ex, "Could not update connection info.");
                _connectionInfo = SetConnectionInfo(ClientState.Disposed, _connectionInfo.Error?.ToException());
            }
        }
        catch (VpnServiceUnreachableException ex) {
            // increment the count to stop the service if it is unreachable for too long
            _vpnServiceUnreachableCount++;

            // update connection info and set error
            if (_vpnServiceUnreachableCount == VpnServiceUnreachableThreshold) { }
            _connectionInfo = SetConnectionInfo(ClientState.Disposed, ex);

            // report it first time
            if (_vpnServiceUnreachableCount == 1)
                VhLogger.Instance.LogError(ex, "Could not update connection info.");
        }
        catch (Exception ex) {
            _vpnServiceUnreachableCount = 0; // reset the count if it is not VpnServiceUnreachableException
            VhLogger.Instance.LogError(ex, "Could not update connection info.");
        }

        CheckForEvents();
        _connectionInfoRefreshedTime = FastDateTime.Now;
        return _connectionInfo;
    }


    private Task SendRequest(IApiRequest request, CancellationToken cancellationToken)
    {
        return SendRequest<object>(request, cancellationToken);
    }

    private async Task<T?> SendRequest<T>(IApiRequest request, CancellationToken cancellationToken)
    {
        var response = await SendRequestCore<T>(request, cancellationToken).Vhc();

        // update the last connection info
        if (response.ConnectionInfo.CreatedTime >= _connectionInfo.CreatedTime) {
            _connectionInfo = response.ConnectionInfo;
            _connectionInfoRefreshedTime = FastDateTime.Now;
        }

        // convert to error. 
        if (response.ApiError != null)
            throw ClientExceptionConverter.ApiErrorToException(response.ApiError);

        return response.Result;
    }

    private readonly AsyncLock _sendRequestLock = new();
    private async Task<ApiResponse<T>> SendRequestCore<T>(IApiRequest request, CancellationToken cancellationToken)
    {
        // we have only one TcpClient, so we need to lock it
        // VpnService should not have long-running requests, so it is ok to lock it
        using var scopeLock = await _sendRequestLock.LockAsync(cancellationToken).Vhc();

        var tcpClient = _tcpClient; // it may be reset to null while working
        if (tcpClient != null) {
            try {
                // send request
                await StreamUtils.WriteObjectAsync(tcpClient.GetStream(), request.GetType().Name, cancellationToken).Vhc();
                await StreamUtils.WriteObjectAsync(tcpClient.GetStream(), request, cancellationToken).Vhc();
                return await StreamUtils.ReadObjectAsync<ApiResponse<T>>(tcpClient.GetStream(), cancellationToken).Vhc();
            }
            catch (Exception ex) {
                VhLogger.Instance.LogDebug(ex, "Could not send request to VpnService Host. EndPoint: {EndPoint}",
                    tcpClient.Client.LocalEndPoint);
                tcpClient.Dispose();
                _tcpClient = null;
            }
        }

        // validate connection info
        var connectionInfo = _connectionInfo;
        if (connectionInfo.Error != null) throw new VpnServiceNotReadyException("VpnService is not ready.");
        if (connectionInfo.ApiEndPoint == null) throw new VpnServiceNotReadyException("ApiEndPoint is not available.");
        if (connectionInfo.ApiKey == null) throw new VpnServiceNotReadyException("ApiKey is not available.");
        _tcpClient = await ConnectToVpnService(connectionInfo.ApiEndPoint, connectionInfo.ApiKey, cancellationToken).Vhc();

        // send request
        try {
            await StreamUtils.WriteObjectAsync(_tcpClient.GetStream(), request.GetType().Name, cancellationToken).Vhc();
            await StreamUtils.WriteObjectAsync(_tcpClient.GetStream(), request, cancellationToken).Vhc();
            return await StreamUtils.ReadObjectAsync<ApiResponse<T>>(_tcpClient.GetStream(), cancellationToken).Vhc();
        }
        catch (Exception ex) {
            _tcpClient.Dispose();
            _tcpClient = null;
            throw new VpnServiceUnreachableException($"VpnService is unreachable. EndPoint: {connectionInfo.ApiEndPoint}", ex);
        }
    }

    private static async Task<TcpClient> ConnectToVpnService(IPEndPoint apiEndPoint, byte[] apiKey,
        CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogDebug("Connecting to VpnService Host... EndPoint: {EndPoint}", apiEndPoint);
        var tcpClient = new TcpClient();
        try {
            await tcpClient.ConnectAsync(apiEndPoint, cancellationToken).Vhc();
            await StreamUtils.WriteObjectAsync(tcpClient.GetStream(), apiKey, cancellationToken).Vhc();
            VhLogger.Instance.LogDebug("Connected to VpnService Host."); return tcpClient;
        }
        catch (Exception ex) {
            tcpClient.Dispose();
            throw new VpnServiceUnreachableException($"VpnService is unreachable. EndPoint: {apiEndPoint}", ex);
        }
    }

    /// <summary>
    /// Stop the VPN service and disconnect from the server if running. This method is idempotent.
    /// No exception will be thrown
    /// </summary>
    private readonly AsyncLock _stopLock = new();
    public async Task<bool> TryStop(TimeSpan? timeout = null)
    {
        // Initialize cll this method, so it must be finished to make sure the service is not interrupted
        using var scopeLock = await _stopLock.LockAsync().Vhc();

        // stop the service
        if (!ConnectionInfo.IsStarted())
            return true;

        using var stopTimeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(5));

        // send disconnect request
        try {
            VhLogger.Instance.LogDebug("Sending disconnect request...");
            await SendRequest(new ApiDisconnectRequest(), stopTimeoutCts.Token).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not send disconnect request. ClientState: {ClientState}",
                ConnectionInfo.ClientState);
        }

        // wait for the service to stop
        VhLogger.Instance.LogDebug("Waiting for VpnService to stop.");
        stopTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        try {
            while (ConnectionInfo.IsStarted()) {
                await RefreshConnectionInfo(true, stopTimeoutCts.Token).Vhc();
                await Task.Delay(200, stopTimeoutCts.Token).Vhc();
            }

            VhLogger.Instance.LogDebug("VpnService has been stopped.");
            return true;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not stop the VpnService.");
            return false;
        }
    }

    private void CheckForEvents()
    {
        var connectionInfo = _connectionInfo;
        // check if the state has changed
        if (_lastConnectionInfo?.ClientState != connectionInfo.ClientState) {
            VhLogger.Instance.LogDebug("The VpnService state has been changed. {OldSate} => {NewState}",
                _lastConnectionInfo?.ClientState, connectionInfo.ClientState);

            Task.Run(() => StateChanged?.Invoke(this, EventArgs.Empty), CancellationToken.None);
        }

        _lastConnectionInfo = connectionInfo;
    }

    public Task Reconfigure(ClientReconfigureParams reconfigureParams, CancellationToken cancellationToken)
    {
        return IsStarted
            ? SendRequest(new ApiReconfigureRequest { Params = reconfigureParams }, cancellationToken)
            : Task.CompletedTask;
    }

    public Task SetWaitForAd(CancellationToken cancellationToken)
    {
        return SendRequest(new ApiSetWaitForAdRequest(), cancellationToken);
    }

    public Task SetAdResult(AdResult adResult, bool isRewarded, CancellationToken cancellationToken)
    {
        return SendRequest(
            new ApiSetAdResultRequest { AdResult = adResult, ApiError = null, IsRewarded = isRewarded },
            cancellationToken);
    }

    public Task SetAdResult(Exception ex, CancellationToken cancellationToken)
    {
        return SendRequest(new ApiSetAdResultRequest {
            AdResult = null,
            IsRewarded = false,
            ApiError = ex.ToApiError()
        }, cancellationToken);
    }

    private async ValueTask UpdateConnectionInfoJob(CancellationToken cancellationToken)
    {
        await RefreshConnectionInfo(false, cancellationToken).Vhc();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        // do not stop the service, lets service keep running until user explicitly stop it
        _updateConnectionInfoJob.Dispose();
        _updateConnectionInfoCts.TryCancel();
        _updateConnectionInfoCts.Dispose();
        _tcpClient?.Dispose();
    }
}