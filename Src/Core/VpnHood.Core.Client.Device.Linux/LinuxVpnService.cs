using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.LinuxTun;

namespace VpnHood.Core.Client.Device.Linux;

public class LinuxVpnService : IVpnServiceHandler, IDisposable
{
    private readonly VpnServiceHost _vpnServiceHost;
    public bool IsDisposed { get; private set; }

    public LinuxVpnService(
        string configFolder)
    {
        _vpnServiceHost = new VpnServiceHost(configFolder, this, new SocketFactory(), withLogger: false);
    }

    public void OnConnect()
    {
        _ = _vpnServiceHost.TryConnect();
    }

    public void OnDisconnect()
    {
        _ = _vpnServiceHost.TryDisconnect();
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        var vpnAdapter = new LinuxTunVpnAdapter(new LinuxVpnAdapterSettings
        {
            AdapterName = adapterSettings.AdapterName,
            AutoRestart = adapterSettings.AutoRestart,
            MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
            Blocking = adapterSettings.Blocking,
            AutoDisposePackets = adapterSettings.AutoDisposePackets,
            QueueCapacity = adapterSettings.QueueCapacity
        });

        return vpnAdapter;
    }

    public void ShowNotification(ConnectionInfo connectionInfo)
    {
    }

    public void StopNotification()
    {
    }

    public void StopSelf()
    {
        Dispose();
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        _vpnServiceHost.Dispose();
    }
}