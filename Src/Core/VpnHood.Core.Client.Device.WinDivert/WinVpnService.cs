using Ga4.Trackers;
using VpnHood.Core.Tunneling.Factory;

namespace VpnHood.Core.Client.Device.WinDivert;

public class WinVpnService(string configFolder, ITracker? tracker) : IAsyncDisposable
{
    private VpnHoodService? _vpnHoodService;
    public bool IsRunning => _vpnHoodService != null;
    public async Task Start()
    {
        if (_vpnHoodService!=null)
            await _vpnHoodService.DisposeAsync();

        // create the vpnhood service
        var serviceContext = new VpnHoodServiceContext(configFolder);
        _vpnHoodService = VpnHoodService.Create(serviceContext, new WinDivertVpnAdapter(), new SocketFactory(), tracker: tracker);
        _vpnHoodService.Disposed += (sender, args) => _ = DisposeAsync();

        // start the service
        _ = _vpnHoodService.Client.Connect(CancellationToken.None); 
    }

    public async ValueTask DisposeAsync()
    {
        if(_vpnHoodService!=null)
            await _vpnHoodService.DisposeAsync(false); // not wait if service forced to stop

        _vpnHoodService = null;
    }
}