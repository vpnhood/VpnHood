using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Filtering.Abstractions;
using VpnHood.Net.Toolkit.ApiClients;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Net.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

public class VpnServiceHost : IVpnTunnelHolder, IDisposable
{
    private readonly ApiController _apiController;
    private readonly IVpnServiceHandler _vpnServiceHandler;
    private readonly ISocketFactory _socketFactory;
    private readonly IVpnTunnelLock? _tunnelLock;
    private readonly string _dataFolder;
    private readonly LogService? _logService;
    private IDisposable? _tunnelHold;
    private IVpnAdapter? _vpnAdapter; // the last connect's, which a revoke takes down at once
    private Exception? _disconnectException;
    private CancellationTokenSource _connectCts = new();
    private readonly TimeSpan _killServiceTimeout = TimeSpan.FromSeconds(3);
    private bool _disposed;
    private bool _disconnectRequested;

    internal VpnHoodClient? Client { get; private set; }

    internal VpnHoodClient RequiredClient =>
        Client ?? throw new InvalidOperationException("Client is not initialized.");

    /// <summary>
    /// The current TCP-proxy status. Prefers the live session value (it reflects server capability and
    /// domain-filtering overrides), falls back to the client's configured value before the session is
    /// established, and to false when there is no client yet.
    /// </summary>
    public bool IsTcpProxy {
        get {
            var client = Client;
            return client?.Session?.Status.IsTcpProxy ?? client?.UseTcpProxy ?? false;
        }
    }

    internal VpnServiceContext Context { get; }
    public static ConnectionInfo DefaultConnectionInfo => ConnectionInfo.Default;

    // configFolder and dataFolder: IDevice's VpnServiceConfigFolder and VpnServiceDataFolder.
    // tunnelLock: the machine's one tunnel, where the platform has one (the desktops).
    public VpnServiceHost(string configFolder,
        string dataFolder,
        IVpnServiceHandler vpnServiceHandler,
        ISocketFactory socketFactory,
        IMessageListener messageListener,
        bool withLogger = true,
        IVpnTunnelLock? tunnelLock = null)
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
        _tunnelLock = tunnelLock;

        // A log file of its own where the VPN service has a process of its own (withLogger), opened at
        // each start request; in the app's process its lines go to the app's file, through the same
        // logger. The process's other sinks are its head's.
        _logService = withLogger ? new LogService(Context.LogFilePath) : null;
        VhLogger.TcpCloseEventId = GeneralEventId.Stream;

        // start apiController; the transport (IMessageListener) owns endpoint/key concerns
        _apiController = new ApiController(this, messageListener);
        VhLogger.Instance.LogInformation("VpnServiceHost has been initiated...");

        // Write an initial ConnectionInfo so that the app side can discover the service state.
        _ = Context.TryWriteConnectionInfo(Context.BeginSession(),
            CreateConnectionInfo(ClientState.Initializing, sessionName: null, exception: null), CancellationToken.None);
    }

    private void VpnHoodClient_StateChanged(object? sender, EventArgs e)
    {
        var client = (VpnHoodClient?)sender;
        if (client is null || Client != client)
            return; // the active client is changed, so ignore the event

        // update last sate; a disconnect asked for with a reason - a revoke, say - carries it, since this
        // write and TryDisconnect's own may land in either order
        VhLogger.Instance.LogDebug("VpnService update the connection info file. State:{State}, LastError: {LastError}",
            client.State, client.Session?.Status.Error);
        _ = UpdateConnectionInfo(client, _disconnectException, CancellationToken.None);

        // if client is not disposed, we can show notification
        if (client.State is not ClientState.Disposed) {
            // the info as the client has it now: the write above may wait for its lock, and until it is
            // done the context's info is the one before
            _vpnServiceHandler.ShowNotification(CreateConnectionInfo(client, _disconnectException));
            return;
        }

        // client is disposed, its adapter with it, so the tunnel is free for another app; stop the
        // notification and service
        ReleaseTunnel();
        VhLogger.Instance.LogDebug("VpnServiceHost requests to stop the notification and service.");
        _vpnServiceHandler.StopNotification();

        // it may be a red flag for android if we don't stop the service after stopping the notification
        Task.Delay(_killServiceTimeout).ContinueWith(_ => {
            if (client != Client) return;
            VhLogger.Instance.LogDebug("VpnServiceHost requests to StopSelf.");
            _vpnServiceHandler.StopSelf();
        });
    }

    public async Task<bool> TryConnect(bool forceReconnect = false, bool isAlwaysOn = false)
    {
        if (_disposed)
            return false;

        try {
            _disconnectRequested = false;
            _disconnectException = null;
            var oldClient= Client;

            // handle previous client
            if (oldClient != null) {
                if (!forceReconnect && oldClient is not { State: ClientState.Disposed or ClientState.Disconnecting }) {
                    // user must disconnect first
                    VhLogger.Instance.LogWarning("VpnService connection is already in progress.");
                    await UpdateConnectionInfo(oldClient, _connectCts.Token).Vhc();
                    return false;
                }

                // this prevents the previous connection to overwrite the state or stop the service
                VhLogger.Instance.LogWarning("VpnService is killing the previous connection.");
                oldClient.StateChanged -= VpnHoodClient_StateChanged;
                Client = null; // double sure to prevent the event handler considered as active client. 

                // dispose old client in the background
                _ = oldClient.DisposeAsync();
            }

            // cancel previous connection if exists
            await _connectCts.TryCancelAsync().Vhc();
            _connectCts.Dispose();

            // start connecting: a new session, which an older one's words can no longer overwrite
            var sessionId = Context.BeginSession();
            _connectCts = new CancellationTokenSource();
            await Connect(isAlwaysOn, sessionId, _connectCts.Token).Vhc();
            return true;
        }
        catch (Exception ex) {
            if (!_disconnectRequested)
                VhLogger.Instance.LogError(ex, "VpnServiceHost could not establish the connection.");

            return false;
        }
    }

    private async Task Connect(bool isAlwaysOn, int sessionId, CancellationToken cancellationToken)
    {
        if (Client is not null)
            throw new InvalidOperationException("Client is already initialized.");
        
        try {
            // read the client options, and start the log with them: the file holds this session
            var serviceOptions = Context.ServiceOptionsFile.Read();
            var clientOptions = serviceOptions.ClientOptions;
            _logService?.Start(clientOptions.LogServiceOptions);

            // check if auto start is allowed: the system's (Android's), or the app's own (a desktop's)
            if ((isAlwaysOn || clientOptions.IsAlwaysOn) && !clientOptions.AllowAlwaysOn)
                throw new AlwaysOnNotAllowedException("Auto start is only available for premium accounts.");

            VhLogger.Instance.LogInformation("VpnService is connecting... ProcessId: {ProcessId}",
                Process.GetCurrentProcess().Id);

            // create a connection info for notification
            await Context.TryWriteConnectionInfo(sessionId,
                CreateConnectionInfo(ClientState.Initializing, clientOptions.SessionName, exception: null),
                cancellationToken).Vhc();

            // show notification as soon as possible
            _vpnServiceHandler.ShowNotification(Context.ConnectionInfo);

            // sni is sensitive, must be explicitly enabled
            clientOptions.ForceLogSni |=
                clientOptions.LogServiceOptions.LogEventNames.Contains(nameof(GeneralEventId.Sni),
                    StringComparer.OrdinalIgnoreCase);

            // create client
            VhLogger.Instance.LogDebug("VpnService is creating a new VpnHoodClient.");
            var adapterSetting = new VpnAdapterSettings {
                AdapterName = clientOptions.AdapterName,
                AppId = clientOptions.AppId,
                Blocking = false,
                AutoDisposePackets = true
            };

            // The machine's one tunnel, before an adapter comes up: an app that holds it drops its adapter
            // and lets go, and this host keeps it across its own reconnects until its client is gone or
            // another app asks for it. A null-capture makes no adapter, so it takes none.
            if (_tunnelLock != null && !clientOptions.UseNullCapture && _tunnelHold == null)
                _tunnelHold = await _tunnelLock.Take(this, cancellationToken).Vhc();

            // null-capture is host debug plumbing, so the adapter is resolved here and handed to the
            // handler ready-made
            var vpnAdapter = clientOptions.UseNullCapture
                ? new NullVpnAdapter(autoDisposePackets: true, blocking: false)
                : _vpnServiceHandler.CreateAdapter(adapterSetting, clientOptions.DebugData1);
            _vpnAdapter = vpnAdapter;

            // assign Client member to monitor states while connecting, even if the client is not connected yet.
            // This is important to update the connection info file and notification correctly.
            var clientFactory = _vpnServiceHandler.CreateClientFactory();
            var clientParams = new VpnHoodClientParams {
                ServiceOptions = serviceOptions,
                VpnAdapter = vpnAdapter,
                SocketFactory = _socketFactory,
                ConfigFolder = Context.ConfigFolder,
                DataFolder = _dataFolder
            };

            // the filter pipes are self-updating: their split filter stages re-read the folder
            // manifests when the Reconfigure command rolls down them, so a live filter update needs no
            // wiring here and the client stays blind to the swap (its caches self-invalidate via the
            // rolled-up change event)
            var netFilter = new NetFilter {
                IpFilter = clientFactory.CreateIpFilter(clientParams),
                DomainFilter = clientFactory.CreateDomainFilter(clientParams),
                IpMapper = clientFactory.CreateIpMapper(clientParams)
            };

            try {
                Client = await clientFactory.Create(clientParams, netFilter, cancellationToken).Vhc();
            }
            catch {
                // the client owns the adapter and the filters only once constructed
                netFilter.Dispose();
                vpnAdapter.Dispose();
                throw;
            }

            Client.StateChanged += VpnHoodClient_StateChanged;

            // show notification.
            await UpdateConnectionInfo(Client, cancellationToken).Vhc();
            _vpnServiceHandler.ShowNotification(Context.ConnectionInfo);

            // let connect in the background
            // ignore cancellation because it will be cancelled by disconnect or dispose
            await Client.Connect(cancellationToken).Vhc();
        }
        catch (Exception ex) when (Client is null) {
            // no client, so no adapter is up: the tunnel goes back
            ReleaseTunnel();
            await Context.TryWriteConnectionInfo(sessionId,
                CreateConnectionInfo(ClientState.Disposed, sessionName: null, ex), _connectCts.Token).Vhc();
            _vpnServiceHandler.StopNotification();
            _vpnServiceHandler.StopSelf();
        }
        catch (Exception ex) {
            // we do not need to do anything if client is not null because the state changed event
            // will handle the notification and service stopping.
            // dispose the client in the background. A start that a requested disconnect ended is no failure
            if (!_disconnectRequested)
                VhLogger.Instance.LogError(ex, "VpnServiceHost could not establish the connection.");

            _ = Client?.DisposeAsync();
        }
    }

    // a disconnect asked for with a reason - a revoke, say - carries it, as the client's own state changes do, or
    // a reply to the app would publish the session's end without it
    public Task UpdateConnectionInfo(VpnHoodClient client, CancellationToken cancellationToken)
    {
        return UpdateConnectionInfo(client, _disconnectException, cancellationToken);
    }

    public async Task UpdateConnectionInfo(VpnHoodClient client, Exception? ex, CancellationToken cancellationToken)
    {
        // flush pending proxy statuses so the app process sees fresh data in the shared endpoint store
        if (client.ProxyConnector != null)
            await client.ProxyConnector.Flush().Vhc();

        // nothing from a client that is no longer the current one. The session before the client check: one begun in
        // between has detached this client already
        var sessionId = Context.SessionId;
        if (client != Client)
            return;

        await Context.TryWriteConnectionInfo(sessionId, CreateConnectionInfo(client, ex), cancellationToken).Vhc();
    }

    // the connection's info as the client has it now
    private static ConnectionInfo CreateConnectionInfo(VpnHoodClient client, Exception? ex)
    {
        return new ConnectionInfo {
            CreatedTime = FastDateTime.UtcNow,
            ProxyConnectorStatus = client.ProxyConnector?.Status,
            SessionName = client.Config.SessionName,
            SessionInfo = client.Session?.Info,
            SessionStatus = client.Session?.Status.ToDto(),
            ClientState = client.State,
            ClientStateProgress = client.StateProgress,
            ClientStateChangedTime = client.StateChangedTime,
            Error = ex?.ToApiError() ?? client.LastException?.ToApiError()
        };
    }

    // the host's own word, with no client to tell it
    private static ConnectionInfo CreateConnectionInfo(ClientState clientState, string? sessionName,
        Exception? exception)
    {
        return new ConnectionInfo {
            CreatedTime = FastDateTime.UtcNow,
            ProxyConnectorStatus = null,
            SessionInfo = null,
            SessionStatus = null,
            ClientState = clientState,
            ClientStateProgress = null,
            ClientStateChangedTime = null,
            Error = exception?.ToApiError(),
            SessionName = sessionName
        };
    }


    public async Task TryDisconnect(Exception? exception = null)
    {
        if (_disposed)
            return;

        try {
            _disconnectRequested = true;
            if (exception != null)
                VhLogger.Instance.LogError(exception, "VpnServiceHost is disconnecting due to an error...");
            else
                VhLogger.Instance.LogDebug(exception, "VpnServiceHost is disconnecting...");

            // a plain disconnect keeps the reason the session already ended for (a revoke, say) in the terminal
            // state below: the app may ask for it before it has seen that end
            exception ??= _disconnectException;
            _disconnectException = exception;

            // a start still on its way - waiting for the tunnel, say - ends too, rather than connecting after
            _connectCts.TryCancel();

            // the session this disconnect ends, so its end cannot land on a newer one
            var sessionId = Context.SessionId;

            // let dispose in the background
            var client = Client;
            if (client != null)
                await client.DisposeAsync().Vhc();

            // the terminal state, unless the client has published its own first: the first Disposed is the
            // session's last word, which the app-side TryStop waits for before reporting the service stopped
            await Context.TryWriteConnectionInfo(sessionId,
                CreateConnectionInfo(ClientState.Disposed, sessionName: null, exception), CancellationToken.None).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not disconnect the client.");
        }
    }

    // Another app took the machine's one tunnel, which goes to it once this returns. The adapter goes now,
    // on this thread, as Android takes a revoked VPN's, or the next app's routes would meet its own: first,
    // so the client's dispose, which it may set off, cannot be the one tearing it down meanwhile. The
    // session ends in its own time, and a connect still on its way, which may not have its adapter yet,
    // ends too. The hold ends itself, so a reconnect takes the tunnel anew.
    void IVpnTunnelHolder.OnTunnelRevoked()
    {
        VhLogger.Instance.LogInformation("Another VPN app asked for the tunnel, so this one lets it go.");
        Interlocked.Exchange(ref _tunnelHold, null);

        // before the adapter goes, so the client's end it brings tells of the revoke
        var exception = new VpnServiceRevokedException();
        _disconnectRequested = true;
        _disconnectException = exception;
        _vpnAdapter?.Dispose();
        _ = TryDisconnect(exception);
    }

    // Once no adapter of this host is up.
    private void ReleaseTunnel()
    {
        Interlocked.Exchange(ref _tunnelHold, null)?.Dispose();
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // cancel connection if exists
        VhLogger.Instance.LogDebug("VpnService Host is destroying...");
        _connectCts.TryCancel();
        _connectCts.Dispose();

        // dispose client
        var client = Client;
        if (client != null) {
            client.StateChanged -= VpnHoodClient_StateChanged; // after VpnHoodClient disposed
            client.Dispose();
        }

        ReleaseTunnel();

        // dispose api controller
        _apiController.Dispose();
        VhLogger.Instance.LogDebug("VpnService has been destroyed.");
        _logService?.Dispose();
    }
}