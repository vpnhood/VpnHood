using System.Diagnostics;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Net.Quic.MsQuic;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.WindowsVpnPlugin;
using Windows.ApplicationModel;
using Windows.ApplicationModel.AppService;
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
    private static readonly TimeSpan KeepAwakeStartTimeout = TimeSpan.FromSeconds(5);
    // a connect Windows makes soon after: again within 5 s after a restart, within 2.6 s after an app's switch
    private static readonly TimeSpan SessionWaitTimeout = TimeSpan.FromSeconds(15);
    private readonly Lock _hostLock = new();
    private VpnServiceHost? _host;
    private volatile WindowsVpnPluginAppServiceListener? _appServiceListener;
    private volatile VpnChannel? _channel;
    private VpnChannel? _connectedChannel; // the connection Windows holds, until its Disconnect
    private volatile WindowsVpnPluginAdapter? _adapter;
    private volatile TaskCompletionSource<WindowsVpnPluginAdapter> _adapterTcs = NewAdapterTcs();
    private int _terminationRequested;
    private int _keepAwakeCount; // the keep-awake processes connected
    private int _connectCount; // Windows' connects so far: a released host's wait tells by it whether one has come

    public void Connect(VpnChannel channel)
    {
        try {
            VhLogger.Instance.LogInformation("Windows asked the VPN plug-in to connect.");
            Interlocked.Increment(ref _connectCount);
            Interlocked.Exchange(ref _terminationRequested, 0);
            Volatile.Write(ref _connectedChannel, null);
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
                if (Task.WaitAny([startedTask], remaining) == -1)
                    throw new TimeoutException("The VPN adapter did not start in time.");
            }

            // the adapter went before it started: the session ended itself, refused by the server, say
            if (startedTask.IsCanceled)
                throw new OperationCanceledException("The VPN session ended before its adapter started.");

            // an adapter's start failure surfaces here
            startedTask.GetAwaiter().GetResult();
            Volatile.Write(ref _connectedChannel, channel);
            VhLogger.Instance.LogInformation("The VPN adapter and the Windows VPN channel started.");

            // before this callback returns, after which Windows may freeze the host; one from an earlier connection
            // of this host still holds
            if (Volatile.Read(ref _keepAwakeCount) == 0 && !StartKeepAwake().Wait(KeepAwakeStartTimeout))
                VhLogger.Instance.LogWarning("The keep-awake process did not start in time.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "The Windows VPN plug-in could not connect.");

            // a session that ended itself has written why; a disconnect could write its end first, with no reason
            if (ex is not OperationCanceledException)
                TryDisconnect();

            channel.TerminateConnection(ex.Message);
        }
    }

    public void Disconnect(VpnChannel channel)
    {
        VhLogger.Instance.LogInformation("Windows asked the VPN plug-in to disconnect.");
        Volatile.Write(ref _connectedChannel, null);
        TryDisconnect();

        // Windows holds the connection until its plug-in stops the channel, as Microsoft's VPN sample does here: RAS
        // ended it 15 s after the plug-in's Disconnect otherwise.
        StopChannel(channel);
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

    // A connection to one of the package's app services, which Windows runs in this process - the app's control channel
    // or the keep-awake process's: served by the session's host, which closes it with the session. The app's, with no
    // session yet, waits for one (GetWaitingListener); the keep-awake process's is closed at once, so it leaves.
    public async Task ServeAppConnection(AppServiceConnection connection, string appServiceName,
        CancellationToken cancellationToken)
    {
        if (appServiceName != WindowsVpnPluginKeepAwake.AppServiceName) {
            var appListener = _appServiceListener ?? GetWaitingListener();
            await appListener.Serve(connection, cancellationToken).Vhc();
            return;
        }

        var listener = _appServiceListener;
        if (listener is null) {
            connection.Dispose();
            return;
        }

        Interlocked.Increment(ref _keepAwakeCount);
        try {
            await listener.Serve(connection, cancellationToken).Vhc();
        }
        finally {
            // gone while the VPN is connected - killed, or ended by Windows: another takes its place
            if (Interlocked.Decrement(ref _keepAwakeCount) == 0 && Volatile.Read(ref _connectedChannel) is not null)
                await StartKeepAwake().Vhc();
        }
    }

    // A VPN host the app's connection started before a connect: Windows, having lost its VPN host, connects again within
    // seconds, and the app's requests wait for that session rather than find none, as they did after a restart. The
    // listener takes the connection at once, so no request comes before it; only then is it known whether a connect comes.
    private WindowsVpnPluginAppServiceListener GetWaitingListener()
    {
        lock (_hostLock) {
            if (_appServiceListener is { } listener)
                return listener;

            listener = new WindowsVpnPluginAppServiceListener();
            _appServiceListener = listener;
            _ = ExpireWaitingListener(listener);
            return listener;
        }
    }

    // Closes a waiting listener's connections unless a connect has come: at once when Windows holds the package's profile
    // disconnected, else in SessionWaitTimeout.
    private async Task ExpireWaitingListener(WindowsVpnPluginAppServiceListener listener)
    {
        var profiles = await TryGetLiveProfiles().Vhc();
        VhLogger.Instance.LogInformation(
            "The app connected before a connect. Profiles connected or connecting: {Count}", profiles?.Length);
        if (profiles is not { Length: 0 })
            await Task.Delay(SessionWaitTimeout).Vhc();

        lock (_hostLock) {
            if (_host is not null || _appServiceListener != listener)
                return;

            _appServiceListener = null;
        }

        VhLogger.Instance.LogInformation("No connect came for the app's connection; closing it.");
        listener.Dispose();
    }

    // The package's profiles Windows holds other than disconnected; null when Windows does not say.
    internal static async Task<VpnPlugInProfile[]?> TryGetLiveProfiles()
    {
        try {
            var packageFamilyName = Package.Current.Id.FamilyName;
            var profiles = await new VpnManagementAgent().GetProfilesAsync().AsTask().Vhc();
            return profiles.OfType<VpnPlugInProfile>()
                .Where(x =>
                    string.Equals(x.VpnPluginPackageFamilyName, packageFamilyName, StringComparison.OrdinalIgnoreCase) &&
                    x.ConnectionStatus != VpnManagementConnectionStatus.Disconnected)
                .ToArray();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not read the package's VPN profiles from Windows.");
            return null;
        }
    }

    // Never throws: without it the VPN still works while the app runs.
    private static async Task StartKeepAwake()
    {
        try {
            VhLogger.Instance.LogInformation("Starting the keep-awake process...");
            await WindowsVpnPluginKeepAwake.Start().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex,
                "Could not start the keep-awake process, so Windows freezes the VPN host between VPN events while the app is not running.");
        }
    }

    // The service host is done with this connection. A connection Windows still holds - the session ended on the app's
    // request or the server's - ends through Windows' own disconnect, which then runs Disconnect: a channel the plug-in
    // stops by itself is a dropped connection, which Windows dials again.
    public void StopSelf()
    {
        VpnServiceHost? host;
        lock (_hostLock)
            host = _host;

        _adapterTcs.TrySetCanceled();
        _adapter = null;
        _channel = null;
        var connectedChannel = Interlocked.Exchange(ref _connectedChannel, null);
        _ = Release(host, connectedChannel, Volatile.Read(ref _connectCount));
    }

    // The host goes once Windows has disconnected - the app's connection it serves keeps this process awake, which
    // Windows freezes between VPN events otherwise, and the disconnect waited 7 s for the next one - and SessionWaitTimeout
    // after. A connect meanwhile keeps it, with the app's connection and the keep-awake process's: the app's switch to
    // another server, which Windows connected 2.6 s after its disconnect once, found them closed and took 11.5 s.
    private async Task Release(VpnServiceHost? host, VpnChannel? connectedChannel, int connectCount)
    {
        if (connectedChannel is not null)
            await DisconnectProfile(connectedChannel).Vhc();

        if (host is null)
            return;

        await Task.Delay(SessionWaitTimeout).Vhc();
        lock (_hostLock) {
            if (_host != host || Volatile.Read(ref _connectCount) != connectCount)
                return;

            _host = null;
            _appServiceListener = null;
        }

        host.Dispose();
    }

    // Windows refusing leaves the channel to stop here: a dialed-again connection rather than one with no session.
    private static async Task DisconnectProfile(VpnChannel channel)
    {
        try {
            VhLogger.Instance.LogInformation("Asking Windows to disconnect the VPN profile...");
            var connectedProfiles = await TryGetLiveProfiles().Vhc() ??
                                    throw new InvalidOperationException("Windows did not list the package's VPN profiles.");
            if (connectedProfiles.Length == 0)
                throw new InvalidOperationException("Windows lists no connected VPN profile of this package.");

            var agent = new VpnManagementAgent();
            foreach (var profile in connectedProfiles) {
                var status = await agent.DisconnectProfileAsync(profile).AsTask().Vhc();
                if (status is not (VpnManagementErrorStatus.Ok or VpnManagementErrorStatus.AlreadyDisconnecting))
                    throw new InvalidOperationException($"Windows could not disconnect the VPN profile. Status: {status}");
            }

            VhLogger.Instance.LogInformation("Windows has disconnected the VPN profile.");
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Windows did not disconnect the VPN profile, so the plug-in stops its channel.");
            StopChannel(channel);
        }
    }

    private static void StopChannel(VpnChannel channel)
    {
        try {
            channel.Stop();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogWarning(ex, "Could not stop the Windows VPN channel.");
        }
    }

    private VpnServiceHost GetHost()
    {
        lock (_hostLock) {
            if (_host is not null)
                return _host;

            // withLogger: the VPN host is a process of its own, so the service host keeps its own log file
            var folder = WindowsVpnPluginFolders.VpnServiceFolder;
            // the listener an app's connection made while waiting for this session, if any
            var appServiceListener = _appServiceListener ?? new WindowsVpnPluginAppServiceListener();
            _host = new VpnServiceHost(
                configFolder: folder,
                dataFolder: folder,
                vpnServiceHandler: this,
                socketFactory: new MsQuicSocketFactory(),
                messageListener: appServiceListener,
                withLogger: true);
            _appServiceListener = appServiceListener;
            return _host;
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
