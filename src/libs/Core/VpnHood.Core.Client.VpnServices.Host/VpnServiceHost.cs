using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Host.Sessions;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

/// <summary>
/// Hosts the VPN service's side of the app: it takes the app's requests, connects with the options the app saved, and
/// reports the connection's state back.
/// </summary>
/// <remarks>
/// The state goes to the config folder as <see cref="ClientOptions.VpnStatusFileName"/> at each change, and with each
/// answer. A session that ended because the server moved, or the network lacks its address's IP version, is followed
/// at once by a new connect where the platform lets the service connect by itself
/// (<see cref="IVpnServiceHandler.CanReconnect"/>).
/// </remarks>
public class VpnServiceHost : IVpnTunnelHolder, IDisposable
{
    // The host runs the flows: connect, the service's own reconnect, stop, revoke and a session's end. What they share
    // lives in Sessions, one class per job. Every connect begins a new session (VpnServiceContext). Each part checks
    // the session under its own lock, so a connect that a newer one replaced sees it and leaves the newer one alone.
    // A check is not a reservation: a platform call made after it may still meet a newer connect.
    private readonly ApiController _apiController;
    private readonly IVpnServiceHandler _vpnServiceHandler;
    private readonly IClientSocketFactory _socketFactory;
    private readonly string _dataFolder;
    private readonly LogService? _logService;
    private readonly VpnServiceConnectState _connectState;
    private readonly VpnServiceTunnel _tunnel;
    private readonly VpnServiceKillSwitch _killSwitch;
    private readonly VpnServiceNotification _notification;
    private readonly ConnectionInfoUpdater _connectionInfoUpdater;
    private readonly TaskCompletionSource _predecessorsWaiting = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private readonly TimeSpan _killServiceTimeout = TimeSpan.FromSeconds(3);

    internal VpnHoodClient? Client => _connectState.Client;

    internal VpnHoodClient RequiredClient =>
        Client ?? throw new InvalidOperationException("Client is not initialized.");

    /// <summary>
    /// Gets a value indicating whether the client forwards IPv4 TCP now: the live session's choice, which follows the
    /// server's support and a domain filter's needs; the client's setting before the session is up; and
    /// <see langword="false"/> with no client.
    /// </summary>
    public bool IsTcpForwarding {
        get {
            var client = Client;
            return client?.Session?.Status.IsTcpForwarding ?? client?.UseTcpForwarding ?? false;
        }
    }

    internal VpnServiceContext Context { get; }
    internal ClientReconfigurer Reconfigurer { get; }
    /// <summary>
    /// Gets the placeholder info before the service has made a real one (<see cref="ConnectionInfo.Default"/>).
    /// </summary>
    public static ConnectionInfo DefaultConnectionInfo => ConnectionInfo.Default;

    // for tests: set when a take of the tunnel starts waiting for an earlier take or release
    internal Task TunnelTakeWaiting => _tunnel.TakeWaiting;

    // for tests: set when a session's end starts waiting for a hold that another end is closing
    internal Task TunnelReleaseWaiting => _tunnel.ReleaseWaiting;

    // for tests: set when a failed connect's end starts waiting for earlier connects or a replaced client's close
    internal Task PredecessorsWaiting => _predecessorsWaiting.Task;

    /// <summary>
    /// Initializes a new instance of the <see cref="VpnServiceHost"/> class: it makes the config folder where it is
    /// missing, starts listening to the app, and reports <see cref="ClientState.Initializing"/>.
    /// </summary>
    /// <param name="configFolder">The VPN service's config folder (<c>IDevice.VpnServiceConfigFolder</c>).</param>
    /// <param name="dataFolder">
    /// The VPN service's data folder, with the person's proxies (<c>IDevice.VpnServiceDataFolder</c>).
    /// </param>
    /// <param name="vpnServiceHandler">The platform's part: the adapter, the client factory, and the notification.</param>
    /// <param name="socketFactory">The factory of the client's sockets.</param>
    /// <param name="messageListener">The channel the app's requests come over, which the host disposes.</param>
    /// <param name="withLogger">
    /// <see langword="true"/> to keep a log file of its own, for a VPN service in a process of its own; in the app's
    /// process, its lines go to the app's log. The default is <see langword="true"/>.
    /// </param>
    /// <param name="tunnelLock">
    /// The machine's one tunnel, where the platform has one (the desktops), or <see langword="null"/>.
    /// </param>
    /// <param name="killSwitch">
    /// The device's kill switch, where it has one (<c>IDevice.KillSwitch</c>), or <see langword="null"/>.
    /// </param>
    public VpnServiceHost(string configFolder,
        string dataFolder,
        IVpnServiceHandler vpnServiceHandler,
        IClientSocketFactory socketFactory,
        IMessageListener messageListener,
        bool withLogger = true,
        IVpnTunnelLock? tunnelLock = null,
        IVpnKillSwitch? killSwitch = null)
    {
        // the service may start before the app has made its folder: always-on, after an update
        Directory.CreateDirectory(configFolder);
#pragma warning disable CS0618 // Type or member is obsolete
        VpnServiceFolders.MigrateServiceFiles(dataFolder, configFolder);
#pragma warning restore CS0618 // Type or member is obsolete
        _dataFolder = dataFolder;
        Context = new VpnServiceContext(configFolder);
        _socketFactory = socketFactory;
        _vpnServiceHandler = vpnServiceHandler;
        _connectState = new VpnServiceConnectState(Context, vpnServiceHandler, VpnHoodClient_StateChanged);
        _tunnel = new VpnServiceTunnel(tunnelLock, this, Context);
        _killSwitch = new VpnServiceKillSwitch(killSwitch, _tunnel, Context);
        _notification = new VpnServiceNotification(vpnServiceHandler, Context);
        _connectionInfoUpdater = new ConnectionInfoUpdater(Context, _connectState, _killSwitch);

        // A log file of its own where the VPN service has a process of its own (withLogger), opened at
        // each start request; in the app's process its lines go to the app's file, through the same
        // logger. The process's other sinks are its head's.
        _logService = withLogger ? new LogService(Context.LogFilePath) : null;
        VhLogger.TcpCloseEventId = GeneralEventId.Stream;

        // before the apiController, which may hand it a request at once
        Reconfigurer = new ClientReconfigurer(this);

        // start apiController; the transport (IMessageListener) owns endpoint/key concerns
        _apiController = new ApiController(this, messageListener);
        VhLogger.Instance.LogInformation("VpnServiceHost has been initiated...");

        // Write an initial ConnectionInfo so that the app side can discover the service state.
        _ = Context.TryWriteConnectionInfo(Context.BeginSession(),
            ConnectionInfoBuilder.BuildWithoutClient(ClientState.Initializing, sessionName: null, exception: null),
            CancellationToken.None);
    }

    /// <summary>Connects with the options the app saved, unless a connection is under way.</summary>
    /// <param name="forceReconnect">
    /// <see langword="true"/> to end a connection under way and connect anew; <see langword="false"/> to leave it.
    /// </param>
    /// <param name="isAlwaysOn">
    /// <see langword="true"/> if the system starts the VPN with no one asking, as Android's always-on; refused without
    /// <see cref="ClientOptions.AllowAlwaysOn"/>.
    /// </param>
    /// <returns>
    /// A task whose result is <see langword="true"/> once a connect was made and ran its course, its outcome in the
    /// state the app reads; <see langword="false"/> where none was made, as the host is disposed, or a connection is
    /// under way, one still starting too, where it is not forced.
    /// </returns>
    /// <remarks>It never throws: a failure is reported to the app as the state's error, or, where it is not, logged.</remarks>
    public Task<bool> TryConnect(bool forceReconnect = false, bool isAlwaysOn = false)
    {
        return TryConnect(forceReconnect, isAlwaysOn, endedSessionId: null, knownHostAddresses: null);
    }

    // endedSessionId: set for the service's own reconnect, to the session that just ended. Such a connect keeps the
    // log, which holds why that session ended, and a stop asked for meanwhile wins over it.
    private async Task<bool> TryConnect(bool forceReconnect, bool isAlwaysOn, int? endedSessionId,
        HostNameAddresses? knownHostAddresses)
    {
        if (_connectState.IsDisposed)
            return false;

        try {
            var admission = _connectState.Admit(forceReconnect, endedSessionId);
            if (admission.Connect is not { } connect) {
                await EndRefusedConnect(admission).Vhc();
                return false;
            }

            try {
                // the old client is detached first, so its end is neither reported nor acted on; it closes beside this
                // connect, and a stop waits for that close
                if (connect.OldClient is { } oldClient) {
                    VhLogger.Instance.LogWarning("VpnService is killing the previous connection.");
                    oldClient.StateChanged -= VpnHoodClient_StateChanged;
                    _ = CloseOldClient(oldClient, connect.OldClientClosed);
                }

                await connect.OldConnectCts.TryCancelAsync().Vhc();
                connect.OldConnectCts.Dispose();

                await Connect(isAlwaysOn, keepLog: endedSessionId is not null, knownHostAddresses, connect.SessionId,
                    connect.PredecessorsEnded, connect.ConnectToken).Vhc();

                return true;
            }
            finally {
                connect.Ended.TrySetResult();
            }
        }
        catch (Exception ex) {
            if (!_connectState.IsDisconnectRequested)
                VhLogger.Instance.LogError(ex, "VpnServiceHost could not establish the connection.");

            return false;
        }
    }

    // A connect that did not begin. If a connection is under way, the app is told its state. If a stop came first, the
    // service stops, as no session will end it: iOS still waits to hear the end of the start it asked for
    private async Task EndRefusedConnect(ConnectAdmission admission)
    {
        if (admission.IsUnderWay) {
            VhLogger.Instance.LogWarning("VpnService connection is already in progress.");
            if (admission.ClientUnderWay is { } clientUnderWay)
                await _connectionInfoUpdater.Update(clientUnderWay, CancellationToken.None).Vhc();

            return;
        }

        if (admission.StopSessionId is { } stopSessionId)
            await EndSessionWithoutClient(stopSessionId, _connectState.ConnectsEnded, failure: null,
                CancellationToken.None).Vhc();
    }

    // A replaced client's close, which a stop waits for: its failure is logged
    private static async Task CloseOldClient(VpnHoodClient client, TaskCompletionSource closed)
    {
        try {
            await client.DisposeAsync().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not close the replaced client.");
        }
        finally {
            closed.TrySetResult();
        }
    }

    // keepLog: the log continues instead of starting anew. predecessorsEnded: completes when every earlier connect and
    // the replaced client have ended; a failure of this connect waits for it before it ends the session
    private async Task Connect(bool isAlwaysOn, bool keepLog, HostNameAddresses? knownHostAddresses, int sessionId,
        Task predecessorsEnded, CancellationToken cancellationToken)
    {
        // this connect's own client: a newer connect may replace Client meanwhile
        var client = await TryCreateSessionClient(isAlwaysOn, keepLog, knownHostAddresses, sessionId, predecessorsEnded,
            cancellationToken).Vhc();

        if (client is null)
            return;

        try {
            await _connectionInfoUpdater.Update(client, cancellationToken).Vhc();
            _notification.Show(Context.ConnectionInfo, sessionId, cancellationToken);

            await client.Connect(cancellationToken).Vhc();

            // a reconfigure that came while the session was not up
            await Reconfigurer.ApplyMissed(client, cancellationToken).Vhc();
        }
        catch (Exception ex) when (Context.SessionId != sessionId) {
            // a newer connect took over: the tunnel, the service and Client are its now, and this one closes only its
            // own client
            VhLogger.Instance.LogDebug(ex, "A connect that a newer one took over has ended.");
            _ = client.DisposeAsync();
        }
        catch (Exception ex) {
            // the client's end (EndClientSession) stops the notification and the service; a start that a requested
            // disconnect ended is no failure
            if (!_connectState.IsDisconnectRequested)
                VhLogger.Instance.LogError(ex, "VpnServiceHost could not establish the connection.");

            _ = client.DisposeAsync();
        }
    }

    // A connect's steps up to its published client. Returns null if it failed or a newer connect took over; a failure
    // ends the session here, as there is no client to end it
    private async Task<VpnHoodClient?> TryCreateSessionClient(bool isAlwaysOn, bool keepLog,
        HostNameAddresses? knownHostAddresses, int sessionId, Task predecessorsEnded,
        CancellationToken cancellationToken)
    {
        try {
            // the session's first word, at once: until then the status file may still hold the last session's end,
            // which the app would take for this one's (at a system's start the app wrote nothing over it)
            await Context.TryWriteConnectionInfo(sessionId,
                ConnectionInfoBuilder.BuildWithoutClient(ClientState.Initializing, sessionName: null, exception: null),
                cancellationToken).Vhc();

            // Before the options are read. A kept reconfigure is dropped: the options file already holds it, as the app
            // writes the file before it sends one. A kill switch asked for from BeginStart on is kept for Start
            await Reconfigurer.Reset(cancellationToken).Vhc();
            await _killSwitch.BeginStart(sessionId, cancellationToken).Vhc();

            var serviceOptions = await Context.ServiceOptionsFile.Read(cancellationToken).Vhc();
            var clientOptions = serviceOptions.ClientOptions;
            _logService?.Start(clientOptions.LogServiceOptions, deleteOldReport: !keepLog);
            VpnServiceContract.Verify(serviceOptions.ContractVersion);

            // check if auto start is allowed: the system's (Android's), or the app's own (a desktop's)
            if ((isAlwaysOn || clientOptions.IsAlwaysOn) && !clientOptions.AllowAlwaysOn)
                throw new AlwaysOnNotAllowedException("Auto start is only available for premium accounts.");

            VhLogger.Instance.LogInformation("VpnService is connecting... ProcessId: {ProcessId}",
                Process.GetCurrentProcess().Id);

            // create a connection info for notification
            await Context.TryWriteConnectionInfo(sessionId,
                ConnectionInfoBuilder.BuildWithoutClient(ClientState.Initializing, clientOptions.SessionName,
                    exception: null),
                cancellationToken).Vhc();

            // show notification as soon as possible
            _notification.Show(Context.ConnectionInfo, sessionId, cancellationToken);

            // sni is sensitive, must be explicitly enabled
            clientOptions.ForceLogSni |=
                clientOptions.LogServiceOptions.LogEventNames.Contains(nameof(GeneralEventId.Sni),
                    StringComparer.OrdinalIgnoreCase);

            VhLogger.Instance.LogDebug("VpnService is creating a new VpnHoodClient.");
            var adapterSetting = new VpnAdapterSettings {
                AdapterName = clientOptions.AdapterName,
                AppId = clientOptions.AppId,
                Blocking = false
            };

            // the machine's one tunnel, before the adapter comes up; a null-capture starts no platform adapter, so it
            // takes none
            if (!clientOptions.UseNullCapture)
                await _tunnel.EnsureTaken(sessionId, cancellationToken).Vhc();

            // The block goes up before the adapter and the connect's first packet. The client is told the switch in
            // use: under a block the system's resolver is cut off, and how the client looks names up and gets the
            // proxies' list depends on it
            clientOptions.UseKillSwitch = await _killSwitch.Start(sessionId, clientOptions, cancellationToken).Vhc();

            // null-capture is host debug plumbing, so the adapter is resolved here and handed to the
            // handler ready-made
            var vpnAdapter = clientOptions.UseNullCapture
                ? new NullVpnAdapter(blocking: false)
                : _vpnServiceHandler.CreateAdapter(adapterSetting, clientOptions.DebugData1);

            var client = await CreateClient(vpnAdapter, serviceOptions, knownHostAddresses, sessionId,
                cancellationToken).Vhc();

            // not published: a newer connect began or a stop came while the client was made. It is closed, its adapter
            // not up yet, and the connect ends as canceled
            if (!_connectState.TryPublishClient(client, sessionId, cancellationToken)) {
                await client.DisposeAsync().Vhc();
                throw new OperationCanceledException("The connect was canceled as its client was made.");
            }

            return client;
        }
        catch (Exception ex) when (Context.SessionId != sessionId) {
            // a newer connect took over: the tunnel and the service are its now
            VhLogger.Instance.LogDebug(ex, "A connect that a newer one took over has ended.");
            return null;
        }
        catch (Exception ex) {
            // no client, so no adapter of this connect is up; earlier connects and the replaced client may still hold
            // theirs, so the end waits for them
            if (!predecessorsEnded.IsCompleted)
                _predecessorsWaiting.TrySetResult();

            await EndSessionWithoutClient(sessionId, predecessorsEnded, ex, cancellationToken).Vhc();
            return null;
        }
    }

    // The client owns the adapter once it is made; until then a failure closes the adapter here and is rethrown. Live
    // filter updates need no wiring here: the filters re-read their folder manifests on Reconfigure
    private async Task<VpnHoodClient> CreateClient(IVpnAdapter vpnAdapter, VpnServiceOptions serviceOptions,
        HostNameAddresses? knownHostAddresses, int sessionId, CancellationToken cancellationToken)
    {
        try {
            vpnAdapter.Started += (_, adapterOptions) => VpnAdapter_Started(sessionId, adapterOptions);
            _connectState.SetVpnAdapter(vpnAdapter, sessionId);
            var clientFactory = _vpnServiceHandler.CreateClientFactory();
            var clientParams = new VpnHoodClientParams {
                ServiceOptions = serviceOptions,
                VpnAdapter = vpnAdapter,
                SocketFactory = _socketFactory,
                ConfigFolder = Context.ConfigFolder,
                DataFolder = _dataFolder,
                KnownHostAddresses = knownHostAddresses
            };

            return await VpnServiceClientBuilder.Build(clientFactory, clientParams, cancellationToken).Vhc();
        }
        catch {
            VhInvoker.TryInvoke("close the VPN adapter of a client not made", LogLevel.Error, vpnAdapter.Dispose);
            throw;
        }
    }

    // every start of the adapter, a restart too: the kill switch's block is pointed at it while its connect is current
    private void VpnAdapter_Started(int sessionId, VpnAdapterOptions adapterOptions)
    {
        if (Context.SessionId != sessionId)
            return;

        _ = VhInvoker.TryInvokeAsync("point the kill switch's block at the tunnel", LogLevel.Error,
            () => _killSwitch.Repoint(sessionId, adapterOptions, CancellationToken.None));
    }

    private void VpnHoodClient_StateChanged(object? sender, EventArgs e)
    {
        // the session before the client check: one begun in between has detached this client already
        var sessionId = Context.SessionId;
        var client = (VpnHoodClient?)sender;
        if (client is null || Client != client)
            return; // the active client is changed, so ignore the event

        // a session that ended for a new connect: the app does not see its end, as the service connects again at once
        if (_connectState.IsReconnectEnd(client)) {
            if (client.State is ClientState.Disposed)
                _ = ReconnectAfterSessionEnd(client);

            return;
        }

        VhLogger.Instance.LogDebug("VpnService update the connection info file. State:{State}, LastError: {LastError}",
            client.State, client.Session?.Status.Error);

        if (client.State is ClientState.Disposed) {
            _ = EndClientSession(client, sessionId);
            return;
        }

        _ = _connectionInfoUpdater.Update(client, CancellationToken.None);

        // checked again: since the check above the client may have ended for a reconnect, or been replaced
        if (Client != client || _connectState.IsReconnectEnd(client))
            return;

        // the info as the client has it now, not the context's: the write above may still wait for its lock
        _notification.Show(ConnectionInfoBuilder.Build(client, _connectState.DisconnectException), sessionId,
            CancellationToken.None);
    }

    // One reconnect per ended client, and none once a stop is asked for. After a server move it passes on the new
    // addresses the move's check found: a lookup now may still go through the old VPN, which Android tears down a
    // moment later
    private async Task ReconnectAfterSessionEnd(VpnHoodClient client)
    {
        if (_connectState.ClaimEndedClient(client) is not { } endedSessionId)
            return;

        client.StateChanged -= VpnHoodClient_StateChanged;
        VhLogger.Instance.LogInformation("The session has ended for a new connect, so the VPN service connects again. " +
                                         "Reason: {Reason}", client.LastException?.Message);

        var newAddresses = (client.LastException as ServerAddressChangedException)?.NewAddresses;
        await TryConnect(forceReconnect: false, isAlwaysOn: false, endedSessionId, newAddresses).Vhc();
    }

    /// <summary>
    /// Disconnects: it ends a connect under way and the session, and reports the end to the app; the kill switch's
    /// block stays as it is, as lifting it is the app's call.
    /// </summary>
    /// <param name="exception">
    /// Why, reported to the app as the end's error, or <see langword="null"/> to keep the reason an earlier disconnect or
    /// a revoke gave, or else the error the session ended with, if any.
    /// </param>
    /// <returns>A task that completes once the session is gone; it never throws, and logs a failure.</returns>
    public Task TryDisconnect(Exception? exception = null)
    {
        if (_connectState.IsDisposed)
            return Task.CompletedTask;

        if (exception != null)
            VhLogger.Instance.LogError(exception, "VpnServiceHost is disconnecting due to an error...");
        else
            VhLogger.Instance.LogDebug("VpnServiceHost is disconnecting...");

        // The mark is taken together with the connect it cancels and that connect's session, so a reconnect cannot slip
        // in between and this stop cannot end a newer session. The reason is kept with the mark, so a state the app
        // reads before the end already carries it
        return TryDisconnect(_connectState.MarkDisconnectRequested(exception));
    }

    private async Task TryDisconnect(RequestedDisconnect disconnect)
    {
        try {
            // The order matters. The session's kill-switch changes end before its client closes: the app may lift the
            // block at the client's end, and no change of this session may land after that. Waiting here is safe: a
            // session with a client is past its own block. The client starts closing before the cancel, so its connect
            // sees the close, and a person's own disconnect leaves no cancel error
            if (disconnect.Client is { } closingClient) {
                await _killSwitch.EndChanges(disconnect.SessionId).Vhc();
                _ = closingClient.DisposeAsync();
            }

            // a start still on its way - waiting for the tunnel, say - ends too, rather than connecting after
            await disconnect.ConnectCts.TryCancelAsync().Vhc();

            // again after the cancel: with no client, the connect's own start may have held the switch's lock until
            // now. The block itself stays: lifting it is the app's call
            await _killSwitch.EndChanges(disconnect.SessionId).Vhc();

            // every connect ends first, a replaced one too, with the tunnel and adapter its start may hold: the app
            // takes the session's end as "everything is released" and may connect again at once
            await disconnect.ConnectsEnded.Vhc();

            // a connect begun since owns the host now: it closes this session's client itself, and this stop reports
            // nothing
            var client = Client;
            if (Context.SessionId != disconnect.SessionId)
                return;

            if (client != null)
                await client.DisposeAsync().Vhc();

            // the tunnel goes back before the end is written: not where a connect has begun meanwhile, which keeps it
            if (!await _tunnel.TryRelease(disconnect.SessionId).Vhc())
                return;

            // the session's last word, unless the client's own end was written first: the first Disposed stands, and
            // the app's stop waits for it. Always reported as Disposed, as the client may still be closing
            var connectionInfo = client != null
                ? ConnectionInfoBuilder.Build(client, disconnect.Exception, ClientState.Disposed)
                : ConnectionInfoBuilder.BuildWithoutClient(ClientState.Disposed, sessionName: null,
                    disconnect.Exception);

            await Context.TryWriteConnectionInfo(disconnect.SessionId, connectionInfo, CancellationToken.None).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not disconnect the client.");
        }
    }

    // Another app took the machine's one tunnel and gets it when this returns, so the adapter goes down now, on this
    // thread, as Android does for a revoked VPN: else the next app's routes would meet ours. The session ends in its
    // own time
    void IVpnTunnelHolder.OnTunnelRevoked()
    {
        _ = RevokeTunnel();
    }

    // Returns the disconnect it starts, which a test waits for. The order matters: the stop is marked first, so the
    // end that the adapter's loss brings carries the revoke as its reason; then the adapter goes down here, before the
    // disconnect, whose close of the client could otherwise take it down after this has returned. The adapter is the
    // last registered one, taken with the mark, so a connect begun after the mark is not this revoke's to end
    internal Task RevokeTunnel()
    {
        VhLogger.Instance.LogInformation("Another VPN app asked for the tunnel, so this one lets it go.");
        _tunnel.Forget();

        var disconnect = _connectState.MarkDisconnectRequested(new VpnServiceRevokedException());
        if (disconnect.VpnAdapter is { } vpnAdapter)
            VhInvoker.TryInvoke("take the VPN adapter down for another app", LogLevel.Error, vpnAdapter.Dispose);

        return TryDisconnect(disconnect);
    }

    // A client's own end. The app takes a session's end as "everything is released" and may connect again at once, so
    // the end is written, and the service stopped, only after every earlier connect and replaced client has ended and
    // the session has let go. Until then the app is told Disconnecting
    private async Task EndClientSession(VpnHoodClient client, int sessionId)
    {
        try {
            if (Client != client || _connectState.IsReconnectEnd(client))
                return;

            // the app may lift the block as soon as it reads Disconnecting, so the kill-switch changes end first. The
            // write skips the proxies' save and is not awaited, so neither delays the tunnel's release
            await _killSwitch.EndChanges(sessionId).Vhc();
            _ = Context.TryWriteConnectionInfo(sessionId,
                ConnectionInfoBuilder.Build(client, _connectState.DisconnectException, ClientState.Disconnecting),
                CancellationToken.None);

            await _connectState.ConnectsEnded.Vhc();
            if (Client != client || _connectState.IsReconnectEnd(client) || !await TryLetSessionGo(sessionId).Vhc())
                return;

            await _connectionInfoUpdater.WriteClientEnd(client, sessionId).Vhc();

            // not where a newer connect has begun meanwhile, whose notification and service they are
            if (Context.SessionId != sessionId)
                return;

            // the service stops whatever the notification's close did
            VhLogger.Instance.LogDebug("VpnServiceHost requests to stop the notification and service.");
            VhInvoker.TryInvoke("stop the notification", LogLevel.Error, () => _notification.Stop(sessionId));

            // it may be a red flag for android if we don't stop the service after stopping the notification
            await Task.Delay(_killServiceTimeout).Vhc();
            StopService(sessionId);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not end the client's session.");
        }
    }

    // Ends a session that has no client: once `ends` completes and the session has let go, its end is written and the
    // service stops. Written before the stop, as a desktop's stop ends the process. failure: null if the connect never
    // began
    private async Task EndSessionWithoutClient(int sessionId, Task ends, Exception? failure,
        CancellationToken cancellationToken)
    {
        await ends.Vhc();
        if (!await TryLetSessionGo(sessionId).Vhc())
            return;

        // A requested stop is reported with its own reason (none for a person's own), not the cancel it caused. It is
        // written here too, as the first end stands and this one may come first: the stop's own write waits for a
        // failed connect to end. Any other failure is written unless the host's dispose canceled it
        var isStopped = _connectState.TryGetStopReason(sessionId, out var stopReason);
        if (isStopped || failure != null)
            await Context.TryWriteConnectionInfo(sessionId,
                ConnectionInfoBuilder.BuildWithoutClient(ClientState.Disposed, sessionName: null,
                    isStopped ? stopReason : failure),
                isStopped ? CancellationToken.None : cancellationToken).Vhc();

        StopService(sessionId);
    }

    // Before a session's end is written: its kill-switch changes end and its tunnel goes back, as the app may lift the
    // block and take the tunnel at the end. Returns false if a newer connect had begun by then
    private async Task<bool> TryLetSessionGo(int sessionId)
    {
        await _killSwitch.EndChanges(sessionId).Vhc();
        return await _tunnel.TryRelease(sessionId).Vhc();
    }

    // Stops the service, whatever the notification's stop did, unless a newer connect has begun, which the service now
    // serves. The platform's stop is called outside the session lock, as it may call back, so a connect can still
    // begin between the check and the stop
    private void StopService(int sessionId)
    {
        if (!_connectState.IsCurrent(sessionId))
            return;

        VhInvoker.TryInvoke("stop the notification", LogLevel.Error, () => _notification.Stop(sessionId));
        _vpnServiceHandler.StopSelf();
    }

    // the state an answer to the app carries, for the API controller
    internal Task<ConnectionInfo> UpdateConnectionInfoForAnswer(CancellationToken cancellationToken)
    {
        return _connectionInfoUpdater.UpdateForAnswer(cancellationToken);
    }

    // the kill switch, turned on or off, for the reconfigurer
    internal Task ReconfigureKillSwitch(bool useKillSwitch, CancellationToken cancellationToken)
    {
        return _killSwitch.Reconfigure(useKillSwitch, cancellationToken);
    }

    /// <summary>Ends the connection at once, without reporting it, and stops listening to the app.</summary>
    /// <remarks>
    /// A connect still under way is canceled first: the end of a client it has made may be reported before the host
    /// stops listening to that client.
    /// </remarks>
    public void Dispose()
    {
        if (!_connectState.TryMarkDisposed())
            return;

        // logged before the cancel, whose own lines then follow it
        VhLogger.Instance.LogDebug("VpnService Host is destroying...");
        _connectState.CancelLastConnect();

        // the end this close brings is not reported: the host stops listening to the client first
        var client = Client;
        if (client != null) {
            client.StateChanged -= VpnHoodClient_StateChanged;
            client.Dispose();
        }

        _tunnel.Dispose();

        _apiController.Dispose();
        VhLogger.Instance.LogDebug("VpnService has been destroyed.");
        _logService?.Dispose();
    }
}
