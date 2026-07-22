using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Abstractions.Exceptions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Sockets;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.VpnAdapters.Abstractions;

namespace VpnHood.Core.Client.VpnServices.Host;

public class VpnServiceHost : IDisposable
{
    private readonly ApiController _apiController;
    private readonly IVpnServiceHandler _vpnServiceHandler;
    private readonly ISocketFactory _socketFactory;
    private readonly LogService? _logService;
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

    public VpnServiceHost(string configFolder,
        IVpnServiceHandler vpnServiceHandler,
        ISocketFactory socketFactory,
        IMessageListener messageListener,
        bool withLogger = true,
        Func<bool, ILoggerProvider>? deviceLoggerProviderFactory = null)
    {
        Context = new VpnServiceContext(configFolder);
        _socketFactory = socketFactory;
        _vpnServiceHandler = vpnServiceHandler;

        // initialize logger. deviceLoggerProviderFactory lets a platform supply its own device log sink
        // (e.g. os_log on iOS); when null the LogService falls back to its default VhDeviceLoggerProvider.
        _logService = withLogger ? new LogService(Context.LogFilePath, deviceLoggerProviderFactory) : null;
        VhLogger.TcpCloseEventId = GeneralEventId.Stream;
        var serviceOptions = Context.TryReadServiceOptions();
        if (_logService != null && serviceOptions != null) {
            _logService.Start(serviceOptions.ClientOptions.LogServiceOptions);
        }

        // start apiController; the transport (IMessageListener) owns endpoint/key concerns
        _apiController = new ApiController(this, messageListener);
        VhLogger.Instance.LogInformation("VpnServiceHost has been initiated...");

        // Write an initial ConnectionInfo so that the app side can discover the service state.
        _ = UpdateConnectionInfo(ClientState.Initializing, sessionName: null, exception: null,
            CancellationToken.None);
    }

    private void VpnHoodClient_StateChanged(object? sender, EventArgs e)
    {
        var client = (VpnHoodClient?)sender;
        if (client is null || Client != client)
            return; // the active client is changed, so ignore the event

        // update last sate
        VhLogger.Instance.LogDebug("VpnService update the connection info file. State:{State}, LastError: {LastError}",
            client.State, client.Session?.Status.Error);
        _ = UpdateConnectionInfo(client, CancellationToken.None);

        // if client is not disposed, we can show notification
        if (client.State is not ClientState.Disposed) {
            // show notification
            _vpnServiceHandler.ShowNotification(Context.ConnectionInfo);
            return;
        }

        // client is disposed so stop the notification and service
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
            await _connectCts.TryCancelAsync();
            _connectCts.Dispose();

            // start connecting 
            _connectCts = new CancellationTokenSource();
            await Connect(isAlwaysOn, _connectCts.Token);
            return true;
        }
        catch (Exception ex) {
            if (!_disconnectRequested)
                VhLogger.Instance.LogError(ex, "VpnServiceHost could not establish the connection.");

            return false;
        }
    }

    private async Task Connect(bool isAlwaysOn, CancellationToken cancellationToken)
    {
        if (Client is not null)
            throw new InvalidOperationException("Client is already initialized.");
        
        try {
            // read client options and start log service
            var serviceOptions = Context.ReadServiceOptions();
            var clientOptions = serviceOptions.ClientOptions;
            _logService?.Start(clientOptions.LogServiceOptions, deleteOldReport: false);

            // check if auto start is allowed
            if (isAlwaysOn && !clientOptions.AllowAlwaysOn)
                throw new AlwaysOnNotAllowedException("Auto start is only available for premium accounts.");

            // restart the log service
            VhLogger.Instance.LogInformation("VpnService is connecting... ProcessId: {ProcessId}",
                Process.GetCurrentProcess().Id);

            // create a connection info for notification
            await UpdateConnectionInfo(ClientState.Initializing, sessionName: clientOptions.SessionName,
                exception: null, cancellationToken: cancellationToken).Vhc();

            // show notification as soon as possible
            _vpnServiceHandler.ShowNotification(Context.ConnectionInfo);

            // sni is sensitive, must be explicitly enabled
            clientOptions.ForceLogSni |=
                clientOptions.LogServiceOptions.LogEventNames.Contains(nameof(GeneralEventId.Sni),
                    StringComparer.OrdinalIgnoreCase);

            // create client
            VhLogger.Instance.LogDebug("VpnService is creating a new VpnHoodClient.");
            var adapterSetting = new VpnAdapterSettings {
                AdapterName = clientOptions.AppName,
                Blocking = false,
                AutoDisposePackets = true
            };

            // null-capture is host debug plumbing, so the adapter is resolved here and handed to the
            // handler ready-made
            var vpnAdapter = clientOptions.UseNullCapture
                ? new NullVpnAdapter(autoDisposePackets: true, blocking: false)
                : _vpnServiceHandler.CreateAdapter(adapterSetting, clientOptions.DebugData1);

            // assign Client member to monitor states while connecting, even if the client is not connected yet.
            // This is important to update the connection info file and notification correctly.
            try {
                Client = await _vpnServiceHandler.CreateClientFactory().Create(new VpnHoodClientParams {
                    ServiceOptions = serviceOptions,
                    VpnAdapter = vpnAdapter,
                    SocketFactory = _socketFactory,
                    ConfigFolder = Context.ConfigFolder
                }, cancellationToken).Vhc();
            }
            catch {
                vpnAdapter.Dispose(); // the client owns the adapter only once constructed
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
            await UpdateConnectionInfo(ClientState.Disposed, null, ex, _connectCts.Token).Vhc();
            _vpnServiceHandler.StopNotification();
            _vpnServiceHandler.StopSelf();
        }
        catch (Exception ex) {
            // we do not need to do anything if client is not null because the state changed event
            // will handle the notification and service stopping.
            // dispose the client in the background
            VhLogger.Instance.LogError(ex, "VpnServiceHost could not establish the connection.");
            _ = Client?.DisposeAsync();
        }
    }

    public Task UpdateConnectionInfo(VpnHoodClient client, CancellationToken cancellationToken)
    {
        return UpdateConnectionInfo(client, null, cancellationToken);
    }

    public async Task UpdateConnectionInfo(VpnHoodClient client, Exception? ex, CancellationToken cancellationToken)
    {
        // flush pending proxy statuses so the app process sees fresh data in the shared endpoint store
        if (client.ProxyConnector != null)
            await client.ProxyConnector.Flush().Vhc();

        var connectionInfo = new ConnectionInfo {
            CreatedTime = FastDateTime.Now,
            ProxyConnectorStatus = client.ProxyConnector?.Status,
            SessionName = client.Config.SessionName,
            SessionInfo = client.Session?.Info,
            SessionStatus = client.Session?.Status.ToDto(),
            ClientState = client.State,
            ClientStateProgress = client.StateProgress,
            ClientStateChangedTime = client.StateChangedTime,
            Error = ex?.ToApiError() ?? client.LastException?.ToApiError()
        };

        await Context.TryWriteConnectionInfo(connectionInfo, cancellationToken);
    }

    public async Task UpdateConnectionInfo(ClientState clientState, string? sessionName, Exception? exception,
        CancellationToken cancellationToken)
    {
        var connectionInfo = new ConnectionInfo {
            CreatedTime = FastDateTime.Now,
            ProxyConnectorStatus = null,
            SessionInfo = null,
            SessionStatus = null,
            ClientState = clientState,
            ClientStateProgress = null,
            ClientStateChangedTime = null,
            Error = exception?.ToApiError(),
            SessionName = sessionName
        };

        await Context.TryWriteConnectionInfo(connectionInfo, cancellationToken);
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

            // let dispose in the background
            var client = Client;
            if (client != null)
                await client.DisposeAsync();

            // always publish the terminal state: the app-side TryStop waits for it before reporting the
            // service stopped — the client releases its resources (filter dbs, adapter) only during
            // DisposeAsync, so leaving "Disconnecting" as the last published state lets the next connect
            // race that teardown. Also overwrites the last exception if one exists.
            await UpdateConnectionInfo(ClientState.Disposed, null, exception, CancellationToken.None);
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not disconnect the client.");
        }
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

        // dispose api controller
        _apiController.Dispose();
        VhLogger.Instance.LogDebug("VpnService has been destroyed.");
        _logService?.Dispose();
    }
}