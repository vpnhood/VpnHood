using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.WinDivert;
using VpnHood.Net.VpnAdapters.WinTun;

namespace VpnHood.Core.Client.VpnServices.Host.WindowsTun;

public class WindowsVpnService : IVpnServiceHandler, IDisposable
{
    private readonly VpnServiceHost _vpnServiceHost;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    public bool IsDisposed => _disposed;

    // Its end, with which the VPN host's process ends (WindowsVpnHost).
    public Task Stopped => _stopped.Task;

    // withLogger: a log file of its own (vpn.log), where it runs in a process of its own - the VPN host
    public WindowsVpnService(
        string configFolder,
        IVpnKillSwitch killSwitch,
        bool withLogger)
    {
        _vpnServiceHost = new VpnServiceHost(
            configFolder,
            dataFolder: configFolder,
            vpnServiceHandler: this,
            socketFactory: new MsQuicSocketFactory(),
            messageListener: new TcpMessageListener(configFolder),
            withLogger: withLogger,
            tunnelLock: new WindowsVpnTunnelLock(),
            killSwitch: killSwitch);
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

    public bool CanReconnect => true;

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
        _stopped.TrySetResult();
    }
}