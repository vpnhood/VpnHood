using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Test.Providers;

namespace VpnHood.Test.Device;

public class TestVpnService
    : IVpnServiceHandler, IAsyncDisposable
{
    private readonly Func<VpnAdapterSettings, IVpnAdapter> _vpnAdapterFactory;
    private readonly VpnServiceHost _vpnServiceHost;
    public bool IsDisposed { get; private set; }
    public IVpnAdapter? CurrentVpnAdapter { get; private set; }

    // config folder should be read from static place in read environment, because service can be started independently
    public TestVpnService(
        string configFolder,
        Func<VpnAdapterSettings, IVpnAdapter> vpnAdapterFactory)
    {
        _vpnAdapterFactory = vpnAdapterFactory;
        _vpnServiceHost = new VpnServiceHost(configFolder, this, new TestSocketFactory(), withLogger: false);
    }

    // it is not async to simulate real environment
    public void OnConnect()
    {
        _vpnServiceHost.Connect();
    }

    public void OnStop()
    {
        _vpnServiceHost.Disconnect();
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings)
    {
        CurrentVpnAdapter = _vpnAdapterFactory(adapterSettings);
        CurrentVpnAdapter.Disposed += (_, _) => CurrentVpnAdapter = null;
        return CurrentVpnAdapter;
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
        await _vpnServiceHost.DisposeAsync();
        IsDisposed = true;
    }
}