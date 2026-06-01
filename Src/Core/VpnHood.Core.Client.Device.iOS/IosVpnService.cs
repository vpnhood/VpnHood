using Microsoft.Extensions.Logging;
using NetworkExtension;
using ObjCRuntime;
using System.Runtime.InteropServices;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Tunneling.Sockets;
using VpnHood.Core.VpnAdapters.Abstractions;
using VpnHood.Core.VpnAdapters.iOSTun;

namespace VpnHood.Core.Client.Device.iOS;

// The Objective-C class name MUST match NSExtensionPrincipalClass in the Extension's Info.plist.
// The host Network Extension should subclass this type and register it under the expected
// ObjC name, overriding <see cref="AppGroupId"/> with its own App Group identifier.
public abstract class IosVpnService : NEPacketTunnelProvider, IVpnServiceHandler, IIosPacketTunnelProvider
{
    // Created in StartTunnel once the config folder is resolved from ProviderConfiguration.
    private VpnServiceHost? _vpnServiceHost;

    // VpnService API transport for iOS: messages flow over NEPacketTunnelProvider's app message channel
    // (HandleAppMessage) instead of a TCP loopback socket.
    private readonly MessageVpnServiceApiListener _apiListener = new();

    // Stored when StartTunnel is called; invoked once IosVpnAdapter applies network settings
    // (iOS terminates the extension if the completion handler runs before SetTunnelNetworkSettings).
    private Action<NSError>? _startTunnelCompletionHandler;
    private int _completionFired;

    /// <summary>
    /// The App Group identifier shared with the host app (e.g. <c>group.com.example.client</c>).
    /// Must match the App Group used by the host <see cref="IosDevice"/>.
    /// </summary>
    protected abstract string AppGroupId { get; }

    private const string VpnServiceConfigFolderKey = "VpnServiceConfigFolder";

    /// Called by IosVpnAdapter.AdapterOpen after SetTunnelNetworkSettings succeeds (error == null)
    /// or fails (error != null). Safe to call multiple times — only the first call wins.
    public void CompleteStartTunnel(NSError? error)
    {
        if (Interlocked.Exchange(ref _completionFired, 1) != 0)
            return;
        var handler = _startTunnelCompletionHandler;
        _startTunnelCompletionHandler = null;
        try { handler?.Invoke(error!); } catch { /* ignore */ }
    }

    /// Resolves the VPN service config folder:
    /// 1. ProviderConfiguration["VpnServiceConfigFolder"] — set by the App in IosDevice.StartVpnService (most reliable)
    /// 2. GetContainerUrl — shared App Group container
    /// 3. LocalApplicationData — per-process fallback (IPC will NOT work with this)
    public string ResolveConfigFolder()
    {
        // First choice: path passed explicitly by the App via ProviderConfiguration
        var proto = ProtocolConfiguration as NETunnelProviderProtocol;
        if (proto?.ProviderConfiguration?[(NSString)VpnServiceConfigFolderKey] is NSString folderStr &&
            !string.IsNullOrEmpty(folderStr))
            return folderStr.ToString();

        // Second choice: App Group shared container
        var containerUrl = NSFileManager.DefaultManager.GetContainerUrl(AppGroupId);
        if (containerUrl?.Path is { } containerPath)
            return Path.Combine(containerPath, "vpn-service");

        // Last resort — IPC will not work between App and Extension with this path
        return Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "vpn-service");
    }

    // Called by the .NET iOS runtime when ObjC creates the extension instance.
    // [Export("init")] is intentionally absent: iOS uses NEPacketTunnelProvider's init,
    // and .NET iOS wraps the resulting ObjC handle here. Adding [Export("init")] caused
    // a null function-pointer in the ObjC dispatch table → EXC_BAD_ACCESS / CODESIGNING:
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

    // Background memory guard: the iOS Network Extension has a hard ~50 MB jetsam limit.
    // Under heavy download traffic the read path allocates a parsed IpPacket + a per-packet
    // byte[] thousands of times per second, so the live managed heap can jump ~5 MB within a
    // few hundred ms and push phys_footprint past the limit. Force a full GC + finalizer drain
    // immediately whenever the live heap crosses a low threshold (and periodically otherwise) so
    // the peak stays well below the limit. Native NSObject peers that escaped Dispose are only
    // reclaimed on finalization, so WaitForPendingFinalizers is essential.
    private static int _memoryGuardStarted;
    private static void StartMemoryGuard()
    {
        if (Interlocked.Exchange(ref _memoryGuardStarted, 1) == 1)
            return;

        var thread = new Thread(() => {
            var n = 0;
            while (true) {
                try {
                    var live = GC.GetTotalMemory(false);
                    if ((n % 10 == 0 && n > 0) || live > 5L * 1024 * 1024) {
                        GC.Collect();
                        GC.WaitForPendingFinalizers();
                        GC.Collect();
                    }
                }
                catch { /* ignore */ }
                n++;
                Thread.Sleep(100);
            }
            // ReSharper disable once FunctionNeverReturns
        }) {
            IsBackground = true,
            Name = "VpnHoodExtMemoryGuard"
        };
        thread.Start();
    }

    public override void StartTunnel(
        NSDictionary<NSString, NSObject>? options, Action<NSError> startTunnelCompletionHandler)
    {
        StartMemoryGuard();

        // NOTE (memory): the iOS-specific small packet/UDP buffer sizes (64 KB) are no longer set
        // here as TunnelDefaults statics. They now flow from the host app via AppOptions →
        // ClientOptions (PacketChannelBufferSize / UdpProxyBufferSize / StreamProxyBufferSize),
        // written into vpn.config and consumed by the client. Keeping these out of the extension
        // means the desktop/Android defaults remain untouched.

        // Resolve the config folder — ProviderConfiguration set by App takes priority.
        var configFolder = ResolveConfigFolder();
        try { Directory.CreateDirectory(configFolder); } catch { /* ignore */ }

        // Apply placeholder settings WITHOUT waiting for callback.
        // Under Mono AOT the block-to-delegate marshaling for the completion handler
        // never invokes the .NET callback, so fire-and-forget then signal start completion
        // immediately. iOS finishes applying settings asynchronously and won't kill the
        // extension as long as we DID call SetTunnelNetworkSettings before signaling start.
        // CRITICAL: do NOT set IncludedRoutes here. A DefaultRoute on the placeholder makes
        // iOS route ALL extension outbound traffic INTO our own (non-forwarding) tunnel, so
        // every TCP connect (including to the VpnHood server) hangs forever. The real routes
        // are installed by IosVpnAdapter.AdapterOpen once the VPN handshake completes.
        var placeholder = new NEPacketTunnelNetworkSettings("192.0.2.1") {
            IPv4Settings = new NEIPv4Settings(new[] { "10.255.255.2" }, new[] { "255.255.255.0" }),
            Mtu = NSNumber.FromInt32(1500)
        };
        try { SetTunnelNetworkSettings(placeholder, null); } catch { /* ignore */ }

        // Kick off heavy VpnHood init off the main thread.
        _ = Task.Run(() => {
            try {
                var sf = new SocketFactory();
                _vpnServiceHost = new VpnServiceHost(configFolder, this, sf,
                    netFilter: null, withLogger: false, apiListener: _apiListener);
                _ = _vpnServiceHost.TryConnect(true);
            }
            catch {
                /* ignore: state is reported back to the App via the connection info file */
            }
        });

        // Tell iOS the tunnel start is done (success).
        try { startTunnelCompletionHandler(null!); } catch { /* ignore */ }
    }

    public override void HandleAppMessage(NSData? messageData, Action<NSData>? completionHandler)
    {
        NSData responseData;
        try {
            if (_vpnServiceHost == null || messageData == null) {
                responseData = BuildApiErrorResponse(new InvalidOperationException("VpnServiceHost is not ready."));
            }
            else {
                var responseBytes = _apiListener.ProcessMessageAsync(messageData.ToArray(),
                    CancellationToken.None).GetAwaiter().GetResult();
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
        return NSData.FromArray(StreamUtils.ObjectToJsonBuffer(response,
            ApiTransportJsonContext.For<ApiResponse<object>>()).ToArray());
    }

    public override void StopTunnel(NEProviderStopReason reason, Action completionHandler)
    {
        _ = _vpnServiceHost?.TryDisconnect();
        completionHandler();
    }

    public IVpnAdapter CreateAdapter(VpnAdapterSettings adapterSettings, string? debugData)
    {
        return new IosVpnAdapter(this, new IosVpnAdapterSettings {
            AdapterName = adapterSettings.AdapterName,
            Blocking = adapterSettings.Blocking,
            AutoDisposePackets = adapterSettings.AutoDisposePackets,
            AutoRestart = adapterSettings.AutoRestart,
            MaxPacketSendDelay = adapterSettings.MaxPacketSendDelay,
            QueueCapacity = adapterSettings.QueueCapacity,
            AutoMetric = adapterSettings.AutoMetric
        });
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
