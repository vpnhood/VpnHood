using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.WindowsVpnPlugin;
using Windows.Networking.Vpn;

namespace VpnHood.Core.Client.Devices.WindowsVpnPlugin;

// The VPN service in the VPN host: the plug-in's callbacks Windows makes when the profile connects, and
// the handler of the service host it makes for each connect, as IosVpnService is both on iOS. Not
// IVpnPlugIn itself: Windows needs the C#/WinRT component's callable wrapper, which a plain library's
// class lacks - it compiles, then activation fails - so the component's bridge is what Windows calls, and
// it forwards here. One per VPN host process, which the bridge keeps.
public sealed class WindowsVpnPluginService : IVpnServiceHandler
{
    private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(60);
    private static readonly TimeSpan DisconnectTimeout = TimeSpan.FromSeconds(15);
    private readonly Lock _hostLock = new();
    private VpnServiceHost? _host;
    private volatile VpnChannel? _channel;
    private volatile WindowsVpnPluginAdapter? _adapter;
    private volatile TaskCompletionSource<WindowsVpnPluginAdapter> _adapterTcs = NewAdapterTcs();
    private int _terminationRequested;

    public void Connect(VpnChannel channel)
    {
        try {
            VhLogger.Instance.LogInformation("Windows asked the VPN plug-in to connect.");
            Interlocked.Exchange(ref _terminationRequested, 0);
            var host = GetHost();
            _channel = channel;
            if (_adapterTcs.Task.IsCompleted)
                _adapterTcs = NewAdapterTcs();
            _adapter = null;

            var connectTask = host.TryConnect(forceReconnect: true);
            var startedTask = WaitForChannelStart(_adapterTcs.Task);

            // IVpnPlugIn.Connect is a synchronous contract: StartWithMainTransport must be called before it
            // returns, so the callback thread waits - but on completion events, never a poll.
            var stopwatch = Stopwatch.StartNew();
            var finishedIndex = Task.WaitAny([startedTask, connectTask], ConnectTimeout);
            if (finishedIndex == -1)
                throw new TimeoutException("The VPN adapter did not start in time.");

            if (finishedIndex == 1) {
                // the client session settled first; false means it could not connect
                if (!connectTask.Result)
                    throw new InvalidOperationException("The VPN could not establish its client session.");

                var remaining = ConnectTimeout - stopwatch.Elapsed;
                if (remaining < TimeSpan.Zero)
                    remaining = TimeSpan.Zero;
                if (!startedTask.Wait(remaining))
                    throw new TimeoutException("The VPN adapter did not start in time.");
            }

            // an adapter's start failure surfaces here; a canceled start means the adapter was closed
            startedTask.GetAwaiter().GetResult();
            VhLogger.Instance.LogInformation("The VPN adapter and the Windows VPN channel started.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The Windows VPN plug-in could not connect.");
            TryDisconnect();
            channel.TerminateConnection(ex.Message);
        }
    }

    public void Disconnect(VpnChannel channel)
    {
        VhLogger.Instance.LogInformation("Windows asked the VPN plug-in to disconnect.");
        _adapter?.NotifyOsStopping();
        TryDisconnect();
    }

    public void Encapsulate(VpnChannel channel, VpnPacketBufferList packets, VpnPacketBufferList encapsulatedPackets)
    {
        // An exception escaping this WinRT callback takes down the VPN host.
        try {
            // Without an adapter the buffers stay in the source list, where Windows takes them back:
            // appended to encapsulatedPackets, they would leave through the channel's UDP socket.
            _adapter?.OnEncapsulate(packets, encapsulatedPackets);
        }
        catch (Exception ex) {
            // A callback failure means packet ownership or the adapter's session is no longer trustworthy:
            // one visible channel transition, rather than Connected while every callback fails and the
            // traffic silently freezes.
            if (Interlocked.Exchange(ref _terminationRequested, 1) == 0) {
                VhLogger.Instance.LogError(ex, "Windows VPN encapsulation failed; terminating the channel.");
                try {
                    channel.TerminateConnection("VPN packet processing failed.");
                }
                catch (Exception terminateException) {
                    VhLogger.Instance.LogError(terminateException, "Could not terminate the failed Windows VPN channel.");
                }
            }
        }
    }

    // VpnHood injects the decrypted packets through the channel's receive buffers itself; the associated
    // transport carries no payload, so there is nothing to decapsulate.
    public void Decapsulate(VpnChannel channel, VpnPacketBuffer encapsulatedBuffer,
        VpnPacketBufferList decapsulatedPackets, VpnPacketBufferList controlPacketsToSend)
    {
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        var channel = _channel ?? throw new InvalidOperationException("Windows has not supplied a VpnChannel.");
        var adapter = new WindowsVpnPluginAdapter(channel, adapterSettings);
        _adapter = adapter;
        _adapterTcs.TrySetResult(adapter);
        return adapter;
    }

    public VpnHoodClientFactory CreateClientFactory() => new();

    // Windows shows the VPN's state itself.
    public void ShowNotification(ConnectionInfo connectionInfo)
    {
    }

    public void StopNotification()
    {
    }

    // The service host is done with this connection; the next connect makes a new one.
    public void StopSelf()
    {
        VpnServiceHost? host;
        lock (_hostLock) {
            host = _host;
            _host = null;
        }

        _adapterTcs.TrySetCanceled();
        _adapter = null;
        _channel = null;
        host?.Dispose();
    }

    private VpnServiceHost GetHost()
    {
        lock (_hostLock) {
            // withLogger: the VPN host is a process of its own, so the service host keeps its own log file
            var folder = WindowsVpnPluginFolders.VpnServiceFolder;
            return _host ??= new VpnServiceHost(
                configFolder: folder,
                dataFolder: folder,
                vpnServiceHandler: this,
                socketFactory: new MsQuicSocketFactory(),
                messageListener: new WindowsVpnPluginPipeListener(),
                withLogger: true);
        }
    }

    // The wait is required: the background task's deferral completes right after this callback returns, and
    // Windows may then suspend the host mid-teardown. Bounded, so a hung disconnect never holds Windows'
    // callback thread for good.
    private void TryDisconnect()
    {
        VpnServiceHost? host;
        lock (_hostLock)
            host = _host;

        if (host is null)
            return;

        try {
            if (!host.TryDisconnect().Wait(DisconnectTimeout))
                VhLogger.Instance.LogWarning("The disconnect did not finish in time; returning to Windows.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The Windows VPN plug-in could not disconnect.");
        }
    }

    private static async Task WaitForChannelStart(Task<WindowsVpnPluginAdapter> adapterTask)
    {
        var adapter = await adapterTask.Vhc();
        await adapter.Started.Vhc();
    }

    private static TaskCompletionSource<WindowsVpnPluginAdapter> NewAdapterTcs() =>
        new(TaskCreationOptions.RunContinuationsAsynchronously);
}
