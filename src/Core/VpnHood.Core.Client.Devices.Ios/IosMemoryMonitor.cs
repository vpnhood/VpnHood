using CoreFoundation;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Quic.Ios;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.VpnAdapters.IosTun;

namespace VpnHood.Core.Client.Devices.Ios;

/// <summary>
/// Diagnostics-only memory probe for the iOS Network Extension — it does not maintain anything, it only
/// observes and logs. When <see cref="Enabled"/> it installs the <c>ext-crash.log</c> handlers and appends a
/// per-sample <c>ext-mem.log</c> breakdown; when disabled <see cref="Start"/> is a no-op. Off in production.
/// </summary>
/// <remarks>
/// The active concerns are elsewhere and run regardless of this switch: the GC maintenance heartbeat is
/// <see cref="IosMemoryGuard"/>, and the live phys_footprint for the QUIC jetsam brake is read on demand via
/// <see cref="IosMemory"/> (published through <c>VhMemory.Instance</c>) — this probe reads the
/// full breakdown via <see cref="IosMemory.TryRead"/> on its own thread. For the aggregate line it also reads the <b>public</b> snapshots
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
    internal const string LifecycleTag = "VH-IOS-LIFECYCLE";

    private static bool _started;
    private static double? _baselineFootprint;
    private static double? _baselineNative;
    private static bool _wasTrafficActive;
    private static long _idleStartedTicks;
    private static double _idleMinFootprint;
    private static double _idleMinNative;
    private static bool _idleCheckpointLogged;
    private static int _crashLogRegistered;
    private static int _cycle;
    private static long _lastTrafficBytes;
    private static long _lastTrafficSampleTicks;
    private static long _lastBusyTicks;
    private static double _lastCensusNative = double.MinValue;
    private static long _lastCensusTicks;
    // ReSharper disable once NotAccessedField.Local — kept alive so the dispatch source isn't collected.
    private static DispatchSource.MemoryPressure? _pressureSource;

    private const double Mib = 1024.0 * 1024.0;
    private const long IdleCheckpointMs = 30_000;
    private const long TrafficSettleMs = 10_000;
    private const long ProbeLogIntervalMs = 5_000;
    private const double BusyTrafficBytesPerSecond = 32 * 1024;

    /// <summary>
    /// Read-only gate for this probe (<c>ext-mem.log</c> + <c>ext-crash.log</c>): on whenever the effective
    /// log level is below Information — the same rule the per-subsystem diagnostics compute, so one log-level
    /// change enables the whole aggregate probe.
    /// </summary>
    public static bool Enabled => VhLogger.MinLogLevel < LogLevel.Information;

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

        // NOTE: the TCP stack's +CONN/-CONN footprint annotation is no longer wired here — IosMemory
        // publishes an on-demand phys_footprint read through VhMemory.Instance (registered in the
        // device/service), so the stack reads it regardless of this diagnostics switch.

        RegisterCrashLog();

        var thread = new Thread(() => {
            string logPath;
            try {
                logPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ext-mem.log");
            }
            catch { return; }

            // Keep device pulls fast while preserving the immediately preceding extension session.
            // A process restart after jetsam rotates the crash session instead of appending another
            // multi-hour run to the same large file.
            try {
                var previousLogPath = Path.Combine(Path.GetDirectoryName(logPath)!, "ext-mem.previous.log");
                if (File.Exists(logPath))
                    File.Move(logPath, previousLogPath, overwrite: true);
                File.WriteAllText(logPath, $"{DateTime.UtcNow:O} [MemoryProbe] SESSION_START pid={Environment.ProcessId}\n");
            }
            catch { /* rotation is diagnostic-only; sampling can still append below */ }

            var peakMb = 0.0;
            var lastLoggedTicks = 0L;
            while (true) {
                try {
                    // Enabled is dynamic (it follows the log level, which a reconnect can change):
                    // go quiet instead of exiting so a later /log:debug reconnect resumes sampling.
                    if (Enabled && IosMemory.TryRead(out var vm) && vm.Footprint > 0) {
                        var mb = vm.Footprint / Mib;
                        var isNewPeak = mb > peakMb;
                        if (isNewPeak) peakMb = mb;

                        // Read footprint at 100 ms so peak still catches short jetsam spikes, but allocate and
                        // write the detailed census at one fixed five-second cadence. A stable cadence makes
                        // subsystem A/B runs directly comparable and keeps the probe out of the hot path.
                        var nowTicks = Environment.TickCount64;
                        if (lastLoggedTicks == 0 || nowTicks - lastLoggedTicks >= ProbeLogIntervalMs) {
                            lastLoggedTicks = nowTicks;
                            AppendProbeLine(logPath, vm, mb, peakMb);
                        }
                    }
                }
                catch { /* best-effort */ }
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
    internal static void RegisterCrashLog()
    {
        if (Interlocked.Exchange(ref _crashLogRegistered, 1) != 0)
            return;

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
        AppDomain.CurrentDomain.ProcessExit += (_, _) => AppendLifecycleLog("PROCESS_EXIT");

        LogEnvironment();
        StartMemoryPressureSource();
    }

    // Termination attribution for the PREVIOUS run, learned from 2026-07-16: a silent death turned out to
    // be an overnight iOS OTA update reboot, and proving that took a full forensics session. Log boot time,
    // OS build (+ the previous run's persisted build), and the previous probe log's last write so the next
    // silent death attributes itself: boot > prevHeartbeat = the DEVICE went down (OTA/panic/user reboot);
    // otherwise the PROCESS died while the device stayed up (jetsam/0xDEAD10CC/crash — check the .ips files).
    private static void LogEnvironment()
    {
        try {
            var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            var boot = IosMemory.TryReadBootTimeUtc();
            var os = "unknown";
            try { os = NSProcessInfo.ProcessInfo.OperatingSystemVersionString; } catch { /* best-effort */ }

            var envPath = Path.Combine(documents, "ext-env.txt");
            string? previousOs = null;
            try { if (File.Exists(envPath)) previousOs = File.ReadAllText(envPath).Trim(); } catch { /* best-effort */ }
            try { File.WriteAllText(envPath, os); } catch { /* best-effort */ }

            // ext-mem.log has not been rotated yet (the probe thread does that later), so its mtime is the
            // previous run's last 5 s heartbeat.
            var previousLogPath = Path.Combine(documents, "ext-mem.log");
            DateTime? previousHeartbeat = File.Exists(previousLogPath)
                ? File.GetLastWriteTimeUtc(previousLogPath)
                : null;

            var verdict = "unknown";
            if (boot.HasValue && previousHeartbeat.HasValue)
                verdict = boot > previousHeartbeat
                    ? "device-rebooted-after-last-heartbeat"
                    : "process-died-while-device-up";

            AppendLifecycleLog(
                $"ENV pid={Environment.ProcessId} os=\"{os}\" osPrev=\"{previousOs ?? "none"}\" " +
                $"osChanged={previousOs != null && previousOs != os} " +
                $"boot={boot?.ToString("yyyy-MM-dd HH:mm:ss") ?? "unknown"}Z " +
                $"prevHeartbeat={previousHeartbeat?.ToString("yyyy-MM-dd HH:mm:ss") ?? "none"}Z " +
                $"prevExitVerdict={verdict}");
        }
        catch { /* best-effort */ }
    }

    // iOS raises WARN/CRITICAL memory-pressure events before jetsam acts; the 5 s probe cadence can miss
    // that final window entirely. Event-driven, negligible cost, so it runs regardless of the diagnostics
    // gate — the census only fires on non-normal levels.
    private static void StartMemoryPressureSource()
    {
        try {
            var source = new DispatchSource.MemoryPressure(
                MemoryPressureFlags.Normal | MemoryPressureFlags.Warn | MemoryPressureFlags.Critical,
                DispatchQueue.DefaultGlobalQueue);
            source.SetEventHandler(() => {
                try {
                    var flags = source.PressureFlags;
                    AppendLifecycleLog($"MEMORY_PRESSURE flags={flags} " + GetLifecycleSnapshot());
                    if ((flags & (MemoryPressureFlags.Warn | MemoryPressureFlags.Critical)) != 0)
                        AppendLifecycleLog($"MEMORY_PRESSURE census {IosMemory.ReadRegionCensus()}");
                }
                catch { /* best-effort */ }
            });
            source.Resume();
            _pressureSource = source;
        }
        catch (Exception ex) {
            AppendLifecycleLog($"MEMORY_PRESSURE_MONITOR_FAILED exception={ex.Message}");
        }
    }

    internal static void AppendLifecycleLog(string message)
    {
        var line = $"[{LifecycleTag}] {message}";
        try {
            var path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "ext-crash.log");
            File.AppendAllText(path, $"{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss.fff} {line}\n");
        }
        catch { /* best-effort */ }

        try { VhLogger.Instance.LogWarning("{LifecycleEvent}", line); } catch { /* best-effort */ }
        try { System.Diagnostics.Debug.WriteLine(line); } catch { /* best-effort */ }
    }

    internal static string GetLifecycleSnapshot()
    {
        var nowTicks = Environment.TickCount64;
        var footprint = IosMemory.TryRead(out var vm) && vm.Footprint > 0
            ? $"{vm.Footprint / Mib:F1}MB"
            : "unknown";
        var tcp = TcpStack.LocalTcpStack.ActiveDiagnostics;
        var lastRead = IosTunDiagnostics.LastReadTicks;
        var lastWrite = IosTunDiagnostics.LastWriteTicks;
        var readAge = lastRead == 0 ? -1 : nowTicks - lastRead;
        var writeAge = lastWrite == 0 ? -1 : nowTicks - lastWrite;
        var fileDescriptors = IosMemory.TryReadFileDescriptorCount();
        var availBytes = IosMemory.TryReadAvailableMemory();
        var avail = availBytes < 0 ? "?" : $"{availBytes / Mib:F1}MB";
        var sqliteKb = Filtering.Sqlite.SplitSqlite.MemoryUsed() / 1024.0;

        return $"footprint={footprint} avail={avail} sqlite={sqliteKb:F0}KB " +
               $"fds={fileDescriptors}({IosMemory.TryReadFileDescriptorSummary()}) " +
               $"tcpConnections={tcp?.ConnectionCount ?? 0} " +
               $"tcpEstablished={tcp?.EstablishedConnections ?? 0} " +
               $"quicConnections={IosQuicDiagnostics.LiveConnectionCount} " +
               $"quicStreams={IosQuicDiagnostics.LiveStreamCount} " +
               $"tunReadAgeMs={readAge} tunWriteAgeMs={writeAge} " +
               $"appMessages={IosVpnService.AppMessageCompleted}/{IosVpnService.AppMessageStarted}";
    }

    // Formats and appends one ext-mem.log line. Reads the PUBLIC snapshots owned by each subsystem's
    // diagnostics (IosTunDiagnostics / IosQuicDiagnostics / TcpStack) — this aggregates, it does not own
    // their counters.
    private static void AppendProbeLine(string logPath, IosMemory.FootprintInfo vm, double mb, double peakMb)
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
        var inboundBytes = IosTunDiagnostics.InboundBytes;
        var outboundBytes = IosTunDiagnostics.OutboundBytes;
        var dnMb = inboundBytes / Mib;
        var upMb = outboundBytes / Mib;
        // TCP-stack diagnostics: concurrent connections + bytes parked in the reassembly pipes + the
        // active small-buffer profile.
        var diag = TcpStack.LocalTcpStack.ActiveDiagnostics;
        var pipeBuf = (diag?.TotalPipeBufferedBytes ?? 0) / Mib;
        var conn = diag?.ConnectionCount ?? 0;
        var peakConn = diag?.PeakConnectionCount ?? 0;
        var est = diag?.EstablishedConnections ?? 0;
        var winKb = (diag?.ConfiguredReceiveWindow ?? 0) / 1024;
        var maxC = diag?.ConfiguredMaxConnections ?? 0;
        var qConnections = IosQuicDiagnostics.LiveConnectionCount;
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
        var rdBatchMax = IosTunDiagnostics.TakeMaxReadBatchSize();
        var rdCallbacks = IosTunDiagnostics.ReadCallbackCount;
        var rdPackets = IosTunDiagnostics.ReadPacketCount;
        var cancelMax = IosQuicDiagnostics.TakeMaxStreamCancelMs();
        var appMsgStarted = IosVpnService.AppMessageStarted;
        var appMsgCompleted = IosVpnService.AppMessageCompleted;
        var fileDescriptors = IosMemory.TryReadFileDescriptorCount();
        var availBytes = IosMemory.TryReadAvailableMemory();
        var availMb = availBytes < 0 ? -1 : availBytes / Mib;
        // sqlite3_memory_used: live native SQLite allocator bytes, answers "how big is the
        // exclude-country layer" exactly (expected ~a hundred KB with the shared-reader fix)
        var sqliteKb = Filtering.Sqlite.SplitSqlite.MemoryUsed() / 1024.0;
        var tracker = VpnHood.Core.Toolkit.Memory.VhTypeTracker.GetSnapshotString();

        var trafficBytes = inboundBytes + outboundBytes;
        var trafficElapsedMs = _lastTrafficSampleTicks == 0 ? 0 : nowTicks - _lastTrafficSampleTicks;
        var trafficDelta = Math.Max(0, trafficBytes - _lastTrafficBytes);
        var trafficBytesPerSecond = trafficElapsedMs > 0 ? trafficDelta * 1000.0 / trafficElapsedMs : 0;
        _lastTrafficBytes = trafficBytes;
        _lastTrafficSampleTicks = nowTicks;

        if (trafficBytesPerSecond >= BusyTrafficBytesPerSecond)
            _lastBusyTicks = nowTicks;

        // Persistent keep-alive connections are normal. A phase becomes idle after traffic, rather than
        // connection count, has remained quiet for ten seconds; active counts are logged as context.
        var isTrafficActive = _lastBusyTicks != 0 && nowTicks - _lastBusyTicks < TrafficSettleMs;
        if (!isTrafficActive) {
            if (_idleStartedTicks == 0) {
                _idleStartedTicks = nowTicks;
                _idleMinFootprint = mb;
                _idleMinNative = native;
                _idleCheckpointLogged = false;
                if (_wasTrafficActive) {
                    File.AppendAllText(logPath,
                        $"{DateTime.UtcNow:HH:mm:ss.fff} [MemoryProbe] cycle={_cycle} IDLE_START " +
                        $"conn={conn} qConnections={qConnections} qStreams={qStreams} tracker={tracker}\n");
                }
            }

            _idleMinFootprint = Math.Min(_idleMinFootprint, mb);
            _idleMinNative = Math.Min(_idleMinNative, native);
            if (!_idleCheckpointLogged && nowTicks - _idleStartedTicks >= IdleCheckpointMs) {
                var gcBefore = GC.GetTotalMemory(false) / Mib;
                try {
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                    GC.WaitForPendingFinalizers();
                    GC.Collect(GC.MaxGeneration, GCCollectionMode.Forced, blocking: true, compacting: true);
                }
                catch { /* diagnostic checkpoint remains best-effort */ }
                var gcAfter = GC.GetTotalMemory(false) / Mib;
                var footprintAfterGc = IosMemory.TryRead(out var afterGcVm) && afterGcVm.Footprint > 0
                    ? afterGcVm.Footprint / Mib
                    : mb;
                tracker = VpnHood.Core.Toolkit.Memory.VhTypeTracker.GetSnapshotString();

                var phase = "IDLE_CHECKPOINT";
                if (!_baselineFootprint.HasValue || !_baselineNative.HasValue) {
                    _baselineFootprint = _idleMinFootprint;
                    _baselineNative = _idleMinNative;
                    phase = "BASELINE";
                }

                var footprintDelta = _idleMinFootprint - _baselineFootprint.Value;
                var nativeDelta = _idleMinNative - _baselineNative.Value;
                File.AppendAllText(logPath,
                    $"{DateTime.UtcNow:HH:mm:ss.fff} [MemoryProbe] cycle={_cycle} {phase} idle=30s " +
                    $"footprint_min={_idleMinFootprint:F2}MB native_min={_idleMinNative:F2}MB " +
                    $"footprint_delta={(footprintDelta >= 0 ? "+" : "")}{footprintDelta:F2}MB " +
                    $"native_delta={(nativeDelta >= 0 ? "+" : "")}{nativeDelta:F2}MB " +
                    $"gc_before={gcBefore:F2}MB gc_after={gcAfter:F2}MB " +
                    $"footprint_after_gc={footprintAfterGc:F2}MB " +
                    $"conn={conn} fds={fileDescriptors} qConnections={qConnections} qStreams={qStreams} " +
                    $"tracker={tracker}\n");
                _idleCheckpointLogged = true;

                // Region census at the baseline and at every +2 MB ratchet of the idle native floor —
                // IOS-MEM-002: names the owner (malloc size-class / skywalk / libnetwork / stack / tag0)
                // of the 14.6 -> ~35 MB creep that per-object tracking cannot see.
                if (_lastCensusNative == double.MinValue || _idleMinNative - _lastCensusNative >= 2.0) {
                    _lastCensusNative = _idleMinNative;
                    _lastCensusTicks = nowTicks;
                    File.AppendAllText(logPath,
                        $"{DateTime.UtcNow:HH:mm:ss.fff} [MemoryProbe] REGION-CENSUS reason={phase.ToLowerInvariant()} " +
                        $"native_min={_idleMinNative:F2}MB fdTypes={IosMemory.TryReadFileDescriptorSummary()} " +
                        $"{IosMemory.ReadRegionCensus()}\n");
                }
            }

            _wasTrafficActive = false;
        }
        else {
            if (!_wasTrafficActive) {
                _cycle++;
                File.AppendAllText(logPath,
                    $"{DateTime.UtcNow:HH:mm:ss.fff} [MemoryProbe] cycle={_cycle} WORK_START " +
                    $"rate={trafficBytesPerSecond / 1024:F1}KB/s tracker={tracker}\n");
            }

            _wasTrafficActive = true;
            _idleStartedTicks = 0;
        }

        // Compact OS-log heartbeat for live Console.app inspection. Keep the full object census in the
        // file-only line below; duplicating it through os_log would add noise and diagnostic allocations.
        VhLogger.Instance.LogDebug(
            "[VH-MEM-5S] footprint={Footprint:F1}MB peak={Peak:F1}MB gcLive={GcLive:F1}MB " +
            "gcCommit={GcCommit:F1}MB anon={Anon:F1}MB comp={Compressed:F1}MB " +
            "conn={Connections} fds={FileDescriptors} qConnections={QuicConnections} qStreams={QuicStreams} rdBatchMax={ReadBatchMax} " +
            "appMsg={AppMessageStarted}/{AppMessageCompleted} dn={Download:F1}MB up={Upload:F1}MB " +
            "rate={Rate:F1}KB/s",
            mb, peakMb, gcLive, gcCommit, anon, comp, conn, fileDescriptors, qConnections, qStreams, rdBatchMax,
            appMsgStarted, appMsgCompleted, dnMb, upMb, trafficBytesPerSecond / 1024);

        File.AppendAllText(logPath,
            $"{DateTime.UtcNow:HH:mm:ss.fff} footprint={mb:F1}MB peak={peakMb:F1}MB avail={availMb:F1} " +
            $"sqlite={sqliteKb:F0}KB " +
            $"gcLive={gcLive:F1} gcHeap={gcHeap:F1} gcCommit={gcCommit:F1} native={native:F1} " +
            $"anon={anon:F1} comp={comp:F1} code={code:F1} " +
            $"conn={conn} est={est} peakConn={peakConn} fds={fileDescriptors} qConnections={qConnections} qStreams={qStreams} sendQ={sendQ:F2}MB pipeBuf={pipeBuf:F1}MB win={winKb}KB maxC={maxC} " +
            $"rdAge={rdAge} rdCallbacks={rdCallbacks} rdPackets={rdPackets} rdBatchMax={rdBatchMax} " +
            $"wrAge={wrAge} wrMax={wrMax} cancelMax={cancelMax} " +
            $"appMsg={appMsgStarted}/{appMsgCompleted} " +
            $"dn={dnMb:F1}MB up={upMb:F1}MB rate={trafficBytesPerSecond / 1024:F1}KB/s " +
            $"tracker={tracker}" +
            (mb >= 50 ? " <<< NEAR 52MB JETSAM" : "") + "\n");

        // Burst census: while a climb is in flight (the pre-jetsam window), capture the owner every 3 s.
        if (mb >= 46 && nowTicks - _lastCensusTicks >= 3000) {
            _lastCensusTicks = nowTicks;
            File.AppendAllText(logPath,
                $"{DateTime.UtcNow:HH:mm:ss.fff} [MemoryProbe] REGION-CENSUS reason=burst " +
                $"footprint={mb:F1}MB avail={availMb:F1}MB fdTypes={IosMemory.TryReadFileDescriptorSummary()} " +
                $"{IosMemory.ReadRegionCensus()}\n");
        }
    }
}
