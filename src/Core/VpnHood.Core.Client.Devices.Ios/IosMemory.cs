using System.Runtime.InteropServices;
using VpnHood.Core.Toolkit.Memory;
// ReSharper disable CommentTypo
// ReSharper disable StringLiteralTypo
// ReSharper disable IdentifierTypo

namespace VpnHood.Core.Client.Devices.Ios;

/// <summary>
/// iOS <see cref="VhMemory"/> implementation. It reads on demand (no background sampler, no cached global):
/// each call issues fresh mach/Foundation queries and returns the live values. The process footprint comes
/// from mach TASK_VM_INFO (<c>phys_footprint</c> — the number the ~52 MB Network Extension jetsam limit is
/// enforced on); total system memory from <c>NSProcessInfo.PhysicalMemory</c>.
/// </summary>
/// <remarks>
/// A host installs it as <see cref="VhMemory.Instance"/> via <see cref="Install"/> at init — in <b>both</b>
/// processes: the app (<c>IosDevice</c> constructor) and the extension (<c>IosVpnService.StartTunnel</c>) — and
/// each reports its OWN process's footprint, so it works wherever it's asked. Consumers (the iOS QUIC download
/// brake in IosQuicStream and the TCP stack's +CONN/-CONN log annotation) then read live values without any
/// dependency on this class. The gated <c>ext-mem.log</c> probe (<see cref="IosMemoryMonitor"/>) also reads the
/// fuller iOS-specific breakdown via <see cref="TryRead"/> on its own thread. System-wide "used" memory is left
/// <c>null</c> here (it needs <c>host_statistics</c>, not worth the native struct parsing); a non-iOS host can
/// fill it from <c>GC.GetGCMemoryInfo()</c>.
/// </remarks>
internal sealed class IosMemory : VhMemory
{
    // Per-thread scratch buffer for task_info so a fresh read on the QUIC hot path allocates nothing.
    [ThreadStatic] private static byte[]? _buffer;

    // mach TASK_VM_INFO interop — the source of phys_footprint (the number iOS jetsam enforces).
    [DllImport("__Internal")] private static extern int task_info(uint task, int flavor, byte[] taskInfo, ref uint count);
    [DllImport("__Internal")] private static extern uint mach_task_self();
    [DllImport("/usr/lib/libproc.dylib")]
    private static extern int proc_pidinfo(int pid, int flavor, ulong arg, byte[] buffer, int bufferSize);
    // Remaining allocatable bytes before the jetsam limit (os/proc.h, iOS 13+) — footprint + available
    // gives the MEASURED limit instead of the assumed ~52 MB.
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern nuint os_proc_available_memory();
    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int sysctlbyname(string name, byte[] oldp, ref nint oldlenp, IntPtr newp, nint newlen);
    // Region walk for the native-owner census (who owns the anonymous dirty memory, by VM user tag).
    [DllImport("__Internal")]
    private static extern int vm_region_recurse_64(uint targetTask, ref ulong address, ref ulong size,
        ref uint nestingDepth, byte[] info, ref uint infoCnt);
    private const int TaskVmInfo = 22;          // mach/task_info.h flavor
    private const int ProcPidListFds = 1;
    private const int ProcFdInfoSize = 8;
    private const double Mib = 1024.0 * 1024.0;
    private static readonly byte[] FdBuffer = new byte[64 * 1024];
    // task_vm_info_data_t field byte offsets (arm64). phys_footprint @144 is verified on-device.
    private const int InternalOffset = 48;        // anonymous dirty memory: malloc, GC heap, thread stacks, buffers
    private const int ExternalOffset = 64;        // file-backed: code/dylibs/AOT — NOT counted in phys_footprint
    private const int CompressedOffset = 120;     // compressed anonymous pages — IS counted in phys_footprint
    private const int PhysFootprintOffset = 144;  // the exact number iOS jetsam enforces

    /// <summary>A single phys_footprint reading (bytes). All fields come from one TASK_VM_INFO snapshot.</summary>
    public readonly struct FootprintInfo(long footprint, long @internal, long compressed, long external)
    {
        public long Footprint { get; } = footprint;   // phys_footprint — the number jetsam enforces
        public long Internal { get; } = @internal;     // anonymous dirty (malloc/heap/stacks/buffers)
        public long Compressed { get; } = compressed;  // compressed anonymous pages
        public long External { get; } = external;      // file-backed (code/dylibs/AOT — not in footprint)
    }

    /// <summary>
    /// Installs an <see cref="IosMemory"/> as <see cref="VhMemory.Instance"/>, unless one is already installed
    /// ("if not created"). Idempotent; call once from each process's init (the app device / the extension
    /// service). A host may override <see cref="VhMemory.Instance"/> afterwards.
    /// </summary>
    public static void Install()
    {
        if (Instance is not IosMemory)
            Instance = new IosMemory();
    }

    // Total system memory never changes while the process runs — read it ONCE. GetInfo is called from
    // hot paths (per QUIC receive-arm, per SYN under the admission gate, gate watcher polls), and
    // NSProcessInfo.ProcessInfo is an Objective-C round-trip per call; the footprint itself stays a pure
    // mach task_info read.
    private static long? _totalMemory;

    public override VhMemoryInfo GetInfo()
    {
        var footprint = TryRead(out var vm) && vm.Footprint > 0 ? vm.Footprint : (long?)null;

        try {
            _totalMemory ??= (long)NSProcessInfo.ProcessInfo.PhysicalMemory;
        }
        catch { /* best-effort */ }

        return new VhMemoryInfo {
            ProcessFootprintBytes = footprint,
            TotalBytes = _totalMemory,
            UsedBytes = null
        };
    }

    /// <summary>Reads a fresh phys_footprint snapshot. Returns false if the mach call fails.</summary>
    public static bool TryRead(out FootprintInfo info)
    {
        info = default;
        var buffer = _buffer ??= new byte[512];
        var count = (uint)(buffer.Length / 4);
        if (task_info(mach_task_self(), TaskVmInfo, buffer, ref count) != 0)
            return false;
        info = new FootprintInfo(
            footprint: BitConverter.ToInt64(buffer, PhysFootprintOffset),
            @internal: BitConverter.ToInt64(buffer, InternalOffset),
            compressed: BitConverter.ToInt64(buffer, CompressedOffset),
            external: BitConverter.ToInt64(buffer, ExternalOffset));
        return true;
    }

    /// <summary>Returns the number of open process file descriptors, including sockets.</summary>
    public static int TryReadFileDescriptorCount()
    {
        try {
            var bytes = proc_pidinfo(Environment.ProcessId, ProcPidListFds, 0, FdBuffer, FdBuffer.Length);
            return bytes > 0 ? bytes / ProcFdInfoSize : -1;
        }
        catch {
            return -1;
        }
    }

    /// <summary>
    /// Buckets the open file descriptors by type (sock/vnode/kqueue/pipe/...), highest count first.
    /// Names a fd leak's owner class at a glance (e.g. leaked SQLite connections are vnodes, leaked
    /// sockets are sock). Returns "?" on failure.
    /// </summary>
    public static string TryReadFileDescriptorSummary()
    {
        try {
            var bytes = proc_pidinfo(Environment.ProcessId, ProcPidListFds, 0, FdBuffer, FdBuffer.Length);
            if (bytes <= 0)
                return "?";

            var counts = new Dictionary<uint, int>();
            for (var offset = 0; offset + ProcFdInfoSize <= bytes; offset += ProcFdInfoSize) {
                var type = BitConverter.ToUInt32(FdBuffer, offset + 4); // proc_fdinfo.proc_fdtype
                counts[type] = counts.GetValueOrDefault(type) + 1;
            }

            return string.Join(',', counts
                .OrderByDescending(kv => kv.Value)
                .Select(kv => $"{FdTypeName(kv.Key)}:{kv.Value}"));
        }
        catch {
            return "?";
        }
    }

    // PROX_FDTYPE_* (sys/proc_info.h)
    private static string FdTypeName(uint type) => type switch {
        1 => "vnode",
        2 => "sock",
        3 => "pshm",
        4 => "psem",
        5 => "kq",
        6 => "pipe",
        7 => "fsevents",
        9 => "netpolicy",
        10 => "chan",
        11 => "nexus",
        _ => $"t{type}"
    };

    /// <summary>
    /// Bytes this process can still allocate before jetsam (<c>os_proc_available_memory</c>). -1 on failure;
    /// 0 can mean either "no budget applies" or "budget exhausted" — read it together with the footprint.
    /// </summary>
    public static long TryReadAvailableMemory()
    {
        try {
            return (long)os_proc_available_memory();
        }
        catch {
            return -1;
        }
    }

    /// <summary>Device boot time (UTC) from <c>kern.boottime</c>. Newer than a log line = the device rebooted.</summary>
    public static DateTime? TryReadBootTimeUtc()
    {
        try {
            var buffer = new byte[16]; // struct timeval { long tv_sec; int tv_usec; } on arm64
            nint length = buffer.Length;
            if (sysctlbyname("kern.boottime", buffer, ref length, IntPtr.Zero, 0) != 0 || length < 8)
                return null;
            return DateTimeOffset.FromUnixTimeSeconds(BitConverter.ToInt64(buffer, 0)).UtcDateTime;
        }
        catch {
            return null;
        }
    }

    /// <summary>
    /// Walks the task's VM regions and buckets dirty+compressed bytes by VM user tag — the ground truth for
    /// which native owner (malloc size-class, network buffers, stacks, GC maps at tag0) holds the footprint.
    /// Returns a single log-line payload; never throws.
    /// </summary>
    /// <remarks>
    /// vm_region_submap_info_64 is pack(4); the offsets below (user_tag@20, resident@24, swapped@32,
    /// dirtied@36, is_submap@48) were verified on-device (iPhone 11, arm64, 16 KB pages) in the 2026-07
    /// diagnostic runs. infoCnt 19 = V2 count; falls back to 16 (V0) for older kernels.
    /// </remarks>
    public static string ReadRegionCensus()
    {
        try {
            var totals = WalkRegions(infoCount: 19);
            if (totals.Count == 0)
                totals = WalkRegions(infoCount: 16);
            if (totals.Count == 0)
                return "unavailable";

            var parts = totals
                .OrderByDescending(kv => kv.Value)
                .Where(kv => kv.Value >= 0.05 * Mib)
                .Take(14)
                .Select(kv => $"{TagName(kv.Key)}={kv.Value / Mib:F1}");
            return $"total={totals.Values.Sum() / Mib:F1}MB {string.Join(' ', parts)}";
        }
        catch (Exception ex) {
            return $"failed:{ex.GetType().Name}";
        }
    }

    private static Dictionary<uint, long> WalkRegions(uint infoCount)
    {
        const int pageSize = 16384;
        var totals = new Dictionary<uint, long>();
        var info = new byte[128];
        var task = mach_task_self();
        ulong address = 0;
        uint depth = 0;
        for (var i = 0; i < 8192; i++) {
            ulong size = 0;
            var count = infoCount;
            if (vm_region_recurse_64(task, ref address, ref size, ref depth, info, ref count) != 0)
                break;
            if (BitConverter.ToInt32(info, 48) != 0) { // is_submap: descend without advancing
                depth++;
                continue;
            }

            var tag = BitConverter.ToUInt32(info, 20);
            var swapped = BitConverter.ToUInt32(info, 32);  // compressed pages
            var dirtied = BitConverter.ToUInt32(info, 36);
            var bytes = ((long)dirtied + swapped) * pageSize;
            if (bytes > 0)
                totals[tag] = totals.GetValueOrDefault(tag) + bytes;
            address += size;
        }

        return totals;
    }

    // VM_MEMORY_* user tags (mach/vm_statistics.h). tag0 = untagged mmap: CoreCLR runtime/GC maps.
    private static string TagName(uint tag) => tag switch {
        0 => "tag0",
        1 => "malloc",
        2 => "malloc_small",
        3 => "malloc_large",
        4 => "malloc_huge",
        6 => "realloc",
        7 => "malloc_tiny",
        8 => "malloc_lg_reusable",
        9 => "malloc_lg_reused",
        11 => "malloc_nano",
        12 => "malloc_medium",
        20 => "mach_msg",
        21 => "iokit",
        30 => "stack",
        31 => "guard",
        33 => "dylib",
        41 => "foundation",
        42 => "coregraphics",
        45 => "coredata",
        54 => "layerkit",
        70 => "dyld",
        71 => "dyld_malloc",
        72 => "sqlite",
        99 => "os_alloc_once",
        100 => "libdispatch",
        102 => "skywalk",
        103 => "iosurface",
        104 => "libnetwork",
        105 => "audio",
        _ => $"tag{tag}"
    };
}
