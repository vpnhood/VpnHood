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
    private readonly bool _singleMode;
    private readonly bool _passthrough;
    private bool _disposed;
    private bool _disposing;
    private readonly PacketTransportStat _stat = new();
    private bool _isSending;

    // A packet the transport does not deliver (dropped, refused, failed, or left in the queue at the end) is
    // disposed whenever the transport queues, as no one else holds it any more; a delivered one only with
    // AutoDisposePackets, as a transport that hands its packets on (a throttled tunnel) must not dispose them
    private readonly bool _disposeUnsentPackets;
    protected bool IsDisposed => _disposed;
    protected bool IsDisposing => _disposing;

    // Sends a batch, one packet in single mode. It must end once the transport is disposed, as the queue's
    // last packets wait on it. A transport that hands packets on must not throw after handing one on: a throw
    // counts the whole batch dropped and disposes it. In passthrough mode it must complete at once and never
    // send on its own transport, whose one-packet buffer the outer send still uses
    protected abstract ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets);
    protected virtual string Name => VhLogger.FormatType(this);

    public event EventHandler<IpPacket>? PacketReceived;
    public ReadOnlyPacketTransportStat PacketStat { get; }
    public int QueueLength => _sendChannel.Reader.Count;
    public bool IsSending => Volatile.Read(ref _isSending) || QueueLength > 0;

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

    protected virtual void OnPacketReceived(IpPacket ipPacket)
    {
        try {
            ObjectDisposedException.ThrowIf(IsDisposed || IsDisposing, this);

            _stat.LastReceivedTime = FastDateTime.UtcNow;
            _stat.ReceivedBytes += ipPacket.PacketLength;
            _stat.ReceivedPackets++;
            LogPacket(ipPacket, "Received a packet.");
            PacketReceived?.Invoke(this, ipPacket);
        }
        catch (Exception ex) {
            LogPacket(ipPacket, ex, "Error while invoking the received packets.");
            if (_autoDisposePackets)
                ipPacket.Dispose();
        }
    }

    public ValueTask SendPacketQueuedAsync(IpPacket ipPacket)
    {
        // A passthrough send completes at once (see SendPacketQueued), under the lock its one-packet buffer
        // needs. One that fails is a drop, logged and counted as a full queue's is, so the call completes: only
        // a refusal, by a closed transport, throws
        if (_passthrough) {
            SendPacketQueued(ipPacket);
            return default;
        }

        if (IsDisposed || IsDisposing)
            ThrowDisposed(ipPacket);

        LogPacket(ipPacket, "Sending a packet to queue.");

        // waits for room in blocking mode; a queue that drops its oldest takes the packet at once
        var writeTask = _sendChannel.Writer.WriteAsync(ipPacket);
        return writeTask.IsCompletedSuccessfully ? default : WaitForQueueAsync(writeTask, ipPacket);
    }

    private async ValueTask WaitForQueueAsync(ValueTask writeTask, IpPacket ipPacket)
    {
        try {
            await writeTask.Vhc();
        }
        catch {
            DropRefusedPacket(ipPacket);
            throw;
        }
    }

    private readonly IpPacket[] _singlePacketBuffer = new IpPacket[1];

    public bool SendPacketQueued(IpPacket ipPacket)
    {
        if (IsDisposed || IsDisposing)
            ThrowDisposed(ipPacket);

        LogPacket(ipPacket, "Sending a packet to queue.");
        if (_passthrough) {
            lock (_singlePacketBuffer) {
                _singlePacketBuffer[0] = ipPacket;
                var ret = SendPacketsInternalAsync(_singlePacketBuffer);
                if (ret.IsCompleted)
                    return ret.GetAwaiter().GetResult();

                // a passthrough send must complete at once; one that does not is waited out, still under the
                // lock, before the error, so its end does not run over the buffer the next sender fills
                ret.AsTask().GetAwaiter().GetResult();
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

    private bool SendPacketQueuedBlocking(IpPacket ipPacket)
    {
        try {
            _sendChannel.Writer.WriteAsync(ipPacket).VhBlock();
            return true;
        }
        catch {
            DropRefusedPacket(ipPacket);
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

    // a packet the closed queue refused, in any mode; the caller is told so
    private void DropRefusedPacket(IpPacket ipPacket)
    {
        LogPacket(ipPacket, LogLevel.Debug, null, "Dropping a packet. Send queue is closed.");
        _stat.AddDroppedPacket();
        if (_disposeUnsentPackets)
            ipPacket.Dispose();
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

                // send packets
                var task = SendPacketsInternalAsync(ipPackets);
                if (!task.IsCompleted)
                    await task;
            }
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(ex, "Error in SendingPacketsAsync loop. Type: {Type}",
                VhLogger.FormatType(this));
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

            // Send packets asynchronously
            var task = SendPacketsAsync(ipPackets);
            if (!task.IsCompletedSuccessfully)
                await task;

            // ReSharper disable once ForCanBeConvertedToForeach
            // passthrough mode does not dispose packets
            for (var i = 0; i < ipPackets.Count; i++) {
                _stat.SentBytes += ipPackets[i].PacketLength;
                _stat.SentPackets++;
                if (_autoDisposePackets)
                    ipPackets[i].Dispose();
            }

            return true;
        }
        catch (Exception ex) {
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < ipPackets.Count; i++) {
                LogPacket(ipPackets[i], ex, "Error in sending packet via channel.");

                _stat.AddDroppedPacket();
                if (_disposeUnsentPackets)
                    ipPackets[i].Dispose();
            }

            return false;
        }
        finally {
            _isSending = false;
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

        if (disposing)
            DisposeManaged();

        DisposeUnmanaged();
        _disposing = false;
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