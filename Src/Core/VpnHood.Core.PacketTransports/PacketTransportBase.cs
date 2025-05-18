using System.Threading.Channels;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Packets;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.PacketTransports;

public abstract class PacketTransportBase : IPacketTransport
{
    private readonly Channel<IpPacket> _sendChannel;
    private readonly int _queueCapacity;
    private readonly bool _autoDisposePackets;
    private readonly bool _blocking;
    private readonly bool _singleMode;
    private readonly bool _passthrough;
    private readonly PacketTransportStat _stat = new();
    private bool _isSending;
    protected bool IsDisposed { get; private set; }
    protected abstract ValueTask SendPacketsAsync(IList<IpPacket> ipPackets);

    public event EventHandler<IpPacket>? PacketReceived;
    public ReadOnlyPacketTransportStat PacketStat { get; }
    public int QueueLength => _sendChannel.Reader.Count;
    public bool IsSending => _isSending || QueueLength > 0;

    protected PacketTransportBase(PacketTransportOptions options, bool singleMode, bool passthrough)
    {
        if (passthrough && !singleMode)
            throw new ArgumentException("Passthrough mode should be used with single mode only.", nameof(passthrough));

        _queueCapacity = options.QueueCapacity;
        _autoDisposePackets = options.AutoDisposePackets;
        _blocking = options.Blocking;
        _singleMode = singleMode;
        _passthrough = passthrough;
        _sendChannel = Channel.CreateBounded<IpPacket>(new BoundedChannelOptions(options.QueueCapacity) {
            SingleReader = true,
            SingleWriter = false,
            FullMode = options.Blocking ? BoundedChannelFullMode.Wait : BoundedChannelFullMode.DropWrite
        });

        PacketStat = new ReadOnlyPacketTransportStat(_stat);
        _ = StartSendingPacketsAsync();
    }

    protected void OnPacketReceived(IpPacket ipPacket)
    {
        try {
            _stat.LastReceivedTime = FastDateTime.Now;
            _stat.ReceivedBytes += ipPacket.PacketLength;
            _stat.ReceivedPackets++;
            LogPacket(ipPacket, $"{VhLogger.FormatType(this)}: Received a packet.");
            PacketReceived?.Invoke(this, ipPacket);
        }
        catch (Exception ex) {
            LogPacket(ipPacket, $"{VhLogger.FormatType(this)}: Error while invoking the received packets.", exception: ex);
            if (_autoDisposePackets)
                ipPacket.Dispose();
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
        if (_autoDisposePackets)
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
            if (_autoDisposePackets)
                ipPacket.Dispose();

            return false;
        }
    }

    private async Task StartSendingPacketsAsync()
    {
        var ipPackets = new List<IpPacket>(_singleMode ? 1 : _queueCapacity);
        while (await _sendChannel.Reader.WaitToReadAsync() && !IsDisposed) {
            ipPackets.Clear();

            // dequeue all packets
            while (ipPackets.Count < ipPackets.Capacity && _sendChannel.Reader.TryRead(out var ipPacket))
                ipPackets.Add(ipPacket);

            // send packets
            var task = SendPacketsInternalAsync(ipPackets);
            if (!task.IsCompleted)
                await task;
        }

        // dispose remaining packets
        if (_autoDisposePackets)
            while (_sendChannel.Reader.TryRead(out var ipPacket))
                ipPacket.Dispose();
    }

    private async ValueTask<bool> SendPacketsInternalAsync(IList<IpPacket> ipPackets)
    {
        // send packets
        try {
            _isSending = true;
            _stat.LastSentTime = FastDateTime.Now;

            var task = SendPacketsAsync(ipPackets);
            if (!task.IsCompleted)
                await task;

            // ReSharper disable once ForCanBeConvertedToForeach
            // passthrough mode does not dispose packets
            for (var i = 0; i < ipPackets.Count; i++) {
                _stat.SentBytes += ipPackets[i].PacketLength;
                _stat.SentPackets++;
                if (_autoDisposePackets && !_passthrough)
                    ipPackets[i].Dispose();
            }

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
            for (var i = 0; i < ipPackets.Count; i++) {
                _stat.DroppedPackets++;
                if (_autoDisposePackets)
                    ipPackets[i].Dispose();
            }

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
        if (IsDisposed) return;
        IsDisposed = true;

        // Dispose managed resources
        if (disposing) {
            PacketReceived = null;
        }

    }
}