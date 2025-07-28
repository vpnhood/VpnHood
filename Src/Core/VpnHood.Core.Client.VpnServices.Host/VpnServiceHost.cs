using Microsoft.Extensions.Logging;
using System.Diagnostics;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;
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
    private int _isDisposed;
    private bool _disconnectRequested;

    internal VpnHoodClient? Client { get; private set; }
    internal VpnHoodClient RequiredClient => Client ?? throw new InvalidOperationException("Client is not initialized.");
    internal VpnServiceContext Context { get; }

    public VpnServiceHost(
        string configFolder,
        IVpnServiceHandler vpnServiceHandler,
        ISocketFactory socketFactory,
        bool withLogger = true)
    {
        Context = new VpnServiceContext(configFolder);
        _socketFactory = socketFactory;
        _vpnServiceHandler = vpnServiceHandler;

        // initialize logger
        _logService = withLogger ? new LogService(Context.LogFilePath) : null;
        VhLogger.TcpCloseEventId = GeneralEventId.Stream;
        var clientOptions = Context.TryReadClientOptions();
        if (_logService != null && clientOptions != null) {
            _logService.Start(clientOptions.LogServiceOptions);
        }

        // start apiController
        _apiController = new ApiController(this);
        VhLogger.Instance.LogInformation("VpnServiceHost has been initiated...ApiEndPoint: {_apiController}",
            _apiController.ApiEndPoint);
    }

    private void VpnHoodClient_StateChanged(object? sender, EventArgs e)
    {
        var client = (VpnHoodClient?)sender;
        if (client == null)
            return;

        // update last sate
        VhLogger.Instance.LogDebug("VpnService update the connection info file. State:{State}, LastError: {LastError}",
            client.State, client.LastException?.Message);
        _ = UpdateConnectionInfo(client, _connectCts.Token);

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

    public async Task<bool> TryConnect(bool forceReconnect = false)
    {
        if (_isDisposed == 1)
            return false;

        try {
            _disconnectRequested = false;

            // handle previous client
            var client = Client;
            if (!forceReconnect && client is { State: ClientState.Connected or ClientState.Connecting or ClientState.Waiting }) {
                // user must disconnect first
                VhLogger.Instance.LogWarning("VpnService connection is already in progress.");
                await UpdateConnectionInfo(client, _connectCts.Token).Vhc();
                return false;
            }

            // cancel previous connection if exists
            await _connectCts.TryCancelAsync();
            _connectCts.Dispose();

            if (client != null) {
                VhLogger.Instance.LogWarning("VpnService killing the previous connection.");

                // this prevents the previous connection to overwrite the state or stop the service
                client.StateChanged -= VpnHoodClient_StateChanged;

                // ReSharper disable once MethodHasAsyncOverload
                // Don't call disposeAsync here. We don't need graceful shutdown.
                // Graceful shutdown should be handled by disconnect or by client itself.
                client.Dispose();
                Client = null;
            }

            // start connecting 
            _connectCts = new CancellationTokenSource();
            await Connect(_connectCts.Token);
            return true;
        }
        catch (Exception ex) {
            if (!_disconnectRequested)
                VhLogger.Instance.LogError(ex, "VpnServiceHost could not establish the connection.");

            return false;
        }
    }

    private async Task Connect(CancellationToken cancellationToken)
    {
        try {
            // read client options and start log service
            var clientOptions = Context.ReadClientOptions();
            _logService?.Start(clientOptions.LogServiceOptions, deleteOldReport: false);

            // restart the log service
            VhLogger.Instance.LogInformation("VpnService is connecting... ProcessId: {ProcessId}", Process.GetCurrentProcess().Id);

            // create a connection info for notification
            await UpdateConnectionInfo(ClientState.Initializing, sessionName: clientOptions.SessionName,
                exception: null, cancellationToken: cancellationToken).Vhc();

            // show notification as soon as possible
            _vpnServiceHandler.ShowNotification(Context.ConnectionInfo);

            // sni is sensitive, must be explicitly enabled
            clientOptions.ForceLogSni |=
                clientOptions.LogServiceOptions.LogEventNames.Contains(nameof(GeneralEventId.Sni),
                    StringComparer.OrdinalIgnoreCase);

            // create tracker
            var trackerFactory = TryCreateTrackerFactory(clientOptions.TrackerFactoryAssemblyQualifiedName);
            var tracker = trackerFactory?.TryCreateTracker(new TrackerCreateParams {
                ClientId = clientOptions.ClientId,
                ClientVersion = clientOptions.Version,
                Ga4MeasurementId = clientOptions.Ga4MeasurementId,
                UserAgent = clientOptions.UserAgent
            });

            // create client
            VhLogger.Instance.LogDebug("VpnService is creating a new VpnHoodClient.");
            var adapterSetting = new VpnAdapterSettings {
                AdapterName = clientOptions.AppName,
                Blocking = false,
                AutoDisposePackets = true
            };

            var client = new VpnHoodClient(
                vpnAdapter: clientOptions.UseNullCapture
                    ? new NullVpnAdapter(autoDisposePackets: true, blocking: false)
                    : _vpnServiceHandler.CreateAdapter(adapterSetting, clientOptions.DebugData1),
                tracker: tracker,
                socketFactory: _socketFactory,
                options: clientOptions
            );
            client.StateChanged += VpnHoodClient_StateChanged;
            Client = client;

            // show notification.
            await UpdateConnectionInfo(client, cancellationToken).Vhc();
            _vpnServiceHandler.ShowNotification(Context.ConnectionInfo);

            // let connect in the background
            // ignore cancellation because it will be cancelled by disconnect or dispose
            await client.Connect(cancellationToken).Vhc();
        }
        catch (Exception ex) {
            await UpdateConnectionInfo(ClientState.Disposed, null, ex, _connectCts.Token).Vhc();
            _vpnServiceHandler.StopNotification();
            _vpnServiceHandler.StopSelf();
            throw;
        }
    }

    public async Task UpdateConnectionInfo(VpnHoodClient client, CancellationToken cancellationToken)
    {
        var connectionInfo = new ConnectionInfo {
            CreatedTime = FastDateTime.Now,
            SessionName = client.Settings.SessionName,
            SessionInfo = client.SessionInfo,
            SessionStatus = client.SessionStatus?.ToDto(),
            ClientState = client.State,
            Error = client.LastException?.ToApiError(),
            ApiEndPoint = _apiController.ApiEndPoint,
            ApiKey = _apiController.ApiKey
        };

        await Context.TryWriteConnectionInfo(connectionInfo, cancellationToken);
    }

    public async Task UpdateConnectionInfo(ClientState clientState, string? sessionName, Exception? exception,
        CancellationToken cancellationToken)
    {
        var connectionInfo = new ConnectionInfo {
            ApiEndPoint = _apiController.ApiEndPoint,
            ApiKey = _apiController.ApiKey,
            SessionInfo = null,
            SessionStatus = null,
            CreatedTime = DateTime.Now,
            ClientState = clientState,
            Error = exception?.ToApiError(),
            SessionName = sessionName
        };

        await Context.TryWriteConnectionInfo(connectionInfo, cancellationToken);
    }


    private static ITrackerFactory? TryCreateTrackerFactory(string? assemblyQualifiedName)
    {
        if (string.IsNullOrEmpty(assemblyQualifiedName))
            return null;

        try {
            var type = Type.GetType(assemblyQualifiedName);
            if (type == null)
                return null;

            var trackerFactory = Activator.CreateInstance(type) as ITrackerFactory;
            return trackerFactory;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not create tracker factory. ClassName: {className}", assemblyQualifiedName);
            return null;
        }
    }

    public async Task TryDisconnect()
    {
        if (_isDisposed == 1)
            return;

        try {
            _disconnectRequested = true;

            // let dispose in the background
            var client = Client;
            if (client != null)
                await client.DisposeAsync();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not disconnect the client.");
        }
    }

    public void Dispose()
    {
        if (Interlocked.Exchange(ref _isDisposed, 1) == 1)
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

