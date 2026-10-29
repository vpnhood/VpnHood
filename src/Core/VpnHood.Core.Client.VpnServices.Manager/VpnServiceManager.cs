using System.Text.Json;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions;
using VpnHood.Core.Client.Devices.Abstractions.UiContexts;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Proxies.Management.Abstractions.Options;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Streams;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Client.VpnServices.Manager;

public class VpnServiceManager : IDisposable
{
    private const int VpnServiceUnreachableThreshold = 1; // after this count we stop the service
    private readonly TimeSpan _requestVpnServiceTimeout = TimeSpan.FromSeconds(120).WhenNoDebugger();
    private readonly TimeSpan _startVpnServiceTimeout = TimeSpan.FromSeconds(20).WhenNoDebugger();
    private readonly TimeSpan _startVpnServicePollInterval = TimeSpan.FromMilliseconds(200);

    // the service's Connected shows this late at most; a second hid a faster start
    private readonly TimeSpan _waitForConnectionPollInterval = TimeSpan.FromMilliseconds(200);
    private bool _disposed;

    private readonly TimeSpan _connectionInfoTimeSpan = TimeSpan.FromSeconds(1);
    private readonly IDevice _device;
    private readonly VpnServiceOptionsFile _serviceOptionsFile;
    private readonly VpnServiceStatus _status;
    private DateTime? _connectionInfoRefreshedTime;
    private readonly IMessageClient _messageClient;
    private readonly VpnServiceStartState _startState;
    private int _vpnServiceUnreachableCount;
    private CancellationTokenSource _updateConnectionInfoCts = new();
    private ConnectionInfo? _lastConnectionInfo;
    private readonly Job _updateConnectionInfoJob;

    public event EventHandler? StateChanged;
    public event EventHandler<ClientReconfigureParams>? Reconfigured;
    public string LogFilePath => Path.Combine(_device.VpnServiceConfigFolder, ClientOptions.VpnLogFileName);

    public VpnServiceManager(IDevice device, TimeSpan? eventWatcherInterval)
    {
        Directory.CreateDirectory(device.VpnServiceConfigFolder);
#pragma warning disable CS0618 // Type or member is obsolete
        VpnServiceFolders.MigrateServiceFiles(device.VpnServiceDataFolder, device.VpnServiceConfigFolder);
#pragma warning restore CS0618 // Type or member is obsolete
        _serviceOptionsFile = new VpnServiceOptionsFile(device.VpnServiceConfigFolder);
        _device = device;
        _messageClient = device.CreateMessageClient();
        _status = new VpnServiceStatus(device.VpnServiceConfigFolder);
        _startState = new VpnServiceStartState(_status);

        _updateConnectionInfoJob = new Job(UpdateConnectionInfoJob,
            eventWatcherInterval ?? TimeSpan.MaxValue, nameof(UpdateConnectionInfoJob));
    }

    public ConnectionInfo ConnectionInfo {
        get {
            _ = TryRefreshConnectionInfo(false, CancellationToken.None);
            return _status.ConnectionInfo;
        }
    }

    public bool IsStarted => _startState.IsInitializing || ConnectionInfo.IsStarted();

    /// <param name="uiContext">The caller's window, which asks the person for the VPN permission when
    /// the OS needs that (Android); null when it has none, and then a missing permission fails.</param>
    public async Task Start(IUiContext? uiContext, VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        // before the start: the stop waits for an older start's Disposed, which it writes only while it owns the state
        if (IsStarted)
            await TryStop().Vhc();

        await InitializeVpnService(uiContext, serviceOptions, cancellationToken).Vhc();

        // wait for connection or error
        await WaitForConnection(cancellationToken).Vhc();
    }

    // The start owns the state until the service has written its own, or the start has failed
    private async Task InitializeVpnService(IUiContext? uiContext, VpnServiceOptions serviceOptions,
        CancellationToken cancellationToken)
    {
        var clientOptions = serviceOptions.ClientOptions;

        using var startScope = _startState.Begin(cancellationToken);
        try {
            _vpnServiceUnreachableCount = 0;
            await _updateConnectionInfoCts.TryCancelAsync().Vhc();
            _updateConnectionInfoCts.Dispose();
            _updateConnectionInfoCts = new CancellationTokenSource();

            // save vpn config
            await _serviceOptionsFile.Write(serviceOptions, cancellationToken).Vhc();

            // prepare vpn service
            VhLogger.Instance.LogInformation("Requesting VpnService...");
            if (!clientOptions.UseNullCapture)
                await _device.RequestVpnService(uiContext, _requestVpnServiceTimeout,
                    cancellationToken).Vhc();

            // start vpn service
            VhLogger.Instance.LogInformation("Starting VpnService...");
            VhUtils.TryDeleteFile(LogFilePath); // remove previous service log
            await _device.StartVpnService(cancellationToken).Vhc();

            // wait for vpn service to start
            await WaitForVpnService(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            startScope.Fail(ex);
            throw;
        }
    }

    /// <summary>
    /// True when a previous session has left a configuration the VPN service can start from.
    /// </summary>
    public bool IsConfigured => _serviceOptionsFile.Exists;

    // No server to start with: a start without the app then fails, as before the first connect, rather
    // than reach a server the app no longer has.
    public Task ClearConfiguration(CancellationToken cancellationToken)
    {
        return _serviceOptionsFile.Delete(cancellationToken);
    }

    /// <summary>
    /// Start the VPN service from its last persisted configuration without touching it — the same
    /// bootstrap always-on uses. Check IsConfigured before calling.
    /// </summary>
    /// <param name="uiContext">The caller's window, as Start takes it.</param>
    public Task StartFromLastConfig(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // load the last configuration and start the service
        var serviceOptions = _serviceOptionsFile.TryRead(cancellationToken);
        return serviceOptions != null 
            ? Start(uiContext, serviceOptions, cancellationToken)
            : throw new InvalidOperationException("There is no previous VPN configuration to start from.");
    }


    private async Task WaitForVpnService(CancellationToken cancellationToken)
    {
        VhLogger.Instance.LogInformation("Waiting for VpnService to start...");
        using var timeoutCts = new CancellationTokenSource(_startVpnServiceTimeout);

        try {
            using var localCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, timeoutCts.Token);

            // directly read file because UpdateConnection will set the state to disposed if it could not connect to the service
            // UpdateConnection will fail due to it cancellation 
            var connectionInfo = _status.TryRead();

            // wait for vpn service to start
            while (connectionInfo == null ||
                   connectionInfo.ClientState is ClientState.None or ClientState.Initializing) {
                await Task.Delay(_startVpnServicePollInterval, localCts.Token).Vhc();
                connectionInfo = _status.TryRead();
            }

            _status.ConnectionInfo = connectionInfo;
            VhLogger.Instance.LogInformation(
                "VpnService has started. ConnectionState: {ConnectionState}",
                connectionInfo.ClientState);
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
            // refresh and wait for the result; the ConnectionInfo property refreshes in fire-and-forget
            // fashion and may return a stale state for an extra poll cycle
            var connectionInfo = await TryRefreshConnectionInfo(force: true, cancellationToken).Vhc();

            // check for error
            if (connectionInfo.Error != null)
                throw ClientExceptionConverter.ApiErrorToException(connectionInfo.Error) ??
                      connectionInfo.Error.ToException();

            // the service ended with no error: the service's own end, as one after the connect is; the app
            // may have shown Connected before this poll saw it
            if (connectionInfo.ClientState is ClientState.Disposed or ClientState.Disconnecting)
                throw new VpnServiceException("VpnService could not establish any connection.");

            if (connectionInfo.ClientState == ClientState.Connected)
                break;

            // Throw immediately if cancellation is already requested
            cancellationToken.ThrowIfCancellationRequested();

            // wait for a while before checking the state again
            try {
                await Task.Delay(_waitForConnectionPollInterval, cancellationToken).Vhc();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) {
                // continue to check the state again as ThrowIfCancellationRequested will throw after checking the state
                // It helps test with short timeout process the connection established event
            }
        }

        VhLogger.Instance.LogDebug("The VpnService has established a connection.");
    }

    public Task RefreshState(CancellationToken cancellationToken) =>
        TryRefreshConnectionInfo(true, cancellationToken);

    private async Task<ConnectionInfo> TryRefreshConnectionInfo(bool force, CancellationToken cancellationToken)
    {
        try {
            return await RefreshConnectionInfo(force, cancellationToken).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not update connection info.");
            return _status.ConnectionInfo;
        }
    }

    private readonly AsyncLock _connectionInfoLock = new();

    private async Task<ConnectionInfo> RefreshConnectionInfo(bool force, CancellationToken cancellationToken)
    {
        if (_disposed)
            return _status.ConnectionInfo;

        // if it is locked, we are already updating the connection info
        if (!force && _connectionInfoLock.IsLocked)
            return _status.ConnectionInfo;

        // build a new token source to cancel the previous request
        using var updateCts =
            CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _updateConnectionInfoCts.Token);
        using var scopeLock = await _connectionInfoLock.LockAsync(updateCts.Token).Vhc();

        // read from cache if not expired
        if (_startState.IsInitializing ||
            (!force && FastDateTime.UtcNow - _connectionInfoRefreshedTime < _connectionInfoTimeSpan))
            return _status.ConnectionInfo;

        // update from file to make sure there is no error
        // VpnClient always update the file when ConnectionState changes
        // Should send request if service is in initializing state, because SendRequest will set the state to disposed if failed
        _status.ConnectionInfo = _status.TryRead() ?? _status.ConnectionInfo;
        _connectionInfoRefreshedTime = FastDateTime.UtcNow;
        if (_startState.IsInitializing || _status.ConnectionInfo.Error != null || !_status.ConnectionInfo.IsStarted()) {
            CheckForEvents();
            return _status.ConnectionInfo;
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
        catch (Exception) when (_startState.IsInitializing) {
            throw; // initializing state, discard any log or setting new connection info
        }
        catch (VpnServiceNotReadyException ex) {
            if (_status.ConnectionInfo.ClientState != ClientState.Disposed) {
                VhLogger.Instance.LogDebug(ex, "Could not update connection info.");
                _status.Set(ClientState.Disposed, _status.ConnectionInfo.Error?.ToException());
            }
        }
        catch (VpnServiceUnreachableException) when (_stopLock.IsLocked) {
            // service is stopping, set the state to none
            _status.Set(ClientState.None);
        }
        catch (VpnServiceUnreachableException ex) {
            VhLogger.Instance.LogError(ex, "VpnService is unreachable.");

            // increment the count to stop the service if it is unreachable for too long
            _vpnServiceUnreachableCount++;

            // update connection info and set error
            if (_vpnServiceUnreachableCount == VpnServiceUnreachableThreshold)
                _status.Set(ClientState.Disposed, ex);

            // report it first time
            if (_vpnServiceUnreachableCount == 1)
                VhLogger.Instance.LogError(ex, "Could not update connection info.");
        }
        catch (Exception ex) {
            _vpnServiceUnreachableCount = 0; // reset the count if it is not VpnServiceUnreachableException
            VhLogger.Instance.LogError(ex, "Could not update connection info.");
        }

        CheckForEvents();
        _connectionInfoRefreshedTime = FastDateTime.UtcNow;
        return _status.ConnectionInfo;
    }


    private Task SendRequest(IApiRequest request, CancellationToken cancellationToken)
    {
        return SendRequest<object>(request, cancellationToken);
    }

    private async Task<T?> SendRequest<T>(IApiRequest request, CancellationToken cancellationToken)
    {
        var response = await SendRequestCore<T>(request, cancellationToken).Vhc();

        // update the last connection info
        if (response.ConnectionInfo.CreatedTime >= _status.ConnectionInfo.CreatedTime) {
            _status.ConnectionInfo = response.ConnectionInfo;
            _connectionInfoRefreshedTime = FastDateTime.UtcNow;
        }

        // convert to error. 
        if (response.ApiError != null) {
            throw ClientExceptionConverter.ApiErrorToException(response.ApiError) ?? response.ApiError.ToException();
        }

        return response.Result;
    }

    private readonly AsyncLock _sendRequestLock = new();

    private async Task<ApiResponse<T>> SendRequestCore<T>(IApiRequest request, CancellationToken cancellationToken)
    {
        // we have only one message client, so we need to lock it
        // VpnService should not have long-running requests, so it is ok to lock it
        using var scopeLock = await _sendRequestLock.LockAsync(cancellationToken).Vhc();

        // validate connection info
        var connectionInfo = _status.ConnectionInfo;
        if (connectionInfo.Error != null)
            throw new VpnServiceNotReadyException("VpnService is not ready.");

        // build the request blob: [len][requestType][len][requestBody] (StreamUtils framing)
        using var requestStream = new MemoryStream();
        await StreamUtils.WriteObjectAsync(requestStream, request.GetType().Name, cancellationToken).Vhc();
        await StreamUtils.WriteObjectAsync(requestStream, request, cancellationToken).Vhc();

        // send via the transport; connection reuse and reconnection are handled by the message client
        var responseBuffer = await _messageClient.SendAsync(requestStream.ToArray(), cancellationToken).Vhc();

        // the response is raw ApiResponse JSON bytes (no framing)
        return JsonSerializer.Deserialize<ApiResponse<T>>(responseBuffer.Span)
               ?? throw new VpnServiceUnreachableException("VpnService returned an empty response.");
    }

    /// <summary>
    /// Stop the VPN service and disconnect from the server if running. This method is idempotent.
    /// No exception will be thrown
    /// </summary>

    public bool IsStopping => _stopLock.IsLocked;
    private readonly AsyncLock _stopLock = new();
    public async Task<bool> TryStop(TimeSpan? timeout = null)
    {
        // Initialize cll this method, so it must be finished to make sure the service is not interrupted
        using var scopeLock = await _stopLock.LockAsync(CancellationToken.None).Vhc();

        // stop the service. Disconnecting does NOT count as stopped: the client is still closing its
        // session and releasing its resources (filter dbs, adapter) until it reaches a terminal state
        if (ConnectionInfo.ClientState is ClientState.None or ClientState.Disposed)
            return true;

        using var stopTimeoutCts = new CancellationTokenSource(timeout ?? TimeSpan.FromSeconds(5));

        // send disconnect request
        try {
            VhLogger.Instance.LogDebug("Sending disconnect request...");
            await SendRequest(new ApiDisconnectRequest(), stopTimeoutCts.Token).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogDebug(ex, "Could not send disconnect request. ClientState: {ClientState}",
                ConnectionInfo.ClientState);
        }

        // wait for the service to reach a terminal state; the VpnServiceHost publishes Disposed after the
        // client has fully released its resources. Returning on Disconnecting would let the next connect
        // race the teardown (e.g. rebuild a split-ip db still held open by the disposing client).
        VhLogger.Instance.LogDebug("Waiting for VpnService to stop.");
        stopTimeoutCts.CancelAfter(TimeSpan.FromSeconds(5));
        try {
            while (ConnectionInfo.ClientState is not (ClientState.None or ClientState.Disposed)) {
                // the manager's close, which the app's quick close may run while its graceful one waits here,
                // ends the wait: nothing refreshes the state after it
                ObjectDisposedException.ThrowIf(_disposed, this);
                await RefreshConnectionInfo(true, stopTimeoutCts.Token).Vhc();
                await Task.Delay(200, stopTimeoutCts.Token).Vhc();
            }

            VhLogger.Instance.LogDebug("VpnService has been stopped.");
            return true;
        }
        catch (Exception ex) when (_disposed) {
            VhLogger.Instance.LogDebug(ex, "Stopped waiting for the VpnService: its manager has closed.");
            return false;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not stop the VpnService.");
            return false;
        }
    }

    private void CheckForEvents()
    {
        var connectionInfo = _status.ConnectionInfo;
        
        // check if the state has changed
        if (_lastConnectionInfo?.ClientState != connectionInfo.ClientState || 
            _lastConnectionInfo.SessionStatus?.IsAdapterStarted != connectionInfo.SessionStatus?.IsAdapterStarted) {
            VhLogger.Instance.LogDebug("The VpnService state has been changed. {OldSate} => {NewState}",
                _lastConnectionInfo?.ClientState, connectionInfo.ClientState);

            Task.Run(() => StateChanged?.Invoke(this, EventArgs.Empty), CancellationToken.None);
        }

        _lastConnectionInfo = connectionInfo;
    }

    public bool IsReconfiguring { get; private set; }

    // The saved options, connected or not, which a later start without the app runs with; then a
    // running session, at once, with the part it takes live.
    public async Task Reconfigure(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        await _serviceOptionsFile.Update(serviceOptions, cancellationToken).Vhc();
        if (!IsStarted)
            return;

        var clientOptions = serviceOptions.ClientOptions;
        var reconfigureParams = new ClientReconfigureParams {
            DropQuic = clientOptions.DropQuic,
            DropUdp = clientOptions.DropUdp,
            UseTcpProxy = clientOptions.UseTcpProxy,
            AllowAnonymousTracker = clientOptions.AllowAnonymousTracker,
            ChannelProtocol = clientOptions.ChannelProtocol,
            UnroutedIpMode = clientOptions.UnroutedIpMode,
            UnsupportedIpV6Mode = clientOptions.UnsupportedIpV6Mode,
            ProxyOptions = serviceOptions.ProxyOptions ?? new ProxyOptions()
        };

        IsReconfiguring = true;
        try {
            await SendRequest(new ApiReconfigureRequest { Params = reconfigureParams }, cancellationToken).Vhc();
            Reconfigured?.Invoke(this, reconfigureParams);
        }
        finally {
            IsReconfiguring = false;
        }
    }

    public Task SetWaitForAd(CancellationToken cancellationToken)
    {
        return SendRequest(new ApiSetWaitForAdRequest(), cancellationToken);
    }

    public Task SetAdOk(AdResult adResult, bool isRewarded, CancellationToken cancellationToken)
    {
        return SendRequest(
            new ApiSetAdOkRequest {
                AdResult = adResult,
                IsRewarded = isRewarded
            },
            cancellationToken);
    }

    public Task SetAdFailed(Exception ex, CancellationToken cancellationToken)
    {
        return SendRequest(new ApiAdFailedRequest {
            ApiError = ex.ToApiError()
        }, cancellationToken);
    }

    private async ValueTask UpdateConnectionInfoJob(CancellationToken cancellationToken)
    {
        await RefreshConnectionInfo(false, cancellationToken).Vhc();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // do not stop the service, lets service keep running until user explicitly stop it
        _updateConnectionInfoJob.Dispose();
        _updateConnectionInfoCts.TryCancel();
        _updateConnectionInfoCts.Dispose();
        _messageClient.Dispose();
    }
}