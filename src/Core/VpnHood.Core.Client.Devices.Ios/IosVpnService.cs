using NetworkExtension;
using ObjCRuntime;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Quic.Ios;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.IosTun;

namespace VpnHood.Core.Client.Devices.Ios;

// The Objective-C class name MUST match NSExtensionPrincipalClass in the Extension's Info.plist.
// The host Network Extension may subclass this type and register it under the expected ObjC name.
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

    // os_log subsystem for the host LogService's device sink (IosDeviceLoggerProvider). Read from the running
    // bundle's identifier (inside an extension process, MainBundle is the .appex, so this is the extension's
    // bundle id) so it stays correct for any extension/product that hosts this provider — no hardcoded id.
    // Lets LogToDevice output surface in Console.app / `log stream --device` even though the extension's
    // stdout is /dev/null. The os_log category is taken from each logger's MEL category name.
    private static string OsLogSubsystem => NSBundle.MainBundle.BundleIdentifier ?? "IosVpnService";


    private const string VpnServiceConfigFolderKey = "VpnServiceConfigFolder";

    /// Called by IosVpnAdapter.AdapterOpen after SetTunnelNetworkSettings succeeds (error == null)
    /// or fails (error != null). Safe to call multiple times — only the first call wins.
    public void CompleteStartTunnel(NSError? error)
    {
        if (Interlocked.Exchange(ref _completionFired, true))
            return;
        var handler = _startTunnelCompletionHandler;
        _startTunnelCompletionHandler = null;
        try { handler?.Invoke(error!); } catch { /* ignore */ }
    }

    /// Resolves the VPN service config folder:
    /// 1. ProviderConfiguration["VpnServiceConfigFolder"] — set by the App in IosDevice.StartVpnService.
    ///    This is the App-Group shared-container path; an App-Group container is mounted at the SAME
    ///    absolute path in both the App and Extension processes, so the App passes the resolved path
    ///    verbatim and the Extension does not need the App Group id to re-derive it.
    /// 2. LocalApplicationData — per-process fallback (IPC will NOT work with this).
    public string ResolveConfigFolder()
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

    // Called by the .NET iOS runtime when ObjC creates the extension instance.
    // [Export("init")] is intentionally absent: iOS uses NEPacketTunnelProvider's init,
    // and .NET iOS wraps the resulting ObjC handle here. Adding [Export("init")] caused
    // a null function-pointer in the ObjC dispatch table → EXC_BAD_ACCESS / CODE-SIGNING:
    // Invalid Page at 0x0 inside -[PacketTunnelProvider init].
    protected IosVpnService(NativeHandle handle) : base(handle)
    {
        // Redirect .NET Console to null FIRST — before any other Console access.
        // iOS Network Extensions have no stdout reader; any write blocks once the pipe
        // buffer fills. Console.SetOut(TextWriter.Null) must be the first statement.
        Console.SetOut(TextWriter.Null);

        // VhLogger defaults to NullLogger — no console logger runs in this process.
        // Force class init now (safe: fast, no LoggerFactory or Console).
        _ = VhLogger.IsAnonymousMode;
    }

    public override void StartTunnel(
        NSDictionary<NSString, NSObject>? options, Action<NSError> startTunnelCompletionHandler)
    {
        // Active memory systems — REQUIRED, always on (device-measured 2026-06-14): the GC guard keeps the
        // extension under the ~52 MB jetsam limit (without it, it crashes IMMEDIATELY on tunnel start), and
        // installing the memory reader as VhMemory.Instance lets the QUIC download brake read this process's
        // live phys_footprint. These are NOT diagnostics — they run regardless of IosDiagnostics.
        IosMemoryGuard.Start();
        IosMemory.Install();

        // NOTE: IosDiagnostics is applied later, in CreateAdapter — it follows the log level from
        // ClientOptions.LogServiceOptions, which is not known until the host loads vpn.config.

        _startTunnelCompletionHandler = startTunnelCompletionHandler;
        _completionFired = false;

        // NOTE (memory): the iOS-specific small packet/UDP buffer sizes (64 KB) are no longer set
        // here as TunnelDefaults statics. They now flow from the host app via AppOptions →
        // ClientOptions (PacketChannelBufferSize / UdpProxyBufferSize / StreamProxyBufferSize),
        // written into vpn.config and consumed by the client. Keeping these out of the extension
        // means the desktop/Android defaults remain untouched.

        // Resolve the config folder — ProviderConfiguration set by App takes priority.
        var configFolder = ResolveConfigFolder();
        try { Directory.CreateDirectory(configFolder); } catch { /* ignore */ }

        // Kick off heavy VpnHood init off the main thread.
        _ = Task.Run(() => {
            try {
                var sf = new IosSocketFactory();
                // withLogger: true so the host runs the standard LogService (LogToDevice/LogToFile come from
                // the app's LogServiceOptions). deviceLoggerProviderFactory routes LogToDevice to os_log
                // (IosDeviceLoggerProvider) instead of the default Trace sink (which is /dev/null in an extension).
                _vpnServiceHost = new VpnServiceHost(configFolder, this, sf,
                    netFilter: null, withLogger: true, messageListener: _messageListener,
                    deviceLoggerProviderFactory: includeScopes => new IosDeviceLoggerProvider(
                        OsLogSubsystem, includeScopes));
                _ = _vpnServiceHost.TryConnect(true);
            }
            catch (Exception ex) {
                CompleteStartTunnel(new NSError(new NSString("VpnHood"), 1,
                    NSDictionary.FromObjectAndKey((NSString)($"VpnHood init failed: {ex.Message}"), NSError.LocalizedDescriptionKey)));
            }
        });
    }

    public override void HandleAppMessage(NSData? messageData, Action<NSData>? completionHandler)
    {
        // CRITICAL: do NOT block here. iOS may deliver app messages on the provider/main thread, and
        // blocking on ProcessMessageAsync with .GetAwaiter().GetResult() (sync-over-async) can
        // deadlock when the continuation needs that same thread. The completion handler then never
        // fires, so every App<->Extension RPC (status refresh, disconnect) times out — which shows up
        // as a permanent "Connecting" state and broken disconnect/reconnect. Process asynchronously
        // and invoke the completion handler from the continuation (it is safe from any thread).
        _ = HandleAppMessageAsync(messageData, completionHandler);
    }

    private async Task HandleAppMessageAsync(NSData? messageData, Action<NSData>? completionHandler)
    {
        NSData responseData;
        try {
            if (_vpnServiceHost == null || messageData == null) {
                responseData = BuildApiErrorResponse(new InvalidOperationException("VpnServiceHost is not ready."));
            }
            else {
                var responseBytes = await _messageListener
                    .ProcessMessageAsync(messageData.ToArray(), CancellationToken.None)
                    .Vhc();
                responseData = NSData.FromArray(responseBytes);
            }
        }
        catch (Exception ex) {
            responseData = BuildApiErrorResponse(ex);
        }

        try { completionHandler?.Invoke(responseData); } catch { /* ignore */ }
    }

    private static NSData BuildApiErrorResponse(Exception ex)
    {
        var response = new ApiResponse<object> {
            ConnectionInfo = VpnServiceHost.DefaultConnectionInfo,
            ApiError = ex.ToApiError(),
            Result = null
        };
        return NSData.FromArray(StreamUtils.ObjectToJsonBuffer(response).ToArray());
    }

    public override void StopTunnel(NEProviderStopReason reason, Action completionHandler)
    {
        _ = _vpnServiceHost?.TryDisconnect();
        completionHandler();
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        // Apply the iOS diagnostics master switch from the effective log level (set by the host's LogService
        // from ClientOptions.LogServiceOptions just before this call: below Information = diagnostics on),
        // then start the memory probe — a no-op unless just enabled. Re-evaluated on every (re)connect, so
        // toggling "/log:debug" in the app UI turns the diagnostics on and off with it.
        IosDiagnostics.ApplyLogLevel(VhLogger.MinLogLevel);
        IosMemoryMonitor.Start();

        return new IosVpnAdapter(this, new IosVpnAdapterSettings {
            AdapterName = adapterSettings.AdapterName,
            Blocking = adapterSettings.Blocking,
            AutoDisposePackets = adapterSettings.AutoDisposePackets,
            AutoRestart = adapterSettings.AutoRestart,
            MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
            QueueCapacity = adapterSettings.QueueCapacity,
            AutoMetric = adapterSettings.AutoMetric
        }, CompleteStartTunnel);
    }

    public void ShowNotification(ConnectionInfo connectionInfo)
    {
        // iOS does not support foreground notifications and does not need to show one.
    }

    public void StopNotification()
    {
        // iOS does not support foreground notifications and does not need to show one.
    }

    public void StopSelf()
    {
        CancelTunnel(null);
    }
}
