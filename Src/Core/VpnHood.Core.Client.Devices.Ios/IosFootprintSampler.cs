using System.Runtime.InteropServices;
using VpnHood.Core.Quic.Ios;

namespace VpnHood.Core.Client.Devices.Ios;

/// <summary>
/// Active phys_footprint sampler for the iOS Network Extension — NOT diagnostics. Every 100 ms it reads the
/// extension's phys_footprint (mach TASK_VM_INFO, the number the ~52 MB jetsam limit is enforced on) and
/// publishes it to <c>IosQuicClient.FootprintMb</c>, the input the QUIC download brake reads to avoid jetsam
/// (see IosQuicStream). It runs in production and is never gated by any diagnostics switch — disabling it
/// re-opens the crash.
/// </summary>
/// <remarks>
/// The optional <c>ext-mem.log</c> breakdown that consumes these samples is a separate, gated concern
/// (see <see cref="IosMemoryMonitor"/>), which reads footprint via <see cref="TryRead"/> rather than
/// running its own native probe. The 100 ms cadence is required: a guarded crash once spiked +6.6 MB inside
/// a single 250 ms tick, so the brake's <c>FootprintMb</c> input must refresh fast enough to catch it.
/// </remarks>
internal static class IosFootprintSampler
{
    private static bool _started;

    // mach TASK_VM_INFO interop — the source of phys_footprint (the number iOS jetsam enforces).
    [DllImport("__Internal")] private static extern int task_info(uint task, int flavor, byte[] taskInfo, ref uint count);
    [DllImport("__Internal")] private static extern uint mach_task_self();
    private const int TaskVmInfo = 22;          // mach/task_info.h flavor
    // task_vm_info_data_t field byte offsets (arm64). phys_footprint @144 is verified on-device.
    private const int InternalOffset = 48;        // anonymous dirty memory: malloc, GC heap, thread stacks, buffers
    private const int ExternalOffset = 64;        // file-backed: code/dylibs/AOT — NOT counted in phys_footprint
    private const int CompressedOffset = 120;     // compressed anonymous pages — IS counted in phys_footprint
    private const int PhysFootprintOffset = 144;  // the exact number iOS jetsam enforces

    private const double Mib = 1024.0 * 1024.0;

    /// <summary>A single phys_footprint reading (bytes). All fields come from one TASK_VM_INFO snapshot.</summary>
    public readonly struct FootprintInfo(long footprint, long @internal, long compressed, long external)
    {
        public long Footprint { get; } = footprint;   // phys_footprint — the number jetsam enforces
        public long Internal { get; } = @internal;     // anonymous dirty (malloc/heap/stacks/buffers)
        public long Compressed { get; } = compressed;  // compressed anonymous pages
        public long External { get; } = external;      // file-backed (code/dylibs/AOT — not in footprint)
    }

    /// <summary>
    /// Starts the sampler thread (idempotent). Call once from <see cref="IosVpnService.StartTunnel"/>.
    /// </summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, true))
            return;

        var thread = new Thread(() => {
            while (true) {
                try {
                    if (TryRead(out var vm) && vm.Footprint > 0)
                        // LOAD-BEARING: feed the QUIC download intake brake so it can throttle when the
                        // footprint nears the 52 MB limit (see IosQuicClient.FootprintMb).
                        IosQuicClient.FootprintMb = vm.Footprint / Mib;
                }
                catch { /* best-effort */ }
                Thread.Sleep(100);
            }
            // ReSharper disable once FunctionNeverReturns
        }) {
            IsBackground = true,
            Name = "VpnHoodExtFootprintSampler"
        };
        thread.Start();
    }

    /// <summary>Reads a fresh phys_footprint snapshot. Returns false if the mach call fails.</summary>
    public static bool TryRead(out FootprintInfo info)
    {
        info = default;
        var buffer = new byte[512];
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
