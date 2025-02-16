using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client.VpnServices;

public class VpnHoodService : IAsyncDisposable
{
    private readonly ApiController _apiController;
    private readonly IVpnServiceHandler _vpnServiceHandler;
    private readonly ISocketFactory _socketFactory;
    private bool _isDisposed;

    internal VpnHoodClient? Client { get; private set; }
    internal VpnHoodClient RequiredClient => Client ?? throw new InvalidOperationException("Client is not initialized.");
    internal VpnHoodServiceContext Context { get; }

    public VpnHoodService(
        string configFolder,
        IVpnServiceHandler vpnServiceHandler,
        ISocketFactory socketFactory)
    {
        Context = new VpnHoodServiceContext(configFolder);
        _socketFactory = socketFactory;
        _vpnServiceHandler = vpnServiceHandler;
        _apiController = new ApiController(this);
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
            _vpnServiceHandler.StopNotification();
            _vpnServiceHandler.StopSelf();
        }
    }


    private readonly object _connectLock = new();
    public bool Connect()
    {
        if (_isDisposed)
            throw new ObjectDisposedException(nameof(VpnHoodService));

        lock (_connectLock) {
            VhLogger.Instance.LogTrace("VpnService is connecting...");

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
                VhLogger.Instance.LogTrace("VpnService is creating a new VpnHoodClient.");
                var options = Context.ReadClientOptions();
                Client = new VpnHoodClient(
                    vpnAdapter: options.UseNullCapture ? new NullVpnAdapter() : _vpnServiceHandler.CreateAdapter(),
                    tracker: _vpnServiceHandler.CreateTracker(),
                    socketFactory: _socketFactory,
                    options: Context.ReadClientOptions()
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
            throw new ObjectDisposedException(nameof(VpnHoodService));

        // let dispose in the background
        _ = Client?.DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        VhLogger.Instance.LogTrace("VpnService is destroying...");
        if (_isDisposed) return;

        // dispose client
        var client = Client;
        if (client != null) {
            await client.DisposeAsync();
            client.StateChanged -= VpnHoodClient_StateChanged; // after VpnHoodClient disposed
        }

        // dispose api controller
        _apiController.Dispose();
        VhLogger.Instance.LogTrace("VpnService has been destroyed.");
        _isDisposed = true;
    }
}

