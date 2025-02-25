using Microsoft.Extensions.Logging;
using System.Diagnostics;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.Adapters;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Tracking;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Tunneling;
using VpnHood.Core.Tunneling.Sockets;

namespace VpnHood.Core.Client.VpnServices.Host;

public class VpnServiceHost : IAsyncDisposable
{
    private readonly ApiController _apiController;
    private readonly IVpnServiceHandler _vpnServiceHandler;
    private readonly ISocketFactory _socketFactory;
    private readonly LogService? _logService;
    private bool _isDisposed;

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
        _apiController = new ApiController(this);
        _logService = withLogger ? new LogService(Context.LogFilePath) : null;
        VhLogger.TcpCloseEventId = GeneralEventId.TcpLife;

        // write initial state including api endpoint and key
        _ = Context.WriteConnectionInfo(BuildConnectionInfo(ClientState.None, null));
        VhLogger.Instance.LogDebug("VpnServiceHost has been initiated...");
    }

    private void VpnHoodClient_StateChanged(object sender, EventArgs e)
    {
        var client = (VpnHoodClient)sender;
        if (client != Client)
            return;

        // update last sate
        VhLogger.Instance.LogDebug("VpnService update the connection info file. State:{State}, LastError: {LastError}",
            client.State, client.LastException?.Message);
        _ = Context.WriteConnectionInfo(client.ToConnectionInfo(_apiController));

        // no client in progress, let's stop the service
        // handler is responsible to dispose this service
        if (client.State is ClientState.Disposed or ClientState.Disconnecting) {
            _vpnServiceHandler.StopNotification();
        }
        else {
            _vpnServiceHandler.ShowNotification(client.ToConnectionInfo(_apiController));
        }
    }

    private void VpnHoodClient_StateChangedForDisposal(object sender, EventArgs e)
    {
        var client = (VpnHoodClient)sender;
        if (client.State != ClientState.Disposed) return;
        client.StateChanged -= VpnHoodClient_StateChangedForDisposal;

        // let the service stop if there is no client
        Task.Delay(10000).ContinueWith(_ => {
            if (Client == null)
                _vpnServiceHandler.StopSelf();
        });
    }

    private readonly object _connectLock = new();
    public bool Connect()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VpnServiceHost));

        lock (_connectLock) {
            VhLogger.Instance.LogDebug("VpnService is connecting... ProcessId: {ProcessId}", Process.GetCurrentProcess().Id);

            // handle previous client
            var client = Client;
            if (client != null) {
                if (client is { State: ClientState.Connected or ClientState.Connecting or ClientState.Waiting }) {
                    VhLogger.Instance.LogWarning("VpnService connection is already in progress.");
                    return true; // user must disconnect first
                }

                // before VpnHoodClient disposed, don't let the old connection overwrite the state or stop the service
                client.StateChanged -= VpnHoodClient_StateChanged;
                _ = client.DisposeAsync(); //let dispose in the background
                Client = null;
            }

            // create service
            try {
                // read client options and start log service
                var clientOptions = Context.ReadClientOptions();
                _logService?.Start(clientOptions.LogOptions);
                
                // sni is sensitive, must be explicitly enabled
                clientOptions.ForceLogSni |=
                    clientOptions.LogOptions.LogEventNames.Contains(nameof(GeneralEventId.Sni), StringComparer.OrdinalIgnoreCase);

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
                Client = new VpnHoodClient(
                    vpnAdapter: clientOptions.UseNullCapture ? new NullVpnAdapter() : _vpnServiceHandler.CreateAdapter(),
                    tracker: tracker,
                    socketFactory: _socketFactory,
                    options: clientOptions
                );
                Client.StateChanged += VpnHoodClient_StateChanged;
                Client.StateChanged += VpnHoodClient_StateChangedForDisposal;

                // show notification. start foreground service
                _vpnServiceHandler.ShowNotification(Client.ToConnectionInfo(_apiController));

                // let connect in the background
                // ignore cancellation because it will be cancelled by disconnect or dispose
                _ = Client.Connect(CancellationToken.None);
                return true;
            }
            catch (Exception ex) {
                _ = Context.WriteConnectionInfo(BuildConnectionInfo(ClientState.Disposed, ex));
                _vpnServiceHandler.StopNotification();
                _vpnServiceHandler.StopSelf();
                return false;
            }
        }
    }

    private ConnectionInfo BuildConnectionInfo(ClientState clientState, Exception? ex)
    {
        var connectionInfo = new ConnectionInfo {
            SessionInfo = null,
            SessionStatus = null,
            ApiEndPoint = _apiController.ApiEndPoint,
            ApiKey = _apiController.ApiKey,
            ClientState = clientState,
            Error = ex?.ToApiError()
        };

        return connectionInfo;
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

    public void Disconnect()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VpnServiceHost));

        // let dispose in the background
        _ = Client?.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        VhLogger.Instance.LogDebug("VpnService Host is destroying...");
        if (_isDisposed) return;

        // dispose client
        var client = Client;
        if (client != null) {
            await client.DisposeAsync();
            client.StateChanged -= VpnHoodClient_StateChanged; // after VpnHoodClient disposed
        }

        // dispose api controller
        _apiController.Dispose();
        VhLogger.Instance.LogDebug("VpnService has been destroyed.");
        _logService?.Dispose();
        _isDisposed = true;
    }
}

