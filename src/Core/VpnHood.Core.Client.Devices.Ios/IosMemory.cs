using System.Runtime.InteropServices;
using VpnHood.Core.Toolkit.Memory;

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
    private const int TaskVmInfo = 22;          // mach/task_info.h flavor
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

    /// <summary>
    /// Live memory snapshot for the current process: footprint (mach phys_footprint) and total system memory
    /// (NSProcessInfo.PhysicalMemory). System-wide used memory is not read here, so it stays <c>null</c>.
    /// Reads fresh each call (no background sampler); cheap enough for the QUIC read hot path.
    /// </summary>
    public override VhMemoryInfo GetInfo()
    {
        var footprint = TryRead(out var vm) && vm.Footprint > 0 ? vm.Footprint : (long?)null;

        long? total = null;
        try {
            total = (long)NSProcessInfo.ProcessInfo.PhysicalMemory;
        }
        catch { /* best-effort */ }

        return new VhMemoryInfo {
            ProcessFootprintBytes = footprint,
            TotalBytes = total,
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
}
