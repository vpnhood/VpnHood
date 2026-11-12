using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Messaging;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.LinuxTun;

namespace VpnHood.Core.Client.VpnServices.Host.Linux;

/// <summary>
/// Represents the VPN service on Linux: the platform's part of its host (<see cref="IVpnServiceHandler"/>), with
/// LinuxTun's adapter, MsQuic, a loopback TCP channel to the app, the machine's tunnel lock and the kill switch.
/// </summary>
internal class LinuxVpnService : IVpnServiceHandler, IDisposable
{
    private readonly VpnServiceHost _vpnServiceHost;
    private readonly TaskCompletionSource _stopped = new(TaskCreationOptions.RunContinuationsAsynchronously);
    private bool _disposed;

    /// <summary>Gets a task that completes when the service ends, with which the VPN host's process ends.</summary>
    public Task Stopped => _stopped.Task;

    /// <summary>Initializes a new instance of the <see cref="LinuxVpnService"/> class, listening to the app at once.</summary>
    /// <param name="configFolder">The VPN service's folder, its config and data folder alike.</param>
    /// <param name="killSwitch">The kill switch's block.</param>
    /// <param name="withLogger">
    /// <see langword="true"/> to keep a log file of its own (<c>vpn.log</c>), where it runs in a process of its own, as
    /// the VPN host.
    /// </param>
    /// <exception cref="System.Net.Sockets.SocketException">It could not listen for the app.</exception>
    /// <exception cref="IOException">It could not publish its channel's file.</exception>
    public LinuxVpnService(string configFolder, IVpnKillSwitch killSwitch, bool withLogger)
    {
        _vpnServiceHost = new VpnServiceHost(
            configFolder: configFolder,
            dataFolder: configFolder,
            vpnServiceHandler: this,
            socketFactory: new ClientSocketFactory(new SystemSocketFactory(), new MsQuicClientFactory()),
            messageListener: new TcpMessageListener(configFolder),
            withLogger: withLogger,
            tunnelLock: new LinuxVpnTunnelLock(),
            killSwitch: killSwitch);
    }

    /// <summary>Starts a connect with the saved options, in the background.</summary>
    public void OnConnect()
    {
        _ = _vpnServiceHost.TryConnect();
    }

    /// <summary>Starts a disconnect, in the background.</summary>
    public void OnDisconnect()
    {
        _ = _vpnServiceHost.TryDisconnect();
    }

    /// <summary>Disconnects: the session's graceful end, as the app's disconnect asks for it.</summary>
    /// <returns>A task that completes once the session is gone; it never throws.</returns>
    public Task TryDisconnect()
    {
        return _vpnServiceHost.TryDisconnect();
    }

    /// <inheritdoc/>
    /// <remarks>It is the default one.</remarks>
    public VpnHoodClientFactory CreateClientFactory()
    {
        return new VpnHoodClientFactory();
    }

    /// <inheritdoc/>
    public bool CanReconnect => true;

    /// <inheritdoc/>
    /// <remarks>It is LinuxTun's, its name made one the kernel takes, as a debug build's is too long.</remarks>
    /// <exception cref="ArgumentOutOfRangeException">The settings' queue capacity is less than 1.</exception>
    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        var vpnAdapter = new LinuxTunVpnAdapter(new LinuxVpnAdapterSettings {
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

    /// <inheritdoc/>
    /// <remarks>Linux shows none here: it does nothing.</remarks>
    public void ShowNotification(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
    }

    /// <inheritdoc/>
    /// <remarks>Linux shows none here: it does nothing.</remarks>
    public void StopNotification()
    {
    }

    /// <inheritdoc/>
    /// <remarks>It disposes the service, which completes <see cref="Stopped"/>.</remarks>
    public void StopSelf()
    {
        Dispose();
    }

    /// <inheritdoc/>
    /// <remarks>It ends the service at once, its host with it, and completes <see cref="Stopped"/>.</remarks>
    public void Dispose()
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        _vpnServiceHost.Dispose();
        _stopped.TrySetResult();
    }
}