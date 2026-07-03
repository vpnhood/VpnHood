using VpnHood.Core.Quic.Ios;
using VpnHood.Core.VpnAdapters.IosTun;

namespace VpnHood.Core.Client.Devices.Ios;

/// <summary>
/// Diagnostics-only memory probe for the iOS Network Extension — it does not maintain anything, it only
/// observes and logs. When <see cref="Enabled"/> it installs the <c>ext-crash.log</c> handlers and appends a
/// per-sample <c>ext-mem.log</c> breakdown; when disabled <see cref="Start"/> is a no-op. Off in production.
/// </summary>
/// <remarks>
/// The active concerns are elsewhere and run regardless of this switch: the GC maintenance heartbeat is
/// <see cref="IosMemoryGuard"/>, and the phys_footprint feed to the QUIC jetsam brake is
/// <see cref="IosFootprintSampler"/> — this probe reads footprint via <see cref="IosFootprintSampler.TryRead"/>
/// rather than sampling natively itself. For the aggregate line it also reads the <b>public</b> snapshots
/// owned by the other subsystems' diagnostics (<c>IosTunDiagnostics</c>, <c>IosQuicDiagnostics</c>,
/// <c>TcpStack.LocalTcpStack.ActiveDiagnostics</c>) — it does not own their counters. See
/// <c>docs/ios-extension-memory-and-throughput.md</c>.
/// <para>
/// The ext-mem.log breakdown: <c>footprint</c> = phys_footprint (the number jetsam enforces); <c>gcLive</c> =
/// live managed objects; <c>native</c> = footprint − managed heap (NSObject peers, TLS/socket buffers, kernel
/// rcv buffers, code, stacks); <c>dn</c>/<c>up</c> = cumulative MB in/out. Diagnosis key: the memory guard
/// force-GCs continuously, so if <c>native</c> climbs while <c>gcLive</c> stays flat, the climb is NATIVE
/// (buffers/peers), not a managed leak — GC levers (soft-heap-limit, ConserveMemory) cannot fix it.
/// </para>
/// </remarks>
internal static class IosMemoryMonitor
{
    private static bool _started;

    private const double Mib = 1024.0 * 1024.0;

    /// <summary>
    /// Gates this probe (<c>ext-mem.log</c> + <c>ext-crash.log</c>). Defaults to <c>false</c> (production);
    /// seeded from the <c>VH_IOS_DIAGNOSTICS</c> env var, the same switch the per-subsystem diagnostics read,
    /// so one variable enables the whole aggregate probe. Set it before <see cref="Start"/> to take effect.
    /// </summary>
    public static bool Enabled { get; set; } = ReadEnvDefault();

    /// <summary>
    /// Starts the probe (idempotent) — a no-op unless <see cref="Enabled"/>. Call once from
    /// <see cref="IosVpnService.StartTunnel"/>. The active guard/sampler are started separately.
    /// </summary>
    public static void Start()
    {
        if (!Enabled)
            return;
        if (Interlocked.Exchange(ref _started, true))
            return;

        // Let the TCP stack annotate its Debug +CONN/-CONN lifecycle logs with the live jetsam footprint
        // (phys_footprint in MB). Purely diagnostic, so it is wired only when this probe is enabled; it
        // reads footprint via the active sampler rather than probing natively itself.
        TcpStack.TcpStackDiagnostics.FootprintMbProvider =
            () => IosFootprintSampler.TryRead(out var vm) && vm.Footprint > 0 ? vm.Footprint / Mib : 0.0;

        RegisterCrashLog();

        var thread = new Thread(() => {
            string logPath;
            try {
                logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ext-mem.log");
            }
            catch { return; }

            var peakMb = 0.0;
            var lastLoggedMb = double.MinValue;
            var tick = 0;
            while (true) {
                try {
                    if (IosFootprintSampler.TryRead(out var vm) && vm.Footprint > 0) {
                        var mb = vm.Footprint / Mib;
                        if (mb > peakMb) peakMb = mb;

                        // Log on any meaningful change, on a new peak, as a heartbeat, OR every sample once
                        // we're within ~7 MB of the limit — so the sub-second burst spike that actually
                        // crosses 52 MB is captured instead of slipping between samples.
                        if (Math.Abs(mb - lastLoggedMb) >= 0.3 || mb >= peakMb || mb >= 45.0 || tick % 15 == 0) {
                            lastLoggedMb = mb;
                            AppendProbeLine(logPath, vm, mb, peakMb);
                        }
                    }
                }
                catch { /* best-effort */ }
                tick++;
                Thread.Sleep(100);
            }
            // ReSharper disable once FunctionNeverReturns
        }) {
            IsBackground = true,
            Name = "VpnHoodExtMemoryProbe"
        };
        thread.Start();
    }

    // CRASH ATTRIBUTION: the extension died with SIGABRT via CoreCLR Task.ThrowAsync twice on 2026-06-29
    // (an unhandled exception escaping a fire-and-forget task / async void), but the .ips report carries no
    // managed exception detail. Persist the exception synchronously to Documents/ext-crash.log BEFORE the
    // runtime aborts so the next occurrence is attributable. File.AppendAllText (not VhLogger) on purpose:
    // the logger's buffering/disposal cannot be trusted mid-crash.
    private static void RegisterCrashLog()
    {
        AppDomain.CurrentDomain.UnhandledException += (_, e) => {
            try {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ext-crash.log");
                File.AppendAllText(path,
                    $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UNHANDLED (terminating={e.IsTerminating})\n{e.ExceptionObject}\n\n");
            }
            catch { /* nothing safe left to do */ }
        };
        TaskScheduler.UnobservedTaskException += (_, e) => {
            try {
                var path = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ext-crash.log");
                File.AppendAllText(path,
                    $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} UNOBSERVED TASK\n{e.Exception}\n\n");
            }
            catch { /* best-effort */ }
        };
    }

    // Formats and appends one ext-mem.log line. Reads the PUBLIC snapshots owned by each subsystem's
    // diagnostics (IosTunDiagnostics / IosQuicDiagnostics / TcpStack) — this aggregates, it does not own
    // their counters.
    private static void AppendProbeLine(string logPath, IosFootprintSampler.FootprintInfo vm, double mb, double peakMb)
    {
        var gcLive = GC.GetTotalMemory(false) / Mib;
        double gcHeap = 0;
        double gcCommit = 0;
        try {
            var gcInfo = GC.GetGCMemoryInfo();
            gcHeap = gcInfo.HeapSizeBytes / Mib;
            // Committed GC segments (count toward anon/footprint even when live is flat): discriminates
            // "allocation-storm grows GC segments" from a true native climb during a freeze.
            gcCommit = gcInfo.TotalCommittedBytes / Mib;
        }
        catch { /* not supported on this runtime */ }
        // anon = anonymous dirty (malloc/heap/stacks/buffers); comp = compressed anon; both count toward
        // footprint. code = file-backed (NOT in footprint), logged for context.
        var anon = vm.Internal / Mib;
        var comp = vm.Compressed / Mib;
        var code = vm.External / Mib;
        var native = mb - Math.Max(gcLive, gcHeap);
        var dnMb = IosTunDiagnostics.InboundBytes / Mib;
        var upMb = IosTunDiagnostics.OutboundBytes / Mib;
        // TCP-stack diagnostics: concurrent connections + bytes parked in the reassembly pipes + the
        // active small-buffer profile.
        var diag = TcpStack.LocalTcpStack.ActiveDiagnostics;
        var pipeBuf = (diag?.TotalPipeBufferedBytes ?? 0) / Mib;
        var conn = diag?.ConnectionCount ?? 0;
        var peakConn = diag?.PeakConnectionCount ?? 0;
        var est = diag?.EstablishedConnections ?? 0;
        var winKb = (diag?.ConfiguredReceiveWindow ?? 0) / 1024;
        var maxC = diag?.ConfiguredMaxConnections ?? 0;
        var qStreams = IosQuicDiagnostics.LiveStreamCount; // live native QUIC streams (NWConnections)
        var sendQ = IosQuicDiagnostics.OutstandingSendBytes / Mib;
        // Freeze locator: ms since the last TUN read callback / completed TUN write, worst single TUN
        // write drain and worst QUIC stream teardown since the last probe line (maxes reset each line).
        var nowTicks = Environment.TickCount64;
        var lastRd = IosTunDiagnostics.LastReadTicks;
        var lastWr = IosTunDiagnostics.LastWriteTicks;
        var rdAge = lastRd == 0 ? -1 : nowTicks - lastRd;
        var wrAge = lastWr == 0 ? -1 : nowTicks - lastWr;
        var wrMax = IosTunDiagnostics.TakeMaxTunWriteMs();
        var cancMax = IosQuicDiagnostics.TakeMaxStreamCancelMs();
        File.AppendAllText(logPath,
            $"{DateTime.UtcNow:HH:mm:ss.fff} footprint={mb:F1}MB peak={peakMb:F1}MB " +
            $"gcLive={gcLive:F1} gcHeap={gcHeap:F1} gcCommit={gcCommit:F1} native={native:F1} " +
            $"anon={anon:F1} comp={comp:F1} code={code:F1} " +
            $"conn={conn} est={est} peakConn={peakConn} qStreams={qStreams} sendQ={sendQ:F2}MB pipeBuf={pipeBuf:F1}MB win={winKb}KB maxC={maxC} " +
            $"rdAge={rdAge} wrAge={wrAge} wrMax={wrMax} cancMax={cancMax} " +
            $"dn={dnMb:F1}MB up={upMb:F1}MB" +
            (mb >= 50 ? " <<< NEAR 52MB JETSAM" : "") + "\n");
    }

    // Seed Enabled from the VH_IOS_DIAGNOSTICS env var (any of 1/true/yes) so one switch turns on all the
    // iOS diagnostics for a dev/simulator run without a code change.
    private static bool ReadEnvDefault()
    {
        try {
            var value = Environment.GetEnvironmentVariable("VH_IOS_DIAGNOSTICS");
            return value is "1" or "true" or "True" or "TRUE" or "yes" or "YES";
        }
        catch {
            return false;
        }
    }
}
