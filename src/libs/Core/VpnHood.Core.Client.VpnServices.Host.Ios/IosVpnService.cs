using Microsoft.Extensions.Logging;
using NetworkExtension;
using ObjCRuntime;
using VpnHood.Core.Client.Devices.Ios.Utils;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;
using VpnHood.Net.Quic.Ios;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Memory;
using VpnHood.Net.Toolkit.Sockets;
using VpnHood.Net.VpnAdapters.Abstractions;
using VpnHood.Net.VpnAdapters.IosTun;
using VpnHood.Core.Client.Devices.Ios;

namespace VpnHood.Core.Client.VpnServices.Host.Ios;

/// <summary>
/// Represents the VPN service on iOS and tvOS: the Network Extension's packet tunnel provider, which runs the VPN
/// service's host and answers the app over the provider's app messages.
/// </summary>
/// <remarks>
/// An extension subclasses it in its own assembly and registers the subclass under the name its Info.plist gives in
/// <c>NSExtensionPrincipalClass</c>, as the heads' <c>PacketTunnelProvider</c> does: under CoreCLR's registrar, an
/// Info.plist that names <c>IosVpnService</c> itself crashes the extension at its launch, as nothing of the extension's
/// own loads this assembly.
/// </remarks>
[Register("IosVpnService")]
public class IosVpnService : NEPacketTunnelProvider, IVpnServiceHandler
{
    // Created in StartTunnel once the config folder is resolved from ProviderConfiguration.
    private VpnServiceHost? _vpnServiceHost;

    // VpnService API transport for iOS: messages flow over NEPacketTunnelProvider's app message channel
    // (HandleAppMessage) instead of a TCP loopback socket.
    private readonly IosMessageListener _messageListener = new();

    // Stored when StartTunnel is called; invoked once IosVpnAdapter applies network settings
    // (iOS terminates the extension if the completion handler runs before SetTunnelNetworkSettings).
    private Action<NSError>? _startTunnelCompletionHandler;
    private bool _completionFired;

    // os_log subsystem for the extension's sink (IosOsLogLoggerProvider). Read from the running bundle's
    // identifier (inside an extension process, MainBundle is the .appex, so this is the extension's
    // bundle id) so it stays correct for any extension/product that hosts this provider — no hardcoded id.
    // The lines surface in Console.app / `log stream --device` even though the extension's stdout is
    // /dev/null. The os_log category is taken from each logger's MEL category name.
    private static string OsLogSubsystem => NSBundle.MainBundle.BundleIdentifier ?? "IosVpnService";


    private const string VpnServiceConfigFolderKey = "VpnServiceConfigFolder";
    private const string VpnServiceDataFolderKey = "VpnServiceDataFolder";

    // Called by IosVpnAdapter.AdapterOpen after SetTunnelNetworkSettings succeeds (error == null)
    // or fails (error != null). Safe to call multiple times — only the first call wins.
    private void CompleteStartTunnel(NSError? error)
    {
        if (Interlocked.Exchange(ref _completionFired, true))
            return;
        var handler = _startTunnelCompletionHandler;
        _startTunnelCompletionHandler = null;
        try { handler?.Invoke(error!); } catch { /* ignore */ }
    }

    // Resolves the VPN service config folder:
    // 1. ProviderConfiguration["VpnServiceConfigFolder"] — set by the App in IosDevice.StartVpnService.
    //    This is the App-Group shared-container path; an App-Group container is mounted at the SAME
    //    absolute path in both the App and Extension processes, so the App passes the resolved path
    //    verbatim and the Extension does not need the App Group id to re-derive it.
    // 2. LocalApplicationData — per-process fallback (IPC will NOT work with this).
    private string ResolveConfigFolder()
    {
        // Path passed explicitly by the App via ProviderConfiguration.
        var proto = ProtocolConfiguration as NETunnelProviderProtocol;
        if (proto?.ProviderConfiguration?[(NSString)VpnServiceConfigFolderKey] is NSString folderStr &&
            !string.IsNullOrEmpty(folderStr))
            return folderStr.ToString();

        // Last resort — IPC will not work between App and Extension with this path.
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vpn-service");
    }

    // The config folder and the proxies' folder, which travels with a backup (IosDevice.VpnServiceDataFolder),
    // passed by the App as the config folder is.
    private (string ConfigFolder, string DataFolder) ResolveFolders()
    {
        var configFolder = ResolveConfigFolder();
        var proto = ProtocolConfiguration as NETunnelProviderProtocol;
        if (proto?.ProviderConfiguration?[(NSString)VpnServiceDataFolderKey] is NSString dataFolder &&
            !string.IsNullOrEmpty(dataFolder))
            return (configFolder, dataFolder.ToString());

#pragma warning disable CS0618 // Type or member is obsolete
        return MigrateFolders(configFolder);
#pragma warning restore CS0618 // Type or member is obsolete
    }

    // iOS keeps the configuration an older App saved until the App connects again, and a VPN switched on
    // from iOS Settings starts with it. It names one folder, the one that is now the proxies'; the
    // service's own files go to the config folder beside it, where IosDevice puts it.
    [Obsolete("Migration (2026-10): drop a few months after it ships, with its call in ResolveFolders.")]
    private static (string ConfigFolder, string DataFolder) MigrateFolders(string sharedFolder)
    {
        var containerPath = Path.GetDirectoryName(sharedFolder) ??
                            throw new InvalidOperationException("The VPN service's folder has no parent folder.");
        var configFolder = Path.Combine(containerPath, IosDevice.VpnServiceConfigFolderName);
        VhLogger.Instance.LogInformation(
            "The VPN configuration is an older app's, with one folder; the service's files go beside it. ConfigFolder: {ConfigFolder}",
            configFolder);
        return (configFolder, sharedFolder);
    }

    // [Export("init")] is intentionally absent: iOS uses NEPacketTunnelProvider's init,
    // and .NET iOS wraps the resulting ObjC handle here. Adding [Export("init")] caused
    // a null function-pointer in the ObjC dispatch table → EXC_BAD_ACCESS / CODE-SIGNING:
    // Invalid Page at 0x0 inside -[PacketTunnelProvider init].
    /// <summary>
    /// Initializes a new instance of the <see cref="IosVpnService"/> class, as the .NET iOS runtime makes it for the
    /// extension's Objective-C object.
    /// </summary>
    /// <param name="handle">The Objective-C object's handle.</param>
    /// <remarks>
    /// It first sends .NET's console output to nothing, as an extension has no reader of its standard output and a
    /// write would block once the pipe fills, then adds Apple's unified log (os_log) as the log's sink, under the
    /// extension's bundle identifier.
    /// </remarks>
    protected IosVpnService(NativeHandle handle) : base(handle)
    {
        // Redirect .NET Console to null FIRST — before any other Console access.
        // iOS Network Extensions have no stdout reader; any write blocks once the pipe
        // buffer fills. Console.SetOut(TextWriter.Null) must be the first statement.
        Console.SetOut(TextWriter.Null);

        // The extension's sink from its first line: os_log, since it has no console. Its log file
        // is the host's (VpnServiceHost), opened at each start request.
        VhLogger.AddProvider(new IosOsLogLoggerProvider(OsLogSubsystem));
    }

    /// <inheritdoc/>
    /// <param name="options">The options iOS starts the tunnel with, which it does not read.</param>
    /// <param name="startTunnelCompletionHandler">
    /// The handler that tells iOS the start's outcome: <see langword="null"/>, or the error it failed with.
    /// </param>
    /// <remarks>
    /// It starts the extension's memory guard, then makes the VPN service's host off the main thread, which connects.
    /// iOS hears of a successful start only once the adapter has applied the tunnel's network settings; it hears of a
    /// failure where the host cannot be made or the settings cannot be applied.
    /// </remarks>
    /// <exception cref="InvalidOperationException">
    /// The VPN service's folder, from an older app's configuration, has no parent folder.
    /// </exception>
    public override void StartTunnel(
        NSDictionary<NSString, NSObject>? options, Action<NSError> startTunnelCompletionHandler)
    {
        // Required memory systems, always on (device-measured 2026-06-14): the GC guard keeps the
        // extension under the ~52 MB jetsam limit — without it, it crashes immediately on tunnel start —
        // and VhMemory.Instance lets the QUIC download brake read this process's live phys_footprint.
        IosMemoryGuard.Start();
        IosMemory.Install();
        IosMemoryMonitor.RegisterCrashLog();

        _startTunnelCompletionHandler = startTunnelCompletionHandler;
        _completionFired = false;

        // Resolve the folders — ProviderConfiguration set by App takes priority. The config folder stays
        // out of backups.
        var (configFolder, dataFolder) = ResolveFolders();
        try {
            IosNoBackupFolder.Create(configFolder);
        }
        catch { /* ignore */ }

        // Kick off heavy VpnHood init off the main thread.
        _ = Task.Run(() => {
            try {
                // memoryScale feeds the QUIC receive-window sizing: when TCP forwarding is off, TCP rides
                // the packet channel (few QUIC streams), so the windows can be doubled under the same
                // memory bound. Read lazily from the host so it reflects the status at each QUIC-client
                // creation (session build), not the state at factory construction.
                var quicClientFactory = new IosQuicClientFactory(memoryScale: () => _vpnServiceHost?.IsTcpForwarding == true ? 1 : 2);
                // withLogger: true - the extension is a process of its own, so the host keeps its own log
                // file, with the settings the app's LogServiceOptions carry
                _vpnServiceHost = new VpnServiceHost(configFolder, dataFolder, this,
                    new ClientSocketFactory(new SystemSocketFactory(), quicClientFactory),
                    withLogger: true, messageListener: _messageListener);
                _ = _vpnServiceHost.TryConnect(true);
            }
            catch (Exception ex) {
                CompleteStartTunnel(new NSError(new NSString(NSBundle.MainBundle.BundleIdentifier ?? nameof(IosVpnService)), 1,
                    NSDictionary.FromObjectAndKey((NSString)($"The VPN could not start: {ex.Message}"), NSError.LocalizedDescriptionKey)));
            }
        });
    }

    /// <inheritdoc/>
    /// <param name="messageData">The app's message.</param>
    /// <param name="completionHandler">The handler that takes the answer to the app.</param>
    /// <remarks>
    /// It hands the message on without blocking, and answers through the completion handler; the answer is
    /// <see langword="null"/> where the service has not started yet or could not handle the message, which the app
    /// reads as an unreachable service.
    /// </remarks>
    public override void HandleAppMessage(NSData? messageData, Action<NSData>? completionHandler)
    {
        // CRITICAL: do NOT block here. iOS may deliver app messages on the provider/main thread, and
        // blocking on ProcessMessageAsync with .GetAwaiter().GetResult() (sync-over-async) can
        // deadlock when the continuation needs that same thread. The completion handler then never
        // fires, so every App<->Extension RPC (status refresh, disconnect) times out — which shows up
        // as a permanent "Connecting" state and broken disconnect/reconnect. The listener processes
        // asynchronously and invokes the completion handler from the continuation.
        _ = _messageListener.ProcessAppMessage(messageData, completionHandler, CancellationToken.None);
    }

    /// <inheritdoc/>
    /// <param name="reason">Why iOS stops the tunnel.</param>
    /// <param name="completionHandler">The handler that tells iOS the stop has ended.</param>
    /// <remarks>
    /// It disconnects, then calls the completion handler. Where iOS stops it for another VPN, a configuration turned off
    /// or removed, a logout or a user switch, the disconnect carries a <see cref="VpnServiceRevokedException"/>, which
    /// the app shows.
    /// </remarks>
    public override void StopTunnel(NEProviderStopReason reason, Action completionHandler)
    {
        VhLogger.Instance.LogWarning("iOS requested StopTunnel. Reason: {Reason}", reason);
        _ = StopTunnelAsync(completionHandler, ToStopException(reason));
    }

    // Android's OnRevoke equivalent: the reasons that mean the tunnel was taken away — by another
    // VPN app (Superceded) or by the user/system acting outside this app (Settings toggles, profile
    // removal, logout/user switch) — must surface in the app, or the disconnect looks like a silent
    // self-cancel. UserInitiated is the app's own stopVPNTunnel and stays a clean disconnect;
    // failure reasons (network loss, provider crash) keep their own error paths.
    private static Exception? ToStopException(NEProviderStopReason reason)
    {
        return reason switch {
            NEProviderStopReason.Superseded or
                NEProviderStopReason.ProviderDisabled or
                NEProviderStopReason.ConfigurationDisabled or
                NEProviderStopReason.ConfigurationRemoved or
                NEProviderStopReason.UserLogout or
                NEProviderStopReason.UserSwitch => new VpnServiceRevokedException(),
            _ => null
        };
    }

    /// <inheritdoc/>
    /// <remarks>It is the default one.</remarks>
    public VpnHoodClientFactory CreateClientFactory()
    {
        return new VpnHoodClientFactory();
    }

    /// <inheritdoc/>
    /// <remarks>The extension's tunnel outlives an adapter: a new one applies its settings to it anew.</remarks>
    public bool CanReconnect => true;

    private async Task StopTunnelAsync(Action completionHandler, Exception? stopException)
    {
        try {
            if (_vpnServiceHost != null)
                await _vpnServiceHost.TryDisconnect(stopException).Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not cleanly stop the iOS VPN tunnel.");
        }
        finally {
            try { completionHandler(); } catch { /* ignore */ }
        }
    }

    /// <inheritdoc/>
    /// <remarks>
    /// It is IosTun's, on this provider, and completes the tunnel's start for iOS once its settings apply. Where debug
    /// logging is on, it also starts the memory diagnostics.
    /// </remarks>
    /// <exception cref="ArgumentOutOfRangeException">The settings' queue capacity is less than 1.</exception>
    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        // Diagnostics hooks — no-ops unless debug logging is on (the gates follow VhLogger.MinLogLevel,
        // set by the host's LogService from vpn.config just before this per-(re)connect call).
        VhTypeTracker.Enabled = IosMemoryMonitor.Enabled;
        IosMemoryMonitor.Start();

        return new IosVpnAdapter(this, new IosVpnAdapterSettings {
            AdapterName = adapterSettings.AdapterName,
            Blocking = adapterSettings.Blocking,
            AutoRestart = adapterSettings.AutoRestart,
            MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
            QueueCapacity = adapterSettings.QueueCapacity,
            AutoMetric = adapterSettings.AutoMetric
        }, CompleteStartTunnel);
    }

    /// <inheritdoc/>
    /// <remarks>iOS has no foreground notification for it: it does nothing.</remarks>
    public void ShowNotification(ConnectionInfo connectionInfo, CancellationToken cancellationToken)
    {
    }

    /// <inheritdoc/>
    /// <remarks>iOS has no foreground notification for it: it does nothing.</remarks>
    public void StopNotification()
    {
    }

    /// <inheritdoc/>
    /// <remarks>It cancels the tunnel from the provider's side.</remarks>
    public void StopSelf()
    {
        CancelTunnel(null);
    }
}
