using System.Runtime.InteropServices;
using Foundation;
using NetworkExtension;
using ObjCRuntime;
using VpnHood.Core.Client.VpnServices.Abstractions;
using VpnHood.Core.Client.VpnServices.Abstractions.Requests;
using VpnHood.Core.Client.VpnServices.Host;
using VpnHood.Core.Toolkit.ApiClients;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Streams;
using VpnHood.Core.Tunneling.Sockets;
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


    // Background memory guard: the iOS Network Extension has a hard ~52 MB jetsam limit.
    // Under heavy FULL-TUNNEL download traffic the read path (OnPacketsReceived) allocates a parsed
    // IpPacket + a per-packet byte[] (NSData.ToArray) thousands of times per second, so the live
    // managed heap can jump ~5 MB within a few hundred ms and push phys_footprint past the limit.
    // Force a full GC + finalizer drain immediately whenever the live heap crosses a low threshold
    // (and periodically otherwise) so the peak stays well below the limit. Native NSObject peers that
    // escaped Dispose are only reclaimed on finalization, so WaitForPendingFinalizers is essential.
    // (Regression: removed in 9e9e05c80; restored — without it full tunnel jetsams under load while
    // the memory-light DNS-only device-split mode did not, which is why it "worked before".)
    private static bool _memoryGuardStarted;
    private static void StartMemoryGuard()
    {
        if (Interlocked.Exchange(ref _memoryGuardStarted, true))
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
        // REQUIRED — do not remove. Device-measured (2026-06-14): disabling this crashes the extension
        // IMMEDIATELY on tunnel start, even with MONO_GC_PARAMS soft-heap-limit=8m + System.GC.ConserveMemory=9
        // and the adapter's deterministic NSData Dispose. The forced GC.Collect + WaitForPendingFinalizers
        // heartbeat drains native NSObject peers that no project-config GC setting drains promptly enough,
        // so the init-time allocation burst (VpnServiceHost + TLS) blows the 52 MB jetsam cap without it.
        StartMemoryGuard();
        StartMemoryProbe();

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
                var sf = new SystemSocketFactory();
                _vpnServiceHost = new VpnServiceHost(configFolder, this, sf,
                    netFilter: null, withLogger: false, messageListener: _messageListener);
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
                    .ConfigureAwait(false);
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

    // ----- DIAGNOSTIC memory probe: attribute phys_footprint to managed vs native -----
    // The ~52 MB jetsam limit is enforced on phys_footprint (mach TASK_VM_INFO). The simple
    // "log the one number" probe could not tell us WHERE the long-run climb lives, so this version
    // breaks it down. Every ~2 s (and on any >=0.5 MB change) it appends one line to
    // Documents/ext-mem.log:
    //   footprint = phys_footprint                          -> the exact number iOS jetsam enforces
    //   gcLive    = GC.GetTotalMemory(false)                -> live managed objects
    //   gcHeap    = GC.GetGCMemoryInfo().HeapSizeBytes      -> managed heap incl. committed-but-free
    //                                                          (may be 0 if unsupported on MonoVM)
    //   native    = footprint - max(gcLive, gcHeap)         -> EVERYTHING not in the managed heap:
    //                                                          NSObject peers, socket/TLS buffers,
    //                                                          kernel-charged rcv buffers, code, stacks
    //   dn/up     = cumulative MB written-inbound / read-outbound through the adapter
    // Diagnosis key: the memory guard force-GCs the managed heap continuously, so if `native` climbs
    // while `gcLive` stays flat, the climb is NATIVE (buffers/peers), not a managed leak — and the
    // GC-based levers (soft-heap-limit, ConserveMemory) cannot fix it. Correlate with dn/up: a climb
    // that tracks bytes-downloaded and never recedes is a per-traffic native leak; one that plateaus
    // is a buffer high-water mark.
    [DllImport("__Internal")] private static extern int task_info(uint task, int flavor, byte[] taskInfo, ref uint count);
    [DllImport("__Internal")] private static extern uint mach_task_self();
    private const int TASK_VM_INFO = 22;          // mach/task_info.h flavor
    // task_vm_info_data_t field byte offsets (arm64). phys_footprint @144 is verified on-device.
    private const int InternalOffset = 48;        // anonymous dirty memory: malloc, GC heap, thread stacks, buffers
    private const int ExternalOffset = 64;        // file-backed: code/dylibs/AOT — NOT counted in phys_footprint
    private const int CompressedOffset = 120;     // compressed anonymous pages — IS counted in phys_footprint
    private const int PhysFootprintOffset = 144;  // the exact number iOS jetsam enforces

    private struct VmInfo { public long Footprint, Internal, Compressed, External; }

    private static bool TryReadVmInfo(out VmInfo info)
    {
        info = default;
        var buffer = new byte[512];
        var count = (uint)(buffer.Length / 4);
        if (task_info(mach_task_self(), TASK_VM_INFO, buffer, ref count) != 0)
            return false;
        info.Footprint = BitConverter.ToInt64(buffer, PhysFootprintOffset);
        info.Internal = BitConverter.ToInt64(buffer, InternalOffset);
        info.Compressed = BitConverter.ToInt64(buffer, CompressedOffset);
        info.External = BitConverter.ToInt64(buffer, ExternalOffset);
        return true;
    }

    private static bool _memoryProbeStarted;
    private static void StartMemoryProbe()
    {
        if (Interlocked.Exchange(ref _memoryProbeStarted, true))
            return;

        var thread = new Thread(() => {
            string logPath;
            try {
                logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ext-mem.log");
            }
            catch { return; }

            const double mib = 1024.0 * 1024.0;
            var peakMb = 0.0;
            var lastLoggedMb = double.MinValue;
            var tick = 0;
            while (true) {
                try {
                    if (TryReadVmInfo(out var vm) && vm.Footprint > 0) {
                        var mb = vm.Footprint / mib;
                        if (mb > peakMb) peakMb = mb;

                        // Log on any meaningful change, on a new peak, as a heartbeat, OR every sample
                        // once we're within ~7 MB of the limit — so the sub-second burst spike that
                        // actually crosses 52 MB is captured instead of slipping between samples.
                        if (Math.Abs(mb - lastLoggedMb) >= 0.3 || mb >= peakMb || mb >= 45.0 || tick % 15 == 0) {
                            lastLoggedMb = mb;
                            var gcLive = GC.GetTotalMemory(false) / mib;
                            double gcHeap = 0;
                            try { gcHeap = GC.GetGCMemoryInfo().HeapSizeBytes / mib; }
                            catch { /* not supported on this runtime */ }
                            // anon = anonymous dirty (malloc/heap/stacks/buffers); comp = compressed anon;
                            // both count toward footprint. code = file-backed (NOT in footprint), logged
                            // for context. native-non-managed ≈ (anon+comp) - managed heap.
                            var anon = vm.Internal / mib;
                            var comp = vm.Compressed / mib;
                            var code = vm.External / mib;
                            var native = mb - Math.Max(gcLive, gcHeap);
                            var dnMb = Interlocked.Read(ref IosVpnAdapter.InboundBytes) / mib;
                            var upMb = Interlocked.Read(ref IosVpnAdapter.OutboundBytes) / mib;
                            // TCP-stack diagnostics: concurrent connections + bytes parked in the
                            // reassembly pipes + the active small-buffer profile.
                            var diag = VpnHood.Core.TcpStack.LocalTcpStack.ActiveDiagnostics;
                            var pipeBuf = (diag?.TotalPipeBufferedBytes ?? 0) / mib;
                            var conn = diag?.ConnectionCount ?? 0;
                            var peakConn = diag?.PeakConnectionCount ?? 0;
                            var est = diag?.EstablishedConnections ?? 0;
                            var winKb = (diag?.ConfiguredReceiveWindow ?? 0) / 1024;
                            var maxC = diag?.ConfiguredMaxConnections ?? 0;
                            File.AppendAllText(logPath,
                                $"{DateTime.UtcNow:HH:mm:ss.fff} footprint={mb:F1}MB peak={peakMb:F1}MB " +
                                $"gcLive={gcLive:F1} gcHeap={gcHeap:F1} native={native:F1} " +
                                $"anon={anon:F1} comp={comp:F1} code={code:F1} " +
                                $"conn={conn} est={est} peakConn={peakConn} pipeBuf={pipeBuf:F1}MB win={winKb}KB maxC={maxC} " +
                                $"dn={dnMb:F1}MB up={upMb:F1}MB" +
                                (mb >= 50 ? " <<< NEAR 52MB JETSAM" : "") + "\n");
                        }
                    }
                }
                catch { /* best-effort */ }
                tick++;
                Thread.Sleep(250);
            }
            // ReSharper disable once FunctionNeverReturns
        }) {
            IsBackground = true,
            Name = "VpnHoodExtMemoryProbe"
        };
        thread.Start();
    }
}
