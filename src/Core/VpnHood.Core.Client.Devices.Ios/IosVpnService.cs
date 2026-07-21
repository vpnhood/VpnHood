using Microsoft.Extensions.Logging;
using NetworkExtension;
using ObjCRuntime;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Quic.Ios;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Extensions;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Streams;
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
        // Required memory systems, always on (device-measured 2026-06-14): the GC guard keeps the
        // extension under the ~52 MB jetsam limit — without it, it crashes immediately on tunnel start —
        // and VhMemory.Instance lets the QUIC download brake read this process's live phys_footprint.
        IosMemoryGuard.Start();
        IosMemory.Install();
        IosMemoryMonitor.RegisterCrashLog();

        _startTunnelCompletionHandler = startTunnelCompletionHandler;
        _completionFired = false;

        // Resolve the config folder — ProviderConfiguration set by App takes priority.
        var configFolder = ResolveConfigFolder();
        try { Directory.CreateDirectory(configFolder); } catch { /* ignore */ }

        // Kick off heavy VpnHood init off the main thread.
        _ = Task.Run(() => {
            try {
                // memoryScale feeds the QUIC receive-window sizing: when the TCP proxy is off, TCP rides
                // the packet channel (few QUIC streams), so the windows can be doubled under the same
                // memory bound. Read lazily from the host so it reflects the status at each QUIC-client
                // creation (session build), not the state at factory construction.
                var sf = new IosSocketFactory(memoryScale: () => _vpnServiceHost?.IsTcpProxy == true ? 1 : 2);
                // withLogger: true so the host runs the standard LogService (LogToDevice/LogToFile come from
                // the app's LogServiceOptions). deviceLoggerProviderFactory routes LogToDevice to os_log
                // (IosDeviceLoggerProvider) instead of the default Trace sink (which is /dev/null in an extension).
                _vpnServiceHost = new VpnServiceHost(configFolder, this, sf,
                    withLogger: true, messageListener: _messageListener,
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
            VhLogger.Instance.LogError(ex, "Could not process an app message.");
            responseData = BuildApiErrorResponse(ex);
        }

        using (responseData) {
            try { completionHandler?.Invoke(responseData); } catch { /* ignore */ }
        }
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
        VhLogger.Instance.LogWarning("iOS requested StopTunnel. Reason: {Reason}", reason);
        _ = StopTunnelAsync(completionHandler);
    }

    public VpnHoodClientFactory CreateClientFactory()
    {
        return new VpnHoodClientFactory();
    }

    private async Task StopTunnelAsync(Action completionHandler)
    {
        try {
            if (_vpnServiceHost != null)
                await _vpnServiceHost.TryDisconnect().Vhc();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Could not cleanly stop the iOS VPN tunnel.");
        }
        finally {
            try { completionHandler(); } catch { /* ignore */ }
        }
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        // Diagnostics hooks — no-ops unless debug logging is on (the gates follow VhLogger.MinLogLevel,
        // set by the host's LogService from vpn.config just before this per-(re)connect call).
        VpnHood.Core.Toolkit.Memory.VhTypeTracker.Enabled = IosMemoryMonitor.Enabled;
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
