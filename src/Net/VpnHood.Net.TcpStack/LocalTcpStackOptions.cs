using System.IO.Pipelines;

namespace VpnHood.Net.TcpStack;

/// <summary>
/// Provides the options of a <see cref="LocalTcpStack"/>: its windows, buffers, timeouts and connection limits, so its
/// memory can be sized per platform.
/// </summary>
/// <remarks>
/// The stack checks the options when it is made and throws <see cref="ArgumentException"/> for one out of its range.
/// The buffer and window defaults are the full-size values Android throughput was tuned against: change them only with
/// a throughput measurement. The retransmission timeout backs off as RFC 6298 has it, and the delayed-ACK default stays
/// under the 200 millisecond minimum RTO of Linux and Android peers.
/// </remarks>
public sealed class LocalTcpStackOptions
{
    // ---- Receive / Upload buffering (tun -> stack -> app stream) ----

    /// <summary>
    /// Gets the receive window each connection advertises, in bytes: how much data the peer may send that the app has
    /// not read yet. It is the main receive-side memory of a connection.
    /// </summary>
    /// <remarks>
    /// It must be from 1 to 65535: the stack advertises no window scaling. The default, which the <see cref="Ios"/>
    /// preset keeps too, is 65535.
    /// </remarks>
    public int ReceiveWindowSize { get; init; } = 0xFFFF;

    // ---- Send / Download buffering (app stream -> stack -> tun) ----

    /// <summary>
    /// Gets the size of each connection's send buffer, in bytes, which holds what is sent until the peer acknowledges
    /// it: no more than this is in flight. It is the main send-side memory of a connection.
    /// </summary>
    /// <remarks>
    /// It must be at least <see cref="MaxMss"/>. The default is 64 KB; the <see cref="Ios"/> preset's is 16 KB.
    /// </remarks>
    public int RetransmitBufferSize { get; init; } = 64 * 1024;

    // ---- Segmentation ----

    /// <summary>
    /// Gets the largest segment the stack sends, in bytes, which it also advertises in its SYN-ACK; a larger MSS from
    /// the peer is lowered to it.
    /// </summary>
    /// <remarks>It must be at least <see cref="DefaultMss"/>. The default is 1460, an Ethernet frame's.</remarks>
    public ushort MaxMss { get; init; } = 1460;

    /// <summary>
    /// Gets the segment size the stack sends with when the peer's SYN advertises none, in bytes.
    /// </summary>
    /// <remarks>
    /// It must be from <see cref="MinMss"/> to <see cref="MaxMss"/>. The default is 536, RFC 879's.
    /// </remarks>
    public ushort DefaultMss { get; init; } = 536;

    /// <summary>
    /// Gets the smallest segment size the stack takes from the peer, in bytes; a smaller advertised MSS is raised to it.
    /// </summary>
    /// <remarks>It must be from 1 to <see cref="DefaultMss"/>. The default is 64.</remarks>
    public ushort MinMss { get; init; } = 64;

    // ---- Timeouts ----

    /// <summary>
    /// Gets how long a connection may go without sending or receiving before the stack closes it. The default is
    /// 15 minutes.
    /// </summary>
    /// <remarks>
    /// It must be longer than <see cref="IdleCheckInterval"/>, the interval it is checked at, so a connection nominally
    /// stays up to one interval longer. Once the app has closed its stream, idle means no progress in the sequence
    /// numbers, which each check samples, so the connection may stay almost two intervals longer; acknowledgments that
    /// move nothing do not count. A lower value frees memory sooner.
    /// </remarks>
    public TimeSpan IdleTimeout { get; init; } = TimeSpan.FromMinutes(15);

    /// <summary>
    /// Gets how often the stack checks its connections against <see cref="IdleTimeout"/>. A longer interval wakes the
    /// device less often. The default is 1 minute.
    /// </summary>
    /// <remarks>It must be from 1 millisecond to 49 days, and shorter than <see cref="IdleTimeout"/>.</remarks>
    public TimeSpan IdleCheckInterval { get; init; } = TimeSpan.FromMinutes(1);

    /// <summary>
    /// Gets how long the stack waits while the peer's window is zero before it sends a zero-window probe. The default
    /// is 200 milliseconds.
    /// </summary>
    /// <remarks>It must be more than zero.</remarks>
    public TimeSpan ZeroWindowProbeInterval { get; init; } = TimeSpan.FromMilliseconds(200);

    // ---- Loss recovery ----

    /// <summary>
    /// Gets the first retransmission timeout (RTO): when the peer acknowledges nothing new for this long while a SYN-ACK,
    /// data or a FIN is unacknowledged, the stack sends the oldest unacknowledged segment again and doubles the timeout,
    /// up to <see cref="RetransmitMaxTimeout"/>. The default is 500 milliseconds.
    /// </summary>
    /// <remarks>
    /// It is the only recovery when the last segment of a burst is lost, as fast retransmit needs later segments to draw
    /// duplicate ACKs. Keep it well above the peer's delayed ACK, which commonly holds an acknowledgment for up to
    /// 200 milliseconds. The stack checks it every 100 milliseconds, so a retransmission nominally comes up to that much later, and
    /// later still when the stack is busy. It
    /// must be at least 1 millisecond.
    /// </remarks>
    public TimeSpan RetransmitTimeout { get; init; } = TimeSpan.FromMilliseconds(500);

    /// <summary>
    /// Gets the longest the doubled retransmission timeout grows. A handshake whose SYN-ACK backoff has reached it is
    /// given up with a reset at the next expiry; equal to <see cref="RetransmitTimeout"/>, it leaves a handshake no
    /// retransmission at all. The default is 4 seconds.
    /// </summary>
    /// <remarks>It must be at least <see cref="RetransmitTimeout"/>.</remarks>
    public TimeSpan RetransmitMaxTimeout { get; init; } = TimeSpan.FromSeconds(4);

    /// <summary>
    /// Gets how long a delayed ACK waits before the stack sends it, at its next check: the stack checks every
    /// 100 milliseconds. The default is 50 milliseconds, so the ACK nominally goes out 50 to 150 milliseconds after the
    /// segment, later when the stack is busy, or sooner when another segment draws it: under the 200 millisecond minimum
    /// RTO of a Linux or Android peer, which a later ACK would trip into a needless retransmission.
    /// </summary>
    /// <remarks>
    /// It must be more than zero and at most 500 milliseconds, RFC 1122's delayed-ACK bound, which the check interval
    /// and a busy stack may exceed.
    /// </remarks>
    public TimeSpan DelayedAckTimeout { get; init; } = TimeSpan.FromMilliseconds(50);

    // ---- Aggregate bounds ----

    /// <summary>
    /// Gets the most connections the stack holds at once, handshakes included, or 0, the default, for no limit.
    /// </summary>
    /// <remarks>
    /// When the stack is full, a new connection's SYN takes the place of an idle connection the stack closes for it
    /// (<see cref="EvictionMinIdle"/>), or else is answered with a reset. While memory is short
    /// (<see cref="AdmissionMemoryLimitMb"/>), SYNs are dropped before this check. A limit bounds the live connections'
    /// buffers to about (<see cref="ReceiveWindowSize"/> + <see cref="RetransmitBufferSize"/>) × this value, besides
    /// allocator overhead and what closed connections' streams still hold. It must be 0 or more.
    /// </remarks>
    public int MaxConnections { get; init; }

    /// <summary>
    /// Gets how long a connection must have been idle, neither sending nor receiving, before the stack may close it to
    /// admit a new one when <see cref="MaxConnections"/> is reached. The default, <see cref="TimeSpan.MaxValue"/>, closes
    /// none: a full stack answers a new SYN with a reset.
    /// </summary>
    /// <remarks>
    /// The stack closes the connection idle the longest, if it has been idle this long; its stream reads as ended, so
    /// what forwards it ends too. Once the app has closed a connection's stream, idle means no progress in the sequence
    /// numbers. With no such connection, the new SYN is answered with a reset. A connection that turns
    /// active while it is chosen may still be closed, and with zero, any connection may be. It is used only with
    /// <see cref="MaxConnections"/> above 0, and must be zero or more.
    /// </remarks>
    public TimeSpan EvictionMinIdle { get; init; } = TimeSpan.MaxValue;

    /// <summary>
    /// Gets how many connections that completed their handshake a listener holds until they are accepted, or 0 or less,
    /// the default, for no limit. A listener whose queue is full closes a new connection instead.
    /// </summary>
    public int AcceptQueueCapacity { get; init; }

    /// <summary>
    /// Gets the process footprint, in megabytes, at or above which the stack drops new SYNs silently, with no reset, or
    /// <see langword="null"/>, the default, for no such limit.
    /// </summary>
    /// <remarks>
    /// The footprint is read from <c>VhMemory.Instance</c>, the figure a host's memory limit is enforced against, such as
    /// iOS's phys_footprint; with no memory reader installed, there is no limit. Each admitted connection costs memory
    /// outside the stack's own buffers, such as a QUIC stream or a TLS connection to the server, so under memory pressure
    /// closing one connection for another only churns. The peer sends a dropped SYN again, about a second later, when the
    /// footprint may have receded; existing connections keep working.
    /// </remarks>
    public double? AdmissionMemoryLimitMb { get; init; }

    // ---- Presets ----

    /// <summary>
    /// Gets new full-size options, for desktops and Android, where throughput matters and memory is plentiful.
    /// </summary>
    public static LocalTcpStackOptions Default => new();

    /// <summary>
    /// Gets new small-footprint options for an iOS Network Extension's tight memory: starting points, to tune against
    /// real traffic.
    /// </summary>
    /// <remarks>
    /// An estimate for live connections: on the receive side, 40 connections × 64 KB window = 2.5 MB unread at most,
    /// about 3.6 MB in the pipes' 4 KB segments; on the send side, 40 × 16 KB = 0.6 MB; about 4.2 MB in all. It leaves
    /// out allocator overhead, what closed connections' streams still hold, and memory outside the stack, such as QUIC
    /// streams.
    /// </remarks>
    public static LocalTcpStackOptions Ios => new() {
        // The 16-bit max window (no scaling) so a few active flows get full upload/download speed
        // (throughput ≈ window / RTT); MaxConnections bounds the aggregate: 40 × 64 KB = 2.5 MB unread at
        // most, and a flow holds unread bytes only while its app lags behind.
        ReceiveWindowSize = 0xFFFF,
        RetransmitBufferSize = 16 * 1024,
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
        IdleCheckInterval = TimeSpan.FromSeconds(5), // Check frequently to keep memory footprint bounded
        // Defer NEW flows while phys_footprint is critical (same threshold the QUIC ingest gate closes
        // at). 2026-07-11 on-device: with download ingest fully gated, a browse burst's establishment
        // stampede (12+ fallback TLS connections in 500 ms) still climbed native ~4-5 MB/s into the
        // jetsam kill — establishment cost is the one flow-driven consumer no other bound covers.
        AdmissionMemoryLimitMb = 42.0
    };

    /// <summary>
    /// Returns the options for the current platform: <see cref="Ios"/> on iOS and tvOS, whose network extensions are
    /// memory-capped, and <see cref="Default"/> everywhere else, Mac Catalyst included. A stack made without options uses
    /// them.
    /// </summary>
    /// <remarks>
    /// The choice goes by OS family, not by a memory probe: jetsam enforces an iOS extension's limit, and
    /// <see cref="GC.GetGCMemoryInfo()"/> reports the device's whole memory.
    /// </remarks>
    /// <returns>New options for the current platform.</returns>
    public static LocalTcpStackOptions ForCurrentPlatform()
    {
        // Note: IsIOS() can also report true under Mac Catalyst; exclude it (Catalyst is desktop-class).
        var isAppleMobile = !OperatingSystem.IsMacCatalyst() &&
                            (OperatingSystem.IsIOS() || OperatingSystem.IsTvOS());
        return isAppleMobile ? Ios : Default;
    }

    /// <summary>
    /// Builds the (immutable) <see cref="PipeOptions"/> for the network→app reassembly pipe.
    /// One instance is created per stack and shared by all of its connections.
    /// The pause threshold sits one <see cref="MaxMss"/> ABOVE <see cref="ReceiveWindowSize"/>: the
    /// stack hard-enforces the advertised window before writing (see TryHandleIncoming), so the
    /// writer never legitimately reaches the pause threshold and its fire-and-forget FlushAsync
    /// always completes synchronously (a discarded completed ValueTask is safe; a discarded PENDING
    /// pipe ValueTask would violate the PipeWriter single-operation contract). Never paused, the writer
    /// never resumes either: its resume threshold is the pause threshold, the highest the pipe takes.
    /// </summary>
    internal PipeOptions CreatePipeOptions() => new(
        pauseWriterThreshold: ReceiveWindowSize + MaxMss,
        resumeWriterThreshold: ReceiveWindowSize + MaxMss,
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

        if (RetransmitBufferSize < MaxMss)
            throw new ArgumentException(
                $"{nameof(RetransmitBufferSize)} ({RetransmitBufferSize}) must be at least {nameof(MaxMss)} ({MaxMss}).");

        if (MinMss < 1 || MinMss > DefaultMss || DefaultMss > MaxMss)
            throw new ArgumentException(
                $"MSS values must satisfy 0 < {nameof(MinMss)} ≤ {nameof(DefaultMss)} ≤ {nameof(MaxMss)}; was {MinMss}/{DefaultMss}/{MaxMss}.");

        if (ZeroWindowProbeInterval <= TimeSpan.Zero)
            throw new ArgumentException("ZeroWindowProbeInterval must be positive.");

        // a Timer's range: under 1 ms it would be a one-shot, beyond 49 days Timer.Change throws
        if (IdleCheckInterval < TimeSpan.FromMilliseconds(1) || IdleCheckInterval.TotalMilliseconds > uint.MaxValue - 1)
            throw new ArgumentException($"{nameof(IdleCheckInterval)} must be between 1 ms and 49 days; was {IdleCheckInterval}.");

        // the RTO is kept in whole milliseconds: under 1 ms it would be 0, never back off and never reach its cap
        if (RetransmitTimeout < TimeSpan.FromMilliseconds(1) || RetransmitMaxTimeout < RetransmitTimeout)
            throw new ArgumentException(
                $"{nameof(RetransmitTimeout)} must be at least 1 ms and {nameof(RetransmitMaxTimeout)} must be ≥ {nameof(RetransmitTimeout)}; " +
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
