using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.PacketTransports;

public abstract class PacketTransportBase :  IPacketTransport
{
    private readonly Channel<IpPacket> _sendChannel;
    private readonly int _queueCapacity;
    private readonly bool _autoDisposeSentPackets;
    private readonly bool _autoDisposeFailedPackets;
    private readonly bool _blocking;
    private readonly bool _singleMode;
    private readonly bool _passthrough;
    private bool _isSending;
    public DateTime LastReceivedTime { get; protected set; }
    public DateTime LastActivityTime => LastReceivedTime > LastSentTime ? LastReceivedTime : FastDateTime.Now;
    public event EventHandler<PacketReceivedEventArgs>? PacketReceived;
    private readonly PacketReceivedEventArgs _packetReceivedEventArgs = new(new IpPacket[1]);

    protected abstract ValueTask SendPacketsAsync(IList<IpPacket> ipPackets);
    public int QueueLength => _sendChannel.Reader.Count;
    public bool IsSending => _isSending || QueueLength > 0;
    public DateTime LastSentTime { get; protected set; } = FastDateTime.Now;
    public bool Disposed { get; private set; }

    protected PacketTransportBase(PacketTransportOptions options, bool singleMode, bool passthrough)
    {
        _queueCapacity = options.QueueCapacity;
        _autoDisposeSentPackets = options.AutoDisposeSentPackets;
        _autoDisposeFailedPackets = options.AutoDisposeFailedPackets;
        _blocking = options.Blocking;
        _singleMode = singleMode;
        _passthrough = passthrough;
        _sendChannel = Channel.CreateBounded<IpPacket>(new BoundedChannelOptions(options.QueueCapacity) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = options.Blocking ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropWrite
        });

        _ = StartSendingPacketsAsync();
    }
    
    protected void OnPacketReceived(PacketReceivedEventArgs arg)
    {
        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < arg.IpPackets.Count; i++) {
            var ipPacket = arg.IpPackets[i];
            OnPacketReceived(ipPacket);
        }
    }

    protected void OnPacketReceived(IpPacket ipPacket)
    {
        try {
            lock (_packetReceivedEventArgs) { //todo: remove by packet
                LastReceivedTime = FastDateTime.Now;
                _packetReceivedEventArgs.IpPackets[0] = ipPacket;
                LogPacket(ipPacket, $"{VhLogger.FormatType(this)}: Received a packet.");
                PacketReceived?.Invoke(this, _packetReceivedEventArgs);
            }
        }
        catch (Exception ex) {
            LogPacket(ipPacket, $"{VhLogger.FormatType(this)}: Error while invoking the received packets.", exception: ex);
        }
    }

    public ValueTask SendPacketQueuedAsync(IpPacket ipPacket)
    {
        return _sendChannel.Writer.WriteAsync(ipPacket);
    }

    private readonly IpPacket[] _singlePacketBuffer = new IpPacket[1];
    public bool SendPacketQueued(IpPacket ipPacket)
    {
        LogPacket(ipPacket, $"{VhLogger.FormatType(this)}: Sending a packet to queue.");
        if (_passthrough) {
            lock (_singlePacketBuffer) {
                _singlePacketBuffer[0] = ipPacket;
                var ret = SendPacketsInternalAsync(_singlePacketBuffer);
                if (!ret.IsCompleted) throw new InvalidOperationException(
                    "A passthrough PacketTransport should not return an incomplete task.");
                return ret.Result;
            }
        }

        // try to write the packet to the channel
        if (_sendChannel.Writer.TryWrite(ipPacket))
            return true;

        // in wait mode we need to block until there is space in the queue
        if (_blocking)
            return SendPacketQueuedBlocking(ipPacket);

        // dispose the packet
        LogPacket(ipPacket, $"{VhLogger.FormatType(this)}: Dropping a packet. Send queue is full.", LogLevel.Debug);
        if (_autoDisposeFailedPackets)
            ipPacket.Dispose();

        return false;
    }

    private bool SendPacketQueuedBlocking(IpPacket ipPacket)
    {
        try {
            _sendChannel.Writer.WriteAsync(ipPacket).GetAwaiter().GetResult();
            return true;
        }
        catch (Exception ex) {
            LogPacket(ipPacket, "Dropping packet. Could not write the packet to queue.", exception: ex);
            if (_autoDisposeFailedPackets)
                ipPacket.Dispose();

            return false;
        }
    }

    private async Task StartSendingPacketsAsync()
    {
        var ipPackets = new List<IpPacket>(_singleMode ? 1 : _queueCapacity);
        while (await _sendChannel.Reader.WaitToReadAsync() && !Disposed) {
            ipPackets.Clear();
            _isSending = true;
            LastSentTime = FastDateTime.Now;

            // dequeue all packets
            while (ipPackets.Count < ipPackets.Capacity && _sendChannel.Reader.TryRead(out var ipPacket))
                ipPackets.Add(ipPacket);

            // send packets
            var task = SendPacketsInternalAsync(ipPackets);
            if (!task.IsCompleted)
                await task;
        }
        
        // dispose remaining packets
        if (_autoDisposeFailedPackets)
            while (_sendChannel.Reader.TryRead(out var ipPacket))
                ipPacket.Dispose();
    }

    private async ValueTask<bool> SendPacketsInternalAsync(IList<IpPacket> ipPackets)
    {
        // send packets
        try {
            _isSending = true;
            LastSentTime = FastDateTime.Now;

            var task = SendPacketsAsync(ipPackets);
            if (!task.IsCompleted)
                await task;

            // ReSharper disable once ForCanBeConvertedToForeach
            if (_autoDisposeSentPackets)
                for (var i = 0; i < ipPackets.Count; i++)
                    ipPackets[i].Dispose();

            return true;
        }
        catch (Exception ex) {
            if (ipPackets.Count == 1)
                LogPacket(ipPackets[0],
                    $"{VhLogger.FormatType(this)}: Error in sending packet via channel.", exception: ex);
            else
                VhLogger.Instance.LogError(ex,
                    $"{VhLogger.FormatType(this)}: Error in sending some packets via channel.");

            // ReSharper disable once ForCanBeConvertedToForeach
            if (_autoDisposeFailedPackets)
                for (var i = 0; i < ipPackets.Count; i++)
                    ipPackets[i].Dispose();
            
            return false;
        }
        finally {
            _isSending = false;
        }
    }

    protected virtual void LogPacket(IpPacket ipPacket, string message, LogLevel? logLevel = null, Exception? exception = null)
    {
        if (VhLogger.IsDiagnoseMode) {
            logLevel ??= exception != null ? LogLevel.Debug : LogLevel.Trace;
            VhLogger.Instance.Log(logLevel.Value, message: $"{message}. {ipPacket}", exception: exception);
        }
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing)
    {
        if (Disposed) return;
        Disposed = true;

        // Dispose managed resources
        if (disposing) {
            PacketReceived = null;
        }

    }
}