using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.PacketTransports;

/// <summary>
/// Provides the send queue, the sending loop, the packet ownership rules and the counters every packet transport
/// shares; a derived class sends the packets and raises the ones it receives.
/// </summary>
/// <remarks>
/// The transport takes every packet it is given. One it drops or refuses is disposed in any mode. One a send fails on,
/// or left in the queue at the end, is disposed too, except in a passthrough transport without
/// <see cref="PacketTransportOptions.AutoDisposePackets"/>, whose derived class disposes what it does not hand on. One
/// delivered is disposed with <see cref="PacketTransportOptions.AutoDisposePackets"/>. A full queue waits with
/// <see cref="PacketTransportOptions.Blocking"/>, or drops its oldest packet. A sending loop that fails disposes
/// the transport.
/// </remarks>
public abstract class PacketTransportBase : IPacketTransport
{
    // a passthrough transport sends on the sender's thread and queues nothing: it shares this closed queue and
    // runs no send loop, saving each its own
    private static readonly Channel<IpPacket> PassthroughSendChannel = CreatePassthroughSendChannel();

    // the longest a CancellationTokenSource times
    private static readonly TimeSpan MaxBlockingTimeout = TimeSpan.FromMilliseconds(uint.MaxValue - 1);

    private readonly Channel<IpPacket> _sendChannel;
    private readonly int _queueCapacity;
    private readonly bool _autoDisposePackets;
    private readonly bool _blocking;
    private readonly TimeSpan? _blockingTimeout;
    private readonly bool _singleMode;
    private readonly bool _passthrough;
    private bool _disposed;
    private bool _disposing;
    private readonly PacketTransportStat _stat = new();
    private bool _isSending;

    // The transport takes every packet it is given. One it drops or refuses went to no one, so it is disposed in
    // any mode. One a send failed on, or left in the queue at the end, is disposed whenever the transport queues,
    // as no one else holds it; in passthrough mode only with AutoDisposePackets, as the subclass may have handed
    // it on. A delivered one only with AutoDisposePackets, as a transport that hands its packets on (a tunnel)
    // must not dispose them. No disposal is guarded: a packet's Dispose only returns its buffer to the pool
    private readonly bool _disposeUnsentPackets;
    /// <summary>Gets a value indicating whether the transport is disposed.</summary>
    protected bool IsDisposed => _disposed;

    // set once disposal begins, and stays set
    /// <summary>Gets a value indicating whether the transport's disposal has begun; it stays set.</summary>
    protected bool IsDisposing => _disposing;

    // Sends a batch, one packet in single mode. It must end once the transport is disposed, as the queue's
    // last packets wait on it. A throw counts the whole batch dropped and, by the rule above, disposes it, so a
    // send must not throw after handing a packet on; a passthrough send without AutoDisposePackets disposes
    // itself what it does not hand on. In passthrough mode it must complete at once and never send on its own
    // transport, whose one-packet buffer the outer send still uses. Both rules are left unchecked by design:
    // telling which packets a failing send had handed on would take an API of its own, and catching a send
    // on its own transport would cost every packet. A failed send is packet loss, whatever the error, and the
    // loop goes on: a subclass whose resource stays broken ends itself
    /// <summary>
    /// Sends a batch of packets: one packet in single mode. It runs on the sending loop, one batch at a time; in
    /// passthrough mode, on the sender's thread.
    /// </summary>
    /// <remarks>
    /// It must end once the transport is disposed. A throw counts the whole batch dropped and disposes it, except in
    /// passthrough mode without <see cref="PacketTransportOptions.AutoDisposePackets"/>, where the derived class
    /// disposes what it does not hand on; so it must not throw after handing a packet on. In passthrough mode it must
    /// complete at once and never send on its own transport.
    /// </remarks>
    /// <param name="ipPackets">The batch. The list is reused after the call.</param>
    /// <returns>A task that represents the asynchronous operation.</returns>
    protected abstract ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets);
    /// <summary>Gets the name the transport's log lines carry. The default is its type's name.</summary>
    protected virtual string Name => VhLogger.FormatType(this);

    /// <inheritdoc/>
    public event EventHandler<IpPacket>? PacketReceived;
    /// <inheritdoc/>
    public ReadOnlyPacketTransportStat PacketStat { get; }
    // a subclass that queues its packets elsewhere counts them here
    /// <summary>Gets the number of packets waiting in the send queue.</summary>
    public virtual int QueueLength => _sendChannel.Reader.Count;

    // the flag is read again after the queue: the loop marks a batch before it empties the queue, so a batch
    // taken between the first two reads still shows
    /// <inheritdoc/>
    public bool IsSending => Volatile.Read(ref _isSending) || QueueLength > 0 || Volatile.Read(ref _isSending);

    // the batch in flight, for a stop that waits for it; read fresh each time
    /// <summary>Gets a value indicating whether a batch is being sent now, for a stop that waits for it.</summary>
    protected bool IsSendingBatch => Volatile.Read(ref _isSending);

    /// <summary>
    /// Initializes the transport and, unless it is a passthrough one, starts its sending loop.
    /// </summary>
    /// <param name="options">The queue's capacity, blocking and packet disposal.</param>
    /// <param name="singleMode"><see langword="true"/> to send one packet per batch.</param>
    /// <param name="passthrough"><see langword="true"/> to queue nothing and send on the sender's thread; it needs single mode.</param>
    /// <exception cref="ArgumentException"><paramref name="passthrough"/> is set without <paramref name="singleMode"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// The blocking timeout is negative or too long, or the queue's capacity is not above zero.
    /// </exception>
    protected PacketTransportBase(PacketTransportOptions options, bool singleMode, bool passthrough)
    {
        if (passthrough && !singleMode)
            throw new ArgumentException("Passthrough mode should be used with single mode only.", nameof(passthrough));

        // a CancellationTokenSource times each blocking wait: a timeout it refuses would fail every wait, after the
        // send took its packet
        if (options.BlockingTimeout < TimeSpan.Zero || options.BlockingTimeout > MaxBlockingTimeout)
            throw new ArgumentOutOfRangeException(nameof(options.BlockingTimeout), options.BlockingTimeout,
                $"{nameof(options.BlockingTimeout)} must be from zero to {MaxBlockingTimeout}.");

        _queueCapacity = options.QueueCapacity ?? PacketTransportOptions.DefaultQueueCapacity;
        _autoDisposePackets = options.AutoDisposePackets;
        _disposeUnsentPackets = options.AutoDisposePackets || !passthrough;
        _blocking = options.Blocking;
        _blockingTimeout = options.BlockingTimeout;
        _singleMode = singleMode;
        _passthrough = passthrough;

        // a full queue that does not block drops its oldest packet, so the newer ones behind it show the loss
        _sendChannel = passthrough
            ? PassthroughSendChannel
            : Channel.CreateBounded(new BoundedChannelOptions(_queueCapacity) {
                SingleReader = true,
                SingleWriter = false,
                FullMode = options.Blocking ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropOldest
            }, options.Blocking ? null : (Action<IpPacket>)DropQueuedPacket);

        PacketStat = new ReadOnlyPacketTransportStat(_stat);
        if (!passthrough)
            Task.Run(StartSendingPacketsAsync);
    }

    private static Channel<IpPacket> CreatePassthroughSendChannel()
    {
        var sendChannel = Channel.CreateBounded<IpPacket>(1);
        sendChannel.Writer.TryComplete();
        return sendChannel;
    }

    // The handler takes a received packet. One the transport cannot deliver (refused once disposed, with no
    // handler, or given back by a handler that threw) went to no one, so it is disposed here in any mode
    /// <summary>
    /// Counts a received packet and raises <see cref="PacketReceived"/> with it. A packet that cannot be delivered
    /// (the transport is disposed, no handler is attached, or the handler throws) is disposed.
    /// </summary>
    /// <param name="ipPacket">The packet, which the handler takes.</param>
    protected virtual void OnPacketReceived(IpPacket ipPacket)
    {
        try {
            ObjectDisposedException.ThrowIf(IsDisposed || IsDisposing, this);

            _stat.LastReceivedTime = FastDateTime.UtcNow;
            _stat.ReceivedBytes += ipPacket.PacketLength;
            _stat.ReceivedPackets++;
            LogPacket(ipPacket, "Received a packet.");
            var packetReceived = PacketReceived;
            if (packetReceived == null) {
                ipPacket.Dispose();
                return;
            }

            packetReceived.Invoke(this, ipPacket);
        }
        catch (Exception ex) {
            LogPacket(ipPacket, ex, "Error while invoking the received packets.");
            ipPacket.Dispose();
        }
    }

    /// <inheritdoc/>
    public ValueTask SendPacketQueuedAsync(IpPacket ipPacket)
    {
        // A passthrough send completes at once (see SendPacketQueued), under the lock its one-packet buffer
        // needs. One that fails is a drop, logged and counted as a full queue's is, so the call completes: it
        // throws only for a disposed transport or a send that broke the passthrough rule
        if (_passthrough) {
            SendPacketQueued(ipPacket);
            return default;
        }

        if (IsDisposed || IsDisposing)
            ThrowDisposed(ipPacket);

        LogPacket(ipPacket, "Sending a packet to queue.");

        // a queue with room takes the packet at once, as does one that drops its oldest; a full one in blocking
        // mode is waited on
        if (_sendChannel.Writer.TryWrite(ipPacket))
            return default;

        if (_blocking)
            return WaitForQueueAsync(ipPacket);

        // a queue that drops its oldest refuses only once it is closed
        DropRefusedPacket(ipPacket);
        return ValueTask.FromException(new ChannelClosedException());
    }

    // waits for room up to the blocking timeout; a packet that waited longer is dropped, and the call completes
    private async ValueTask WaitForQueueAsync(IpPacket ipPacket)
    {
        using var timeoutCancellationTokenSource = CreateBlockingTimeout();
        try {
            await _sendChannel.Writer
                .WriteAsync(ipPacket, timeoutCancellationTokenSource?.Token ?? CancellationToken.None)
                .Vhc();
        }
        catch (OperationCanceledException) {
            DropTimedOutPacket(ipPacket);
        }
        catch {
            DropRefusedPacket(ipPacket);
            throw;
        }
    }

    // the timeout of one blocking wait, when the options set one
    private CancellationTokenSource? CreateBlockingTimeout()
    {
        return _blockingTimeout is { } blockingTimeout ? new CancellationTokenSource(blockingTimeout) : null;
    }

    private readonly Lock _singlePacketLock = new();
    private IpPacket[] _singlePacketBuffer = new IpPacket[1];

    /// <inheritdoc/>
    public bool SendPacketQueued(IpPacket ipPacket)
    {
        if (IsDisposed || IsDisposing)
            ThrowDisposed(ipPacket);

        LogPacket(ipPacket, "Sending a packet to queue.");
        if (_passthrough) {
            lock (_singlePacketLock) {
                _singlePacketBuffer[0] = ipPacket;
                var ret = SendPacketsInternalAsync(_singlePacketBuffer);
                if (ret.IsCompleted)
                    return ret.GetAwaiter().GetResult();

                // a passthrough send must complete at once. One that does not keeps its buffer to its end, and the
                // next sender gets a new one, so the end of this send never counts or disposes another's packet
                _singlePacketBuffer = new IpPacket[1];
                throw new InvalidOperationException("A passthrough PacketTransport should not return an incomplete task.");
            }
        }

        // try to write the packet to the channel
        if (_sendChannel.Writer.TryWrite(ipPacket))
            return true;

        // in wait mode we need to block until there is space in the queue
        if (_blocking)
            return SendPacketQueuedBlocking(ipPacket);

        // a queue that drops its oldest refuses only once it is closed
        DropRefusedPacket(ipPacket);
        return false;
    }

    // waits for room up to the blocking timeout. A packet the queue refused (closed while waiting) or that
    // waited longer is dropped; any other error leaves the packet alone, as its write may still be pending
    private bool SendPacketQueuedBlocking(IpPacket ipPacket)
    {
        using var timeoutCancellationTokenSource = CreateBlockingTimeout();
        try {
            _sendChannel.Writer
                .WriteAsync(ipPacket, timeoutCancellationTokenSource?.Token ?? CancellationToken.None)
                .VhBlock();
            return true;
        }
        catch (ChannelClosedException) {
            DropRefusedPacket(ipPacket);
            return false;
        }
        catch (OperationCanceledException) {
            DropTimedOutPacket(ipPacket);
            return false;
        }
    }

    // a send to a disposed transport is refused: its packet goes as a refused one goes, then the error
    [DoesNotReturn]
    private void ThrowDisposed(IpPacket ipPacket)
    {
        DropRefusedPacket(ipPacket);
        throw new ObjectDisposedException(GetType().FullName);
    }

    // a packet the closed queue refused, in any mode
    private void DropRefusedPacket(IpPacket ipPacket)
    {
        DropUnqueuedPacket(ipPacket, "Dropping a packet. Send queue is closed.");
    }

    // a packet that waited for room longer than the blocking timeout: a drop, as a full queue's is
    private void DropTimedOutPacket(IpPacket ipPacket)
    {
        DropUnqueuedPacket(ipPacket, "Dropping a packet. Send queue stayed full.");
    }

    // a packet the queue did not take went to no one, so it is disposed; the caller is told so
    private void DropUnqueuedPacket(IpPacket ipPacket, string message)
    {
        LogPacket(ipPacket, LogLevel.Debug, null, message);
        _stat.AddDroppedPacket();
        ipPacket.Dispose();
    }

    // called by the queue for the packet it drops to make room, inside the write that took the new one: nothing
    // here may fail that write
    private void DropQueuedPacket(IpPacket ipPacket)
    {
        _stat.AddDroppedPacket();
        LogPacket(ipPacket, LogLevel.Debug, null, "Dropping the oldest packet. Send queue is full.");
        ipPacket.Dispose();
    }

    private async Task StartSendingPacketsAsync()
    {
        try {
            var ipPackets = new List<IpPacket>(_singleMode ? 1 : _queueCapacity);
            while (await _sendChannel.Reader.WaitToReadAsync().Vhc() && !IsDisposed && !IsDisposing) {
                // marked before the queue is emptied, so IsSending never reads false in between
                _isSending = true;
                ipPackets.Clear();

                // dequeue all packets
                while (ipPackets.Count < ipPackets.Capacity && _sendChannel.Reader.TryRead(out var ipPacket))
                    ipPackets.Add(ipPacket);

                // send packets; a failed send is handled there, so a fault here is one whose cleanup failed too
                var task = SendPacketsInternalAsync(ipPackets);
                if (!task.IsCompletedSuccessfully)
                    await task.Vhc();
            }
        }
        catch (Exception ex) {
            // a loop that cannot go on leaves the transport disposed, so its senders fail loud rather than being
            // refused for good
            VhLogger.Instance.LogError(ex, "Error in SendingPacketsAsync loop. Type: {Type}",
                VhLogger.FormatType(this));
            Dispose();
        }
        finally {
            // close the queue before emptying it, so no packet gets in after the last read: a send waiting for
            // room, or a later one, is refused at once instead of waiting forever, and none is left unsent
            // and undisposed
            _sendChannel.Writer.TryComplete();

            // dispose remaining packets
            if (_disposeUnsentPackets)
                while (_sendChannel.Reader.TryRead(out var ipPacket)) {
                    _stat.AddDroppedPacket();
                    ipPacket.Dispose();
                }
        }
    }

    private async ValueTask<bool> SendPacketsInternalAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        // a transport disposed since the send's own check refuses the packets: the subclass never saw them, so
        // they are disposed in any mode. The loop's mark goes with them, or the transport would read as sending
        // for good
        if (IsDisposed) {
            _isSending = false;
            DropFailedPackets(ipPackets, new ObjectDisposedException(VhLogger.FormatType(this)),
                disposePackets: true);
            return false;
        }

        // send packets
        try {
            // Set sending state and last sent time
            _isSending = true;
            _stat.LastSentTime = FastDateTime.UtcNow;

            // Send packets asynchronously, not resuming on the sender's context
            var task = SendPacketsAsync(ipPackets);
            if (!task.IsCompletedSuccessfully)
                await task.Vhc();
        }
        catch (Exception ex) {
            DropFailedPackets(ipPackets, ex, _disposeUnsentPackets);
            return false;
        }
        finally {
            _isSending = false;
        }

        // delivered ones, outside the send's try: a failing dispose is not a failed send
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            _stat.SentBytes += ipPackets[i].PacketLength;
            _stat.SentPackets++;
            if (_autoDisposePackets)
                ipPackets[i].Dispose();
        }

        return true;
    }

    // a batch that was not sent: every packet is counted and, when told, disposed
    private void DropFailedPackets(IReadOnlyList<IpPacket> ipPackets, Exception exception, bool disposePackets)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            LogPacket(ipPackets[i], exception, "Error in sending packet via channel.");
            _stat.AddDroppedPacket();
            if (disposePackets)
                ipPackets[i].Dispose();
        }
    }

    /// <summary>Logs a packet at the error level, when the logger writes trace lines.</summary>
    /// <param name="ipPacket">The packet.</param>
    /// <param name="exception">The error.</param>
    /// <param name="message">The message.</param>
    protected void LogPacket(IpPacket ipPacket, Exception exception, string message)
    {
        LogPacket(ipPacket, LogLevel.Error, exception, message);
    }

    /// <summary>Logs a packet at the trace level.</summary>
    /// <param name="ipPacket">The packet.</param>
    /// <param name="message">The message.</param>
    protected void LogPacket(IpPacket ipPacket, string message)
    {
        LogPacket(ipPacket, LogLevel.Trace, null, message);
    }

    // ReSharper disable once VirtualMemberNeverOverridden.Global
    /// <summary>
    /// Logs a packet, but only when the logger writes trace lines: the packet path is too busy to log otherwise.
    /// </summary>
    /// <param name="ipPacket">The packet.</param>
    /// <param name="logLevel">The level of the line.</param>
    /// <param name="exception">The error, or <see langword="null"/>.</param>
    /// <param name="message">The message.</param>
    /// <param name="args">The message's arguments.</param>
    protected virtual void LogPacket(IpPacket ipPacket, LogLevel logLevel,
        Exception? exception, string message, params object?[] args)
    {
        // LogPacket is so intensively used that we need to avoid unnecessary allocations or formatting
        // Just log if the log level is Trace despite the required log level
        if (VhLogger.MinLogLevel > LogLevel.Trace)
            return;

        VhLogger.Instance.Log(logLevel, message: $"{Name}: {message} {ipPacket}",
            exception: exception, args: args);
    }

    /// <summary>
    /// Closes the send queue, which refuses later sends and releases waiting ones, then disposes the transport.
    /// Packets left in the queue are dropped and disposed by the sending loop, which may finish after this method
    /// returns. Calling it again does nothing.
    /// </summary>
    public virtual void Dispose()
    {
        if (_disposed || Interlocked.Exchange(ref _disposing, true))
            return;

        // the queue is closed first: a sender waiting for room is refused at once, before any cleanup that may
        // wait for it, and no packet gets in meanwhile
        _sendChannel.Writer.TryComplete();
        PreDispose();
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    /// <summary>
    /// Disposes the transport once: <see cref="DisposeManaged"/> when <paramref name="disposing"/> is set, then
    /// <see cref="DisposeUnmanaged"/>.
    /// </summary>
    /// <param name="disposing"><see langword="true"/> when called from <see cref="Dispose()"/>.</param>
    protected void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        if (disposing)
            DisposeManaged();

        DisposeUnmanaged();
    }

    // the hooks catch their own failures: a throw would skip the rest of the close, native handles among it
    /// <summary>
    /// Runs first in a disposal, after the queue is closed. A derived class stops its work here; it must catch its
    /// own errors, as a throw would skip the rest of the close.
    /// </summary>
    protected virtual void PreDispose()
    {
    }

    /// <summary>
    /// Releases managed resources; the base detaches the <see cref="PacketReceived"/> handlers. A derived class
    /// must catch its own errors.
    /// </summary>
    protected virtual void DisposeManaged()
    {
        PacketReceived = null;
    }

    /// <summary>
    /// Releases unmanaged resources, such as native handles. A derived class must catch its own errors.
    /// </summary>
    protected virtual void DisposeUnmanaged()
    {
    }
}