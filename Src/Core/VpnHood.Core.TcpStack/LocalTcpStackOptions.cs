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
    /// <para>Footprint math: ~(16 KB receive + 16 KB retx) = ~32 KB per connection × 256 max
    /// ≈ 8 MB worst case for TCP buffers.</para>
    /// </summary>
    public static LocalTcpStackOptions Ios => new() {
        // Large PER-CONNECTION window (the 16-bit max, no scaling) so a few active flows get full
        // upload/download speed (throughput ≈ window / RTT), but a GLOBAL budget caps the aggregate so
        // 100+ concurrent flows can't blow the 52 MB jetsam limit. A flow only consumes window when it
        // is actively transferring; idle keep-alive sit near zero.
        ReceiveWindowSize = 0xFFFF,
        GlobalReceiveBudget = 6 * 1024 * 1024,
        RetxBufferSize = 16 * 1024,
        MaxConnections = 100, // Capped to prevent memory exhaustion under concurrent flow storms
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
    /// </summary>
    internal PipeOptions CreatePipeOptions() => new(
        pauseWriterThreshold: ReceiveWindowSize,
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

        if (RetxBufferSize < MaxMss)
            throw new ArgumentException(
                $"{nameof(RetxBufferSize)} ({RetxBufferSize}) must be at least {nameof(MaxMss)} ({MaxMss}).");

        if (MinMss < 1 || MinMss > DefaultMss || DefaultMss > MaxMss)
            throw new ArgumentException(
                $"MSS values must satisfy 0 < {nameof(MinMss)} ≤ {nameof(DefaultMss)} ≤ {nameof(MaxMss)}; was {MinMss}/{DefaultMss}/{MaxMss}.");

        if (IdleCheckInterval <= TimeSpan.Zero || ZeroWindowProbeInterval <= TimeSpan.Zero)
            throw new ArgumentException("IdleCheckInterval and ZeroWindowProbeInterval must be positive.");

        if (IdleTimeout <= IdleCheckInterval)
            throw new ArgumentException(
                $"{nameof(IdleTimeout)} ({IdleTimeout}) must be greater than {nameof(IdleCheckInterval)} ({IdleCheckInterval}).");

        if (MaxConnections < 0)
            throw new ArgumentException($"{nameof(MaxConnections)} must be ≥ 0; was {MaxConnections}.");

        return this;
    }
}
