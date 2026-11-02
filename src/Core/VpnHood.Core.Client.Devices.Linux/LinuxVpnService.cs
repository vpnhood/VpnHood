using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.Core.Client.Devices.Linux;

public class LinuxVpnService : IVpnServiceHandler, IDisposable
{
    private readonly VpnServiceHost _vpnServiceHost;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;
    public bool IsDisposed => _disposed;

    // Its end, with which the VPN host's process ends (LinuxVpnHost).
    public Task Stopped => _stopped.Task;

    // withLogger: a log file of its own (vpn.log), where it runs in a process of its own - the VPN host
    public LinuxVpnService(string configFolder, IVpnKillSwitch killSwitch, bool withLogger)
    {
        _vpnServiceHost = new VpnServiceHost(
            configFolder: configFolder,
            dataFolder: configFolder,
            vpnServiceHandler: this,
            socketFactory: new MsQuicSocketFactory(),
            messageListener: new TcpMessageListener(configFolder),
            withLogger: withLogger,
            tunnelLock: new LinuxVpnTunnelLock(),
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

    // The session's graceful end, as the app's disconnect asks for it.
    public Task TryDisconnect()
    {
        return _vpnServiceHost.TryDisconnect();
    }

    public VpnHoodClientFactory CreateClientFactory()
    {
        return new VpnHoodClientFactory();
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        var vpnAdapter = new LinuxTunVpnAdapter(new LinuxVpnAdapterSettings {
            // the app's name as an interface name the kernel takes: a debug build's is too long
            AdapterName = LinuxTunVpnAdapter.GetValidAdapterName(adapterSettings.AdapterName),
            AppId = adapterSettings.AppId,
            AutoRestart = adapterSettings.AutoRestart,
            MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
            Blocking = adapterSettings.Blocking,
            AutoDisposePackets = adapterSettings.AutoDisposePackets,
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