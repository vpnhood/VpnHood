using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.WinDivert;
using VpnHood.Net.VpnAdapters.WinTun;

namespace VpnHood.Core.Client.Devices.WindowsTun;

public class WindowsVpnService : IVpnServiceHandler, IDisposable
{
    private readonly VpnServiceHost _vpnServiceHost;
    private bool _disposed;
    public bool IsDisposed => _disposed;

    public WindowsVpnService(
        string configFolder)
    {
        _vpnServiceHost = new VpnServiceHost(
            configFolder,
            dataFolder: configFolder,
            vpnServiceHandler: this,
            socketFactory: new MsQuicSocketFactory(),
            messageListener: new TcpMessageListener(configFolder),
            withLogger: false,
            tunnelLock: new WindowsVpnTunnelLock());
    }

    public void OnConnect()
    {
        _ = _vpnServiceHost.TryConnect();
    }

    public void OnDisconnect()
    {
        _ = _vpnServiceHost.TryDisconnect();
    }

    public VpnHoodClientFactory CreateClientFactory()
    {
        return new VpnHoodClientFactory();
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        IVpnAdapter vpnAdapter = debugData?.Contains("/windivert", StringComparison.OrdinalIgnoreCase) is true
            ? new WinDivertVpnAdapter(new WinDivertVpnAdapterSettings {
                AdapterName = adapterSettings.AdapterName,
                AutoRestart = adapterSettings.AutoRestart,
                MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
                Blocking = adapterSettings.Blocking,
                AutoDisposePackets = adapterSettings.AutoDisposePackets,
                QueueCapacity = adapterSettings.QueueCapacity,
                ExcludeLocalNetwork = true
            })
            : new WinTunVpnAdapter(new WinVpnAdapterSettings {
                AdapterName = adapterSettings.AdapterName,
                AutoRestart = adapterSettings.AutoRestart,
                MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
                Blocking = adapterSettings.Blocking,
                AutoDisposePackets = adapterSettings.AutoDisposePackets,
                AutoMetric = adapterSettings.AutoMetric,
                QueueCapacity = adapterSettings.QueueCapacity
            });

        return vpnAdapter;
    }

    public void ShowNotification(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
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
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _vpnServiceHost.Dispose();
    }
}