using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.ApiControllers;
using VpnHood.Core.Client.Device;
using VpnHood.Core.Common.ApiClients;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client;

public class VpnHoodService : IAsyncDisposable
{
    private readonly ApiController _apiController;
    public VpnHoodServiceContext Context { get; }
    public VpnHoodClient Client { get; }
    public event EventHandler? Disposed;

    private VpnHoodService(VpnHoodServiceContext context, VpnHoodClient client)
    {
        Context = context;
        Client = client;
        _apiController = new ApiController(this);
        client.StateChanged += VpnHoodClient_StateChanged;
    }

    private void VpnHoodClient_StateChanged(object sender, EventArgs e)
    {
        Context.SaveConnectionInfo(Client.ToConnectionInfo(_apiController));
        if (Client.State == ClientState.Disposed)
            _ = DisposeAsync();
    }

    public static VpnHoodService Create(VpnHoodServiceContext serviceContext,
        IVpnAdapter vpnAdapter, ISocketFactory socketFactory, ITracker? tracker)
    {
        try
        {
            // create the client
            var vpnHoodClient = new VpnHoodClient(vpnAdapter, socketFactory, tracker, serviceContext.ReadClientOptions());
            var vpnHoodService = new VpnHoodService(serviceContext, vpnHoodClient);
            serviceContext.SaveConnectionInfo(vpnHoodClient.ToConnectionInfo(vpnHoodService._apiController));
            return vpnHoodService;
        }
        catch (Exception ex)
        {
            serviceContext.SaveConnectionInfo(new ConnectionInfo
            {
                SessionInfo = null,
                SessionStatus = null,
                ApiEndPoint = null,
                ApiKey = null,
                ClientState = ClientState.Disposed,
                Error = ex.ToApiError()
            });
            throw;
        }
    }

    private int _disposed;
    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }

    public async ValueTask DisposeAsync(bool waitForBye)
    {
        if (Interlocked.Exchange(ref _disposed, 1) == 1) 
            return;

        _apiController.Dispose();
        await Client.DisposeAsync(waitForBye);
        Client.StateChanged -= VpnHoodClient_StateChanged; // after VpnHoodClient disposed

        Disposed?.Invoke(this, EventArgs.Empty);
    }

}
