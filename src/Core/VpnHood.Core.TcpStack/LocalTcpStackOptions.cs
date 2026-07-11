using System.IO.Pipelines;

namespace VpnHood.Core.TcpStack;

/// <summary>
/// Tunable knobs for <see cref="LocalTcpStack"/>. Every value that affects memory footprint
/// is exposed here so the stack can be sized per-platform (small on memory-constrained hosts
/// such as an iOS Network Extension, full-size on desktop/Android).
/// </summary>
/// <remarks>
/// The class is immutable (init-only) and validated once via <see cref="Validated"/> when a
/// stack is constructed. The defaults reproduce the historical hardcoded constants exactly, so
/// a default-constructed instance is byte-for-byte identical to the pre-options behavior.
/// IMPORTANT: keep the defaults equal to the historical constants — Android throughput was
/// tuned against them and must not regress.
/// (Exception: the loss-recovery options — <see cref="RetransmitTimeout"/>,
/// <see cref="RetransmitMaxTimeout"/>, <see cref="DelayedAckTimeout"/> — are additive; the
/// historical code had no retransmission timer at all, so tail loss stalled until the idle timeout.)
/// </remarks>
public sealed class LocalTcpStackOptions
{
    // ---- Receive / Upload buffering (tun -> stack -> app stream) ----

    /// <summary>
    /// Advertised TCP receive window, in bytes. Also sizes the network→app reassembly pipe
    /// (its <see cref="PipeOptions.PauseWriterThreshold"/>). This is the dominant per-connection
    /// receive-side memory cost.
    /// <para>Range: 1..65535. The advertised value lives in the 16-bit TCP window field, and we
    /// do NOT advertise a window-scale shift (shift = 0), so values above 65535 are rejected.</para>
    /// Default: 65535. iOS typically shrinks this to 16–32 KB.
    /// </summary>
    public int ReceiveWindowSize { get; init; } = 0xFFFF;

    /// <summary>
    /// Resume threshold (bytes) at which the reassembly pipe writer un-pauses. Must be in the
    /// range 1..<see cref="ReceiveWindowSize"/>-1.
    /// <para><c>null</c> (default) means <see cref="ReceiveWindowSize"/> / 2, matching the
    /// historical behavior. Most callers should leave this null.</para>
    /// </summary>
    public int? PipeResumeWriterThreshold { get; init; }

    /// <summary>
    /// Global cap (bytes) on the SUM of every connection's unread reassembly-pipe backlog. Each
    /// connection's advertised window is additionally clamped by the remaining global headroom, so a
    /// FEW active flows can use a large per-connection <see cref="ReceiveWindowSize"/> (full speed)
    /// while MANY concurrent flows stay bounded in aggregate. This decouples per-flow throughput from
    /// total memory — the key to surviving 100+ concurrent connections on a memory-capped host.
    /// <para>Default: unbounded (<see cref="long.MaxValue"/>) so desktop/Android keep a pure
    /// per-connection window. Constrained hosts (iOS) set a few MB.</para>
    /// <para>NOTE: against a compliant peer this budget holds, but against a WINDOW-IGNORING sender it
    /// is advisory: the hard per-packet enforcement drops bytes beyond the per-connection
    /// <see cref="ReceiveWindowSize"/> only. Clamping enforcement by the global headroom would drop
    /// in-window data from compliant peers whenever OTHER flows fill the budget after a window was
    /// advertised (cross-flow retransmit storms), so the adversarial worst case is intentionally
    /// <see cref="ReceiveWindowSize"/> × active connections — size <see cref="MaxConnections"/>
    /// accordingly (iOS preset: 64 KB × 50 ≈ 3.2 MB, within the jetsam margin).</para>
    /// </summary>
    public long GlobalReceiveBudget { get; init; } = long.MaxValue;

    // ---- Send / Download buffering (app stream -> stack -> tun) ----

    /// <summary>
    /// Per-connection retransmission ring-buffer size, in bytes. This also caps the amount of
    /// in-flight unacknowledged data we will send before waiting for ACKs, so it is the dominant
    /// per-connection send-side memory cost.
    /// <para>Must be ≥ <see cref="MaxMss"/> (a single max-size segment has to fit).</para>
    /// Default: 64 KB. iOS typically shrinks this to 8–16 KB.
    /// </summary>
    public int RetxBufferSize { get; init; } = 64 * 1024;

    // ---- Segmentation ----

    /// <summary>
    /// Maximum segment size, in bytes. Used both as the upper clamp for the peer's advertised MSS
    /// and as the MSS we advertise in our SYN-ACK. Default: 1460 (standard Ethernet payload).
    /// </summary>
    public ushort MaxMss { get; init; } = 1460;

    /// <summary>
    /// Fallback MSS used when the peer's SYN does not advertise one. Default: 536 (RFC 879 floor).
    /// </summary>
    public ushort DefaultMss { get; init; } = 536;

    /// <summary>
    /// Lower clamp applied to a peer-advertised MSS to reject pathological values. Default: 64.
    /// </summary>
    public ushort MinMss { get; init; } = 64;

    // ---- Timeouts ----

    /// <summary>
    /// Idle timeout after which a connection with no activity is closed. Default: 15 minutes.
    /// Lowering it on constrained platforms frees connection memory sooner.
    /// </summary>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// How often the per-connection idle monitor wakes to check <see cref="IdleTimeout"/>.
    /// Larger intervals mean fewer timer wake-ups (less power). Default: 1 minute.
    /// Must be smaller than <see cref="IdleTimeout"/>.
    /// </summary>
    public TimeSpan IdleCheckInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// How long the sender waits on a zero peer-window before emitting a Zero Window Probe.
    /// Default: 200 ms.
    /// </summary>
    public TimeSpan ZeroWindowProbeInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    // ---- Loss recovery ----

    /// <summary>
    /// Initial retransmission timeout (RTO). If cumulative ACKs make no progress for this long while
    /// something is outstanding (an unacknowledged SYN-ACK, data, or FIN), the stack's maintenance
    /// sweep retransmits the oldest outstanding segment and doubles the timeout up to
    /// <see cref="RetransmitMaxTimeout"/>. This is the ONLY recovery for tail loss: fast retransmit
    /// needs later segments to arrive to generate duplicate ACKs, and the last segment of a burst has
    /// none. Loopback/TUN RTT is microseconds, but the peer may legally delay its ACK up to ~200 ms
    /// (delayed ACK), so this must stay comfortably above that. Default: 500 ms.
    /// </summary>
    public TimeSpan RetransmitTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>Upper bound for the exponentially backed-off RTO. Default: 4 seconds.</summary>
    public TimeSpan RetransmitMaxTimeout { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Maximum time a thinned (delayed) ACK may stay pending before the maintenance sweep flushes it.
    /// Keeps an odd trailing full-size non-PSH segment acknowledged well within RFC 1122's 500 ms
    /// delayed-ACK bound instead of stalling until the peer retransmits. Default: 200 ms.
    /// </summary>
    public TimeSpan DelayedAckTimeout { get; init; } = TimeSpan.FromMilliseconds(200);

    // ---- Aggregate bounds ----

    /// <summary>
    /// Maximum number of simultaneous connections. New SYNs beyond this are answered with an RST.
    /// <para><c>0</c> (default) means unbounded and short-circuits the check entirely (zero
    /// overhead on the SYN path).</para>
    /// On iOS this caps the worst-case aggregate buffer memory:
    /// roughly (<see cref="ReceiveWindowSize"/> + <see cref="RetxBufferSize"/>) × MaxConnections.
    /// </summary>
    public int MaxConnections { get; init; }

    /// <summary>
    /// When <see cref="MaxConnections"/> is reached, the minimum idle time a connection must have had
    /// (no send/receive activity) before it may be force-evicted to admit a NEW connection. The most
    /// idle ("most unused") eligible connection is closed immediately, which propagates EOF to its
    /// consumer (e.g. the proxy's QUIC stream or TCP channel) so the whole upstream flow tears down.
    /// If no connection has been idle this long, the new SYN is rejected with an RST — the historical
    /// behavior. Actively-transferring flows (idle ≈ 0) are therefore never evicted.
    /// <para><see cref="TimeSpan.MaxValue"/> (default) DISABLES eviction entirely → identical to the
    /// historical "reject when full" behavior. Only consulted when <see cref="MaxConnections"/> &gt; 0,
    /// so desktop/Android (unbounded by default) are unaffected.</para>
    /// </summary>
    public TimeSpan EvictionMinIdle { get; init; } = TimeSpan.MaxValue;

    /// <summary>
    /// Capacity of a listener's pending-accept queue. When full, additional accepted connections
    /// are rejected (and disposed) rather than buffered.
    /// <para><c>0</c> or less (default) means an unbounded queue, matching historical behavior.</para>
    /// </summary>
    public int AcceptQueueCapacity { get; init; }

    // ---- Presets ----

    /// <summary>
    /// Full-size defaults equal to the historical hardcoded constants. Use on desktop/Android,
    /// where throughput matters and memory is plentiful.
    /// </summary>
    public static LocalTcpStackOptions Default => new();

    /// <summary>
    /// Small footprint tuned for an iOS Network Extension's tight memory budget. The numbers are
    /// sensible starting points — tune against real traffic.
    /// <para>Footprint math: receive side is capped by the 6 MB global budget (not per-connection:
    /// 64 KB window × 50 connections only matters while the budget has headroom); send side is
    /// 16 KB retx × 50 connections = 0.8 MB. Worst case ≈ 7 MB for TCP buffers.</para>
    /// </summary>
    public static LocalTcpStackOptions Ios => new() {
        // Large PER-CONNECTION window (the 16-bit max, no scaling) so a few active flows get full
        // upload/download speed (throughput ≈ window / RTT), but a GLOBAL budget caps the aggregate so
        // 100+ concurrent flows can't blow the 52 MB jetsam limit. A flow only consumes window when it
        // is actively transferring; idle keep-alive sit near zero.
        ReceiveWindowSize = 0xFFFF,
        GlobalReceiveBudget = 6 * 1024 * 1024,
        RetxBufferSize = 16 * 1024,
        // Capped to prevent memory exhaustion under concurrent flow storms. Tuned 2026-07-02: 50 → 40.
        // Native transient spikes scale with concurrent full-rate streams, but capping too low (25)
        // backfires — a speedtest/browser wanting more connections gets SYN-RSTs and retries, churning
        // QUIC stream setup/teardown (native NWConnections; logs showed 63 live streams vs 42 connections).
        // 40 keeps concurrency high per product direction; the memory bound comes from the QUIC
        // flow-control windows (IosQuicClient), which cap in-flight download tunnel-wide.
        MaxConnections = 40,
        // Under cap pressure, evict the most-idle flow (idle ≥ 15 s) to admit a new one instead of
        // rejecting it; live transfers (idle ≈ 0) are protected. Frees the victim's QUIC stream/native
        // NWConnection immediately rather than waiting for the 20 s idle reaper.
        EvictionMinIdle = TimeSpan.FromSeconds(15),
        AcceptQueueCapacity = 128,
        IdleTimeout = TimeSpan.FromSeconds(20), // Reap idle keep-alive connections rapidly
        IdleCheckInterval = TimeSpan.FromSeconds(5) // Check frequently to keep memory footprint bounded
    };

    /// <summary>
    /// Picks the preset that fits the current platform. Apple mobile platforms (iOS / tvOS) run
    /// network code inside memory-capped Network Extensions, so they get the small
    /// <see cref="Ios"/> footprint; every other platform (Android, Windows, Linux, desktop macOS,
    /// Mac Catalyst) gets <see cref="Default"/> full-size behavior so throughput is unaffected.
    /// <para>This is what <c>new LocalTcpStack()</c> uses when no options are supplied, so callers
    /// don't have to know the platform. Detection is by OS family (not a runtime memory probe):
    /// an iOS extension's real limit is enforced by jetsam and is not visible via
    /// <see cref="GC.GetGCMemoryInfo()"/>, which reports total device RAM.</para>
    /// </summary>
    public static LocalTcpStackOptions ForCurrentPlatform()
    {
        // Note: IsIOS() can also report true under Mac Catalyst; exclude it (Catalyst is desktop-class).
        var isAppleMobile = !OperatingSystem.IsMacCatalyst() &&
                            (OperatingSystem.IsIOS() || OperatingSystem.IsTvOS());
        return isAppleMobile ? Ios : Default;
    }

    /// <summary>
    /// Resolved pipe resume threshold: the explicit value, or <see cref="ReceiveWindowSize"/> / 2.
    /// </summary>
    internal int ResolvedPipeResumeThreshold => PipeResumeWriterThreshold ?? ReceiveWindowSize / 2;

    /// <summary>
    /// Builds the (immutable) <see cref="PipeOptions"/> for the network→app reassembly pipe.
    /// One instance is created per stack and shared by all of its connections.
    /// The pause threshold sits one <see cref="MaxMss"/> ABOVE <see cref="ReceiveWindowSize"/>: the
    /// stack hard-enforces the advertised window before writing (see TryHandleIncoming), so the
    /// writer never legitimately reaches the pause threshold and its fire-and-forget FlushAsync
    /// always completes synchronously (a discarded completed ValueTask is safe; a discarded PENDING
    /// pipe ValueTask would violate the PipeWriter single-operation contract).
    /// </summary>
    internal PipeOptions CreatePipeOptions() => new(
        pauseWriterThreshold: ReceiveWindowSize + MaxMss,
        resumeWriterThreshold: ResolvedPipeResumeThreshold,
        useSynchronizationContext: false);

    /// <summary>
    /// Validates the option values and returns this same instance for chaining. Throws
    /// <see cref="ArgumentException"/> on any invalid combination. Called once at stack
    /// construction — never on the data path.
    /// </summary>
    internal LocalTcpStackOptions Validated()
    {
        // Receive window must fit the 16-bit TCP window field (we advertise no window scaling).
        if (ReceiveWindowSize is < 1 or > 0xFFFF)
            throw new ArgumentException(
                $"{nameof(ReceiveWindowSize)} must be between 1 and 65535 (no window scaling); was {ReceiveWindowSize}.");

        var resume = ResolvedPipeResumeThreshold;
        if (resume < 1 || resume >= ReceiveWindowSize)
            throw new ArgumentException(
                $"{nameof(PipeResumeWriterThreshold)} ({resume}) must be between 1 and {nameof(ReceiveWindowSize)}-1 ({ReceiveWindowSize - 1}).");

        if (GlobalReceiveBudget <= 0)
            throw new ArgumentException($"{nameof(GlobalReceiveBudget)} must be positive; was {GlobalReceiveBudget}.");

        if (RetxBufferSize < MaxMss)
            throw new ArgumentException(
                $"{nameof(RetxBufferSize)} ({RetxBufferSize}) must be at least {nameof(MaxMss)} ({MaxMss}).");

        if (MinMss < 1 || MinMss > DefaultMss || DefaultMss > MaxMss)
            throw new ArgumentException(
                $"MSS values must satisfy 0 < {nameof(MinMss)} ≤ {nameof(DefaultMss)} ≤ {nameof(MaxMss)}; was {MinMss}/{DefaultMss}/{MaxMss}.");

        if (IdleCheckInterval <= TimeSpan.Zero || ZeroWindowProbeInterval <= TimeSpan.Zero)
            throw new ArgumentException("IdleCheckInterval and ZeroWindowProbeInterval must be positive.");

        if (RetransmitTimeout <= TimeSpan.Zero || RetransmitMaxTimeout < RetransmitTimeout)
            throw new ArgumentException(
                $"{nameof(RetransmitTimeout)} must be positive and {nameof(RetransmitMaxTimeout)} must be ≥ {nameof(RetransmitTimeout)}; " +
                $"was {RetransmitTimeout}/{RetransmitMaxTimeout}.");

        if (DelayedAckTimeout <= TimeSpan.Zero || DelayedAckTimeout > TimeSpan.FromMilliseconds(500))
            throw new ArgumentException(
                $"{nameof(DelayedAckTimeout)} must be within (0, 500] ms (RFC 1122 delayed-ACK bound); was {DelayedAckTimeout}.");

        if (IdleTimeout <= IdleCheckInterval)
            throw new ArgumentException(
                $"{nameof(IdleTimeout)} ({IdleTimeout}) must be greater than {nameof(IdleCheckInterval)} ({IdleCheckInterval}).");

        if (MaxConnections < 0)
            throw new ArgumentException($"{nameof(MaxConnections)} must be ≥ 0; was {MaxConnections}.");

        if (EvictionMinIdle < TimeSpan.Zero)
            throw new ArgumentException($"{nameof(EvictionMinIdle)} must be ≥ 0; was {EvictionMinIdle}.");

        return this;
    }
}
