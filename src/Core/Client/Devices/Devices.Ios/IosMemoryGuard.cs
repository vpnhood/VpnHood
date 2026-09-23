namespace VpnHood.Core.Client.Devices.Ios;

/// <summary>
/// Active memory-maintenance system for the iOS Network Extension — NOT diagnostics. The extension runs
/// under a hard ~52 MB jetsam limit; this keeps the process alive by draining memory, so it must run in
/// production and is never gated by any diagnostics switch.
/// </summary>
/// <remarks>
/// Under heavy FULL-TUNNEL download traffic the read path (OnPacketsReceived) allocates a parsed IpPacket
/// + a per-packet byte[] (NSData.ToArray) thousands of times per second, so the live managed heap can jump
/// ~5 MB within a few hundred ms and push phys_footprint past the limit. This forces a full GC + finalizer
/// drain whenever the live heap crosses a low threshold (and periodically otherwise) so the peak stays well
/// below the limit. Native NSObject peers that escaped Dispose are only reclaimed on finalization, so
/// WaitForPendingFinalizers is essential.
/// <para>
/// REQUIRED — do not remove. Device-measured (2026-06-14): disabling this crashes the extension IMMEDIATELY
/// on tunnel start, even with MONO_GC_PARAMS soft-heap-limit=8m + System.GC.ConserveMemory=9 and the
/// adapter's deterministic NSData Dispose. (Regression: removed in 9e9e05c80; restored — without it full
/// tunnel jetsam under load while the memory-light DNS-only device-split mode did not, which is why it
/// "worked before".)
/// </para>
/// </remarks>
internal static class IosMemoryGuard
{
    private static bool _started;
    /// <summary>Starts the GC heartbeat (idempotent). Call once from <see cref="IosVpnService.StartTunnel"/>.</summary>
    public static void Start()
    {
        if (Interlocked.Exchange(ref _started, true))
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
}
