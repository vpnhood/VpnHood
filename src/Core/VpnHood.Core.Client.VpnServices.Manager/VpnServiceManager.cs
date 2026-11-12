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

/// <summary>
/// Manages the VPN service from the app's process: starts it with its options, follows its state, and sends it the
/// app's requests.
/// </summary>
/// <remarks>
/// The service runs in a process of its own, as the platform's VPN extension, or, in a head that runs both halves, in
/// the app's own process; it reports its state in a status file in its config folder and in each answer.
/// </remarks>
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

    /// <summary>
    /// Occurs when the connection's state, or whether its adapter has started, changes; raised on the thread pool.
    /// </summary>
    public event EventHandler? StateChanged;

    /// <summary>Occurs once a started service took a reconfigure, with its settings.</summary>
    public event EventHandler<ClientReconfigureParams>? Reconfigured;

    /// <summary>Gets the path of the VPN service's log, in its config folder; each start deletes the last one.</summary>
    public string LogFilePath => Path.Combine(_device.VpnServiceConfigFolder, ClientOptions.VpnLogFileName);

    /// <summary>
    /// Initializes a new instance of the <see cref="VpnServiceManager"/> class for a device's VPN service: it makes the
    /// service's config folder, moves the service's old files there, and reads the last status.
    /// </summary>
    /// <param name="device">The device, whose VPN service this manages.</param>
    /// <param name="eventWatcherInterval">
    /// How often to refresh the connection's info in the background, for <see cref="StateChanged"/>, or
    /// <see langword="null"/> for no refresh after the first, which runs at the job runner's next tick.
    /// </param>
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

    /// <summary>
    /// Gets the connection's info as last known; reading it starts a refresh in the background, at most one a second.
    /// </summary>
    public ConnectionInfo ConnectionInfo {
        get {
            _ = TryRefreshConnectionInfo(false, CancellationToken.None);
            return _status.ConnectionInfo;
        }
    }

    /// <summary>
    /// Gets a value indicating whether a start is under way or the connection is started
    /// (<see cref="VpnHood.Core.Client.VpnServices.Abstractions.ConnectionInfo.IsStarted"/>).
    /// </summary>
    public bool IsStarted => _startState.IsInitializing || ConnectionInfo.IsStarted();

    /// <summary>
    /// Starts the VPN service with options, after it tries to stop one already started, and waits until it is
    /// connected.
    /// </summary>
    /// <param name="uiContext">
    /// The caller's window, which asks the person for the VPN permission where the OS needs that (Android), or
    /// <see langword="null"/> where it has none, and then a missing permission fails.
    /// </param>
    /// <param name="serviceOptions">The options, which it saves for the service to read.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the service is connected.</returns>
    /// <remarks>
    /// The OS's permission may take 2 minutes; once the device has started the service, the service has 20 seconds to
    /// say it is up; the connect itself has no time limit here. Under a debugger, both limits become the debugger's
    /// timeout, a day by default. With <see cref="ClientOptions.UseNullCapture"/>, no permission is asked.
    /// </remarks>
    /// <exception cref="VpnServiceTimeoutException">The service did not say it is up in 20 seconds.</exception>
    /// <exception cref="VpnServiceException">The service ended without a connection, and without an error.</exception>
    /// <exception cref="IOException">
    /// The options could not be saved, as where the service still holds the file after 2 seconds.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="Exception">
    /// The connect failed: the service's error, as <see cref="ClientExceptionConverter"/> makes it; or the device's,
    /// where it could not ask for the permission or start the service, as Android's <see cref="TimeoutException"/> for a
    /// prompt not answered in time, or whose wait was canceled.
    /// </exception>
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
    /// Gets a value indicating whether a connect has left options the VPN service can start from.
    /// </summary>
    public bool IsConfigured => _serviceOptionsFile.Exists;

    /// <summary>
    /// Deletes the saved options, so a start without the app fails, as before the first connect, rather than reach a
    /// server the app no longer has.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the options are gone.</returns>
    /// <exception cref="IOException">
    /// The file could not be reached, as where the service still holds it after 2 seconds.
    /// </exception>
    /// <exception cref="UnauthorizedAccessException">The file may not be deleted.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task ClearConfiguration(CancellationToken cancellationToken)
    {
        return _serviceOptionsFile.Delete(cancellationToken);
    }

    /// <summary>
    /// Starts the VPN service with its saved options, unchanged, as always-on does, and waits until it is connected.
    /// </summary>
    /// <param name="uiContext">The caller's window, as <see cref="Start"/> takes it.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the service is connected.</returns>
    /// <remarks>Check <see cref="IsConfigured"/> first; the rest is as <see cref="Start"/>.</remarks>
    /// <exception cref="InvalidOperationException">There are no saved options, or they cannot be read.</exception>
    /// <exception cref="ObjectDisposedException">The manager is disposed.</exception>
    /// <exception cref="VpnServiceTimeoutException">The service did not say it is up in 20 seconds.</exception>
    /// <exception cref="VpnServiceException">The service ended without a connection, and without an error.</exception>
    /// <exception cref="IOException">
    /// The options could not be saved again, as where the service still holds the file after 2 seconds.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    /// <exception cref="Exception">
    /// The connect failed: the service's error, as <see cref="ClientExceptionConverter"/> makes it; or the device's,
    /// where it could not ask for the permission or start the service.
    /// </exception>
    public async Task StartFromLastConfig(IUiContext? uiContext, CancellationToken cancellationToken)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);

        // load the last configuration and start the service
        var serviceOptions = await _serviceOptionsFile.TryRead(cancellationToken).Vhc() ??
                             throw new InvalidOperationException("There is no previous VPN configuration to start from.");
        await Start(uiContext, serviceOptions, cancellationToken).Vhc();
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

    /// <summary>
    /// Refreshes the connection's info now: from the service's status file, and from its answer while it is started.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>
    /// A task that completes once the info is refreshed. Where the service cannot be reached, its info becomes
    /// <see cref="ClientState.Disposed"/>, or <see cref="ClientState.None"/> while a stop runs; any other failure is
    /// logged, and the last info stays.
    /// </returns>
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
        if (!force && FastDateTime.UtcNow - _connectionInfoRefreshedTime < _connectionInfoTimeSpan)
            return _status.ConnectionInfo;

        // A start owns the state until the service has written its own, which the service may do long before the start
        // ends: Windows' VPN plug-in dial returns only once the channel is up, its session finding and connecting to the
        // server meanwhile. The start wrote Initializing to the file, so a state of a service at work there is this
        // service's; a Disposed may be an older one's, should the start's write have failed, and is left to the start's
        // end. No request: a failed one would end the start's state.
        if (_startState.IsInitializing) {
            if (_status.TryRead() is { ClientState: not (ClientState.None or ClientState.Initializing or ClientState.Disposed) } serviceInfo) {
                _status.ConnectionInfo = serviceInfo;
                CheckForEvents();
            }

            _connectionInfoRefreshedTime = FastDateTime.UtcNow;
            return _status.ConnectionInfo;
        }

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

    /// <summary>Gets a value indicating whether a stop is under way.</summary>
    public bool IsStopping => _stopLock.IsLocked;
    private readonly AsyncLock _stopLock = new();

    /// <summary>
    /// Asks the VPN service to disconnect, if it is started, and waits until it says it has ended, which it does once it
    /// has let go of its resources, or can no longer be reached, which counts as its end.
    /// </summary>
    /// <param name="timeout">
    /// How long the request to disconnect may take, or <see langword="null"/> for 5 seconds; the wait for the end
    /// takes up to 5 seconds more.
    /// </param>
    /// <returns>
    /// A task whose result is <see langword="true"/> if the service has ended, or had, by its word or as it can no longer
    /// be reached; otherwise, <see langword="false"/>, which it logs.
    /// </returns>
    /// <remarks>
    /// It throws only for a bad timeout, and a second call while one runs waits for it. The wait's 5 seconds start once
    /// the request has ended, but where the request used up its whole time, the wait ends at once.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="timeout"/> is negative, other than infinite, or too long.
    /// </exception>
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

    /// <summary>Gets a value indicating whether a reconfigure is being sent to the service.</summary>
    public bool IsReconfiguring { get; private set; }

    /// <summary>
    /// Saves options made again from the app's settings, connected or not, for a later start without the app, where a
    /// connect has saved options before (<see cref="IsConfigured"/>); then, where the service is started, sends it at
    /// once the part a session takes live.
    /// </summary>
    /// <param name="serviceOptions">
    /// The options; the saved plan and always-on mark are kept (<see cref="VpnServiceOptionsFile.Update"/>).
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the service took them, or they are saved where it is not started.</returns>
    /// <remarks>
    /// A start that is still bringing the service up takes them from the saved options, so a request that cannot reach
    /// the service then is no error.
    /// </remarks>
    /// <exception cref="Exception">The service refused them: its error, as <see cref="ClientExceptionConverter"/> makes it.</exception>
    /// <exception cref="VpnServiceNotReadyException">The service cannot take requests.</exception>
    /// <exception cref="VpnServiceUnreachableException">The service cannot be reached.</exception>
    /// <exception cref="IOException">
    /// The saved options could not be read or written, as where the service still holds the file after 2 seconds.
    /// </exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public async Task Reconfigure(VpnServiceOptions serviceOptions, CancellationToken cancellationToken)
    {
        await _serviceOptionsFile.Update(serviceOptions, cancellationToken).Vhc();
        if (!IsStarted)
            return;

        var clientOptions = serviceOptions.ClientOptions;
        var reconfigureParams = new ClientReconfigureParams {
            DropQuic = clientOptions.DropQuic,
            DropUdp = clientOptions.DropUdp,
            UseTcpForwarding = clientOptions.UseTcpForwarding,
            AllowAnonymousTracker = clientOptions.AllowAnonymousTracker,
            ChannelProtocol = clientOptions.ChannelProtocol,
            UnroutedIpMode = clientOptions.UnroutedIpMode,
            UnsupportedIpV6Mode = clientOptions.UnsupportedIpV6Mode,
            UseKillSwitch = clientOptions.UseKillSwitch,
            ProxyOptions = serviceOptions.ProxyOptions ?? new ProxyOptions()
        };

        IsReconfiguring = true;
        try {
            await SendRequest(new ApiReconfigureRequest { Params = reconfigureParams }, cancellationToken).Vhc();
            Reconfigured?.Invoke(this, reconfigureParams);
        }
        catch (Exception ex) when (_startState.IsInitializing &&
                                   ex is VpnServiceNotReadyException or VpnServiceUnreachableException) {
            // A start still bringing its service up, whose channel is not open yet: no file, or an ended
            // service's, which a VPN host its service's job ended leaves (Windows). The service reads its
            // options after it opens the channel, so it starts with these, saved above.
            VhLogger.Instance.LogDebug(ex, "The VPN service starts with the reconfigure: its channel is not up yet.");
        }
        finally {
            IsReconfiguring = false;
        }
    }

    /// <summary>
    /// Tells the VPN service the app is about to show the ad the session requires, so the session waits for it.
    /// </summary>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the service took it.</returns>
    /// <exception cref="Exception">The service refused it: its error, as <see cref="ClientExceptionConverter"/> makes it.</exception>
    /// <exception cref="VpnServiceNotReadyException">The service cannot take requests.</exception>
    /// <exception cref="VpnServiceUnreachableException">The service cannot be reached.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task SetWaitForAd(CancellationToken cancellationToken)
    {
        return SendRequest(new ApiSetWaitForAdRequest(), cancellationToken);
    }

    /// <summary>Tells the VPN service the ad the session waits for was shown.</summary>
    /// <param name="adResult">The ad's result.</param>
    /// <param name="isRewarded">
    /// <see langword="true"/> if it was a rewarded ad, whose data the service sends the server where it has any.
    /// </param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the service took it.</returns>
    /// <exception cref="Exception">The service refused it: its error, as <see cref="ClientExceptionConverter"/> makes it.</exception>
    /// <exception cref="VpnServiceNotReadyException">The service cannot take requests.</exception>
    /// <exception cref="VpnServiceUnreachableException">The service cannot be reached.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
    public Task SetAdOk(AdResult adResult, bool isRewarded, CancellationToken cancellationToken)
    {
        return SendRequest(
            new ApiSetAdOkRequest {
                AdResult = adResult,
                IsRewarded = isRewarded
            },
            cancellationToken);
    }

    /// <summary>Tells the VPN service the ad the session waits for could not be shown.</summary>
    /// <param name="ex">The error the ad failed with.</param>
    /// <param name="cancellationToken">The token to monitor for cancellation requests.</param>
    /// <returns>A task that completes once the service took it.</returns>
    /// <exception cref="Exception">The service refused it: its error, as <see cref="ClientExceptionConverter"/> makes it.</exception>
    /// <exception cref="VpnServiceNotReadyException">The service cannot take requests.</exception>
    /// <exception cref="VpnServiceUnreachableException">The service cannot be reached.</exception>
    /// <exception cref="OperationCanceledException"><paramref name="cancellationToken"/> was canceled.</exception>
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

    /// <summary>Stops following the VPN service, which goes on running until it is stopped.</summary>
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