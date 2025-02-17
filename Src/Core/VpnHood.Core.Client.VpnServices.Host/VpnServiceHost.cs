using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.Adapters;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Sockets;
using VpnHood.Core.Tunneling;

namespace VpnHood.Core.Client.VpnServices.Host;

public class VpnServiceHost : IAsyncDisposable
{
    private readonly ApiController _apiController;
    private readonly IVpnServiceHandler _vpnServiceHandler;
    private readonly ISocketFactory _socketFactory;
    private readonly LogService _logService;
    private bool _isDisposed;

    internal VpnHoodClient? Client { get; private set; }
    internal VpnHoodClient RequiredClient => Client ?? throw new InvalidOperationException("Client is not initialized.");
    internal VpnServiceContext Context { get; }

    public VpnServiceHost(
        string configFolder,
        IVpnServiceHandler vpnServiceHandler,
        ISocketFactory socketFactory)
    {
        Context = new VpnServiceContext(configFolder);
        _socketFactory = socketFactory;
        _vpnServiceHandler = vpnServiceHandler;
        _apiController = new ApiController(this);
        _logService = new LogService(Context.LogFilePath);
        VhLogger.TcpCloseEventId = GeneralEventId.TcpLife;
    }

    private void VpnHoodClient_StateChanged(object sender, EventArgs e)
    {
        var client = Client;
        if (client == null) return;

        // update last sate
        _ = Context.WriteConnectionInfo(client.ToConnectionInfo(_apiController));

        // no client in progress, let's stop the service
        // handler is responsible to dispose this service
        if (client.State != ClientState.Disposed) {
            _vpnServiceHandler.ShowNotification(client.ToConnectionInfo(_apiController));
        }
        else {
            _logService.Stop();
            _vpnServiceHandler.StopNotification();
            _vpnServiceHandler.StopSelf();
        }
    }


    private readonly object _connectLock = new();
    public bool Connect()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VpnServiceHost));

        lock (_connectLock) {
            VhLogger.Instance.LogDebug("VpnService is connecting...");

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
            }

            // create service
            try {
                // read client options and start log service
                var clientOptions = Context.ReadClientOptions();
                _logService.Start(clientOptions.LogOptions);

                // sni is sensitive, must be explicitly enabled
                clientOptions.ForceLogSni |=
                    clientOptions.LogOptions.LogEventNames.Contains(nameof(GeneralEventId.Sni), StringComparer.OrdinalIgnoreCase);

                // create client
                VhLogger.Instance.LogDebug("VpnService is creating a new VpnHoodClient.");
                Client = new VpnHoodClient(
                    vpnAdapter: clientOptions.UseNullCapture ? new NullVpnAdapter() : _vpnServiceHandler.CreateAdapter(),
                    tracker: _vpnServiceHandler.CreateTracker(),
                    socketFactory: _socketFactory,
                    options: clientOptions
                );
                Client.StateChanged += VpnHoodClient_StateChanged;

                // show notification. start foreground service
                _vpnServiceHandler.ShowNotification(Client.ToConnectionInfo(_apiController));

                // let connect in the background
                // ignore cancellation because it will be cancelled by disconnect or dispose
                _ = Client.Connect(CancellationToken.None);
                return true;
            }
            catch (Exception ex) {
                _ = Context.WriteConnectionInfo(new ConnectionInfo {
                    SessionInfo = null,
                    SessionStatus = null,
                    ApiEndPoint = null,
                    ApiKey = null,
                    ClientState = ClientState.Disposed,
                    Error = ex.ToApiError()
                });

                _vpnServiceHandler.StopNotification();
                _vpnServiceHandler.StopSelf();
                return false;
            }
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
        VhLogger.Instance.LogDebug("VpnService is destroying...");
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
        _isDisposed = true;
    }
}

