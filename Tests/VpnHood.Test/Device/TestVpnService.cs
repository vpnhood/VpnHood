using Ga4.Trackers;
using VpnHood.Core.Client.Abstractions;
using VpnHood.Core.Client.Device.Adapters;
using VpnHood.Core.Client.VpnServicing;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Device;

public class TestVpnService
    : IVpnServiceHandler, IAsyncDisposable
{
    private readonly Func<IVpnAdapter> _vpnAdapterFactory;
    private readonly ITracker? _tracker;
    private readonly VpnHoodService _vpnHoodService;
    public bool IsDisposed { get; private set; }

    // config folder should be read from static place in read environment, because service can be started independently
    public TestVpnService(
        string configFolder,
        Func<IVpnAdapter> vpnAdapterFactory,
        ITracker? tracker)
    {
        _vpnAdapterFactory = vpnAdapterFactory;
        _tracker = tracker;
        _vpnHoodService = new VpnHoodService(configFolder, this, new TestSocketFactory());
    }

    // it is not async to simulate real environment
    public void OnConnect()
    {
        _vpnHoodService.Connect();
    }

    public void OnStop()
    {
        _vpnHoodService.Disconnect();
    }

    public ITracker? CreateTracker()
    {
        return _tracker;
    }

    public IVpnAdapter CreateAdapter()
    {
        return _vpnAdapterFactory();
    }

    public void ShowNotification(ConnectionInfo connectionInfo)
    {
    }

    public void StopNotification()
    {
    }

    public void StopSelf()
    {
        _ = DisposeAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (IsDisposed) return;
        await _vpnHoodService.DisposeAsync();
        IsDisposed = true;
    }
}