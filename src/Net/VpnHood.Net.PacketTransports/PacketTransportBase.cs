using System.Diagnostics.CodeAnalysis;
using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Packets;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Net.PacketTransports;

public abstract class PacketTransportBase : IPacketTransport
{
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
    // it on. A delivered one only with AutoDisposePackets, as a transport that hands its packets on (a throttled
    // tunnel) must not dispose them
    private readonly bool _disposeUnsentPackets;
    protected bool IsDisposed => _disposed;

    // set once disposal begins, and stays set
    protected bool IsDisposing => _disposing;

    // Sends a batch, one packet in single mode. It must end once the transport is disposed, as the queue's
    // last packets wait on it. A throw counts the whole batch dropped and, by the rule above, disposes it, so a
    // send must not throw after handing a packet on; a passthrough send without AutoDisposePackets disposes
    // itself what it does not hand on. In passthrough mode it must complete at once and never send on its own
    // transport, whose one-packet buffer the outer send still uses. Both rules are left unchecked by design:
    // telling which packets a failing send had handed on would take an API of its own, and catching a send
    // on its own transport would cost every packet
    protected abstract ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets);
    protected virtual string Name => VhLogger.FormatType(this);

    public event EventHandler<IpPacket>? PacketReceived;
    public ReadOnlyPacketTransportStat PacketStat { get; }
    public int QueueLength => _sendChannel.Reader.Count;

    // the flag is read again after the queue: the loop marks a batch before it empties the queue, so a batch
    // taken between the first two reads still shows
    public bool IsSending => Volatile.Read(ref _isSending) || QueueLength > 0 || Volatile.Read(ref _isSending);

    // the batch in flight, for a stop that waits for it; read fresh each time
    protected bool IsSendingBatch => Volatile.Read(ref _isSending);

    protected PacketTransportBase(PacketTransportOptions options, bool singleMode, bool passthrough)
    {
        if (passthrough && !singleMode)
            throw new ArgumentException("Passthrough mode should be used with single mode only.", nameof(passthrough));

        _queueCapacity = options.QueueCapacity ?? PacketTransportOptions.DefaultQueueCapacity;
        _autoDisposePackets = options.AutoDisposePackets;
        _disposeUnsentPackets = options.AutoDisposePackets || !passthrough;
        _blocking = options.Blocking;
        _blockingTimeout = options.BlockingTimeout;
        _singleMode = singleMode;
        _passthrough = passthrough;

        // a full queue that does not block drops its oldest packet, so the newer ones behind it show the loss
        _sendChannel = Channel.CreateBounded(new BoundedChannelOptions(_queueCapacity) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = options.Blocking ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropOldest
        }, options.Blocking ? null : (Action<IpPacket>)DropQueuedPacket);

        PacketStat = new ReadOnlyPacketTransportStat(_stat);
        Task.Run(StartSendingPacketsAsync);
    }

    // The handler takes a received packet. One the transport cannot deliver (refused once disposed, with no
    // handler, or given back by a handler that threw) went to no one, so it is disposed here in any mode
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
            try {
                LogPacket(ipPacket, ex, "Error while invoking the received packets.");
            }
            finally {
                ipPacket.Dispose();
            }
        }
    }

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

    private readonly object _singlePacketLock = new();
    private IpPacket[] _singlePacketBuffer = new IpPacket[1];

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

    // a packet the queue did not take went to no one, so it is disposed, even when the logger fails; the caller
    // is told so
    private void DropUnqueuedPacket(IpPacket ipPacket, string message)
    {
        try {
            LogPacket(ipPacket, LogLevel.Debug, null, message);
        }
        finally {
            _stat.AddDroppedPacket();
            ipPacket.Dispose();
        }
    }

    // called by the queue for the packet it drops to make room, inside the write that took the new one: nothing
    // here may fail that write
    private void DropQueuedPacket(IpPacket ipPacket)
    {
        _stat.AddDroppedPacket();
        try {
            LogPacket(ipPacket, LogLevel.Debug, null, "Dropping the oldest packet. Send queue is full.");
        }
        catch {
            // a failing logger must not fail a write that succeeded
        }

        ipPacket.Dispose();
    }

    private async Task StartSendingPacketsAsync()
    {
        try {
            var ipPackets = new List<IpPacket>(_singleMode ? 1 : _queueCapacity);
            while (await _sendChannel.Reader.WaitToReadAsync() && !IsDisposed && !IsDisposing) {
                // marked before the queue is emptied, so IsSending never reads false in between
                _isSending = true;
                ipPackets.Clear();

                // dequeue all packets
                while (ipPackets.Count < ipPackets.Capacity && _sendChannel.Reader.TryRead(out var ipPacket))
                    ipPackets.Add(ipPacket);

                // send packets; a failed send is handled there, so a fault here is one whose cleanup failed too
                var task = SendPacketsInternalAsync(ipPackets);
                if (!task.IsCompletedSuccessfully)
                    await task;
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
        // send packets
        try {
            // if the transport is disposed, do not send packets
            if (IsDisposed)
                throw new ObjectDisposedException(VhLogger.FormatType(this));

            // Set sending state and last sent time
            _isSending = true;
            _stat.LastSentTime = FastDateTime.UtcNow;

            // Send packets asynchronously, not resuming on the sender's context
            var task = SendPacketsAsync(ipPackets);
            if (!task.IsCompletedSuccessfully)
                await task.Vhc();
        }
        catch (Exception ex) {
            DropFailedPackets(ipPackets, ex);
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

    // a batch the send failed on: every packet is counted and, by the rule above, disposed, even when the logger
    // fails
    private void DropFailedPackets(IReadOnlyList<IpPacket> ipPackets, Exception exception)
    {
        try {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < ipPackets.Count; i++)
                LogPacket(ipPackets[i], exception, "Error in sending packet via channel.");
        }
        finally {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < ipPackets.Count; i++) {
                _stat.AddDroppedPacket();
                if (_disposeUnsentPackets)
                    ipPackets[i].Dispose();
            }
        }
    }

    protected void LogPacket(IpPacket ipPacket, Exception exception, string message)
    {
        LogPacket(ipPacket, LogLevel.Error, exception, message);
    }

    protected void LogPacket(IpPacket ipPacket, string message)
    {
        LogPacket(ipPacket, LogLevel.Trace, null, message);
    }

    // ReSharper disable once VirtualMemberNeverOverridden.Global
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

    public virtual void Dispose()
    {
        if (_disposed || Interlocked.Exchange(ref _disposing, true))
            return;

        // the queue is closed first: a sender waiting for room is refused at once, before any cleanup that may
        // wait for it, and no packet gets in meanwhile; a throwing hook still leaves the transport disposed
        _sendChannel.Writer.TryComplete();
        try {
            PreDispose();
        }
        finally {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
    }

    protected void Dispose(bool disposing)
    {
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        // unmanaged resources, native handles among them, go even when the managed cleanup throws
        try {
            if (disposing)
                DisposeManaged();
        }
        finally {
            DisposeUnmanaged();
        }
    }

    protected virtual void PreDispose()
    {
    }

    protected virtual void DisposeManaged()
    {
        PacketReceived = null;
        _sendChannel.Writer.TryComplete(); // complete the writer to stop sending packets
    }

    protected virtual void DisposeUnmanaged()
    {
    }
}