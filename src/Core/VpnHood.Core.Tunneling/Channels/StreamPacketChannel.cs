using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Net.Packets;
using VpnHood.Net.PacketTransports;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Jobs;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.Connections;
using VpnHood.Core.Tunneling.DatagramMessaging;
using VpnHood.Core.Common.Configuration;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannel : PacketTransport, IStreamPacketChannel
{
    private readonly TimeSpan? _lifespan;
    private readonly TrafficMeter? _trafficMeter;
    private readonly int _receiveBufferSize;
    private readonly Memory<byte> _sendBuffer;
    private readonly IStreamConnection _streamConnection;

    // when the channel began closing, by its lifespan, the peer's close or its owner's, as ticks, 0 while open: from
    // then on it is Disconnecting. The lifetime job, the read task and the owner may each close it, so the first
    // close wins and starts the grace
    private long _closeTicks;
    private bool _started;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Job _checkLifetimeJob;

    protected override string Name { get; }

    public string ChannelId { get; }
    public DateTime RequestTime { get; }
    public DateTime LastActivityTime => PacketStat.LastActivityTime;
    public Traffic Traffic => new(PacketStat.SentBytes, PacketStat.ReceivedBytes);

    public StreamPacketChannel(StreamPacketChannelOptions options)
        : base(new PacketTransportOptions {
            AutoDisposePackets = options.AutoDisposePackets,
            Blocking = options.Blocking,
            QueueCapacity = options.QueueCapacity
        })
    {
        _lifespan = options.Lifespan;
        _trafficMeter = options.TrafficMeter;
        _receiveBufferSize = options.BufferSize.Receive;
        _sendBuffer = new byte[options.BufferSize.Send];
        _streamConnection = options.StreamConnection;
        Name = $"{VhLogger.FormatType(this)}: {options.ChannelId}";
        ChannelId = options.ChannelId;
        RequestTime = options.RequestTime;
        _checkLifetimeJob = new Job(CheckLifetime, nameof(StreamPacketChannel));
    }

    public PacketChannelState State {
        get {
            if (IsDisposed)
                return
                    PacketChannelState.Disposed;

            // a plain read, as the tunnel reads the state per packet; a stale one is read again after its cleanup
            if (_closeTicks != 0)
                return PacketChannelState.Disconnecting;

            return _started
                ? PacketChannelState.Connected
                : PacketChannelState.NotStarted;
        }
    }

    public DateTime? CloseTime {
        get {
            var closeTicks = Volatile.Read(ref _closeTicks);
            return closeTicks == 0 ? null : new DateTime(closeTicks, DateTimeKind.Utc);
        }
    }

    public void Start()
    {
        if (IsDisposed)
            throw new ObjectDisposedException(GetType().Name);

        if (_started)
            throw new InvalidOperationException("The packet channel is already started.");

        // start reading packets
        VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
            "Starting a PacketChannel. ChannelId: {ChannelId}", ChannelId);

        _ = StartTask();
        _started = true;
    }

    private async Task StartTask()
    {
        try {
            await StartReadTask().Vhc();
            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "PacketChannel read task completed. ChannelId: {ChannelId}, Type: {Type}",
                ChannelId, VhLogger.FormatType(this));
        }
        catch (OperationCanceledException) when (IsDisposed || _cancellationTokenSource.IsCancellationRequested) {
            // normal cancellation, do nothing
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.PacketChannel, ex,
                "PacketChannel read task failed. ChannelId: {ChannelId}, Type: {Type}",
                ChannelId, VhLogger.FormatType(this));
        }
        finally {
            Dispose();
        }
    }

    private ValueTask CheckLifetime(CancellationToken cancellationToken)
    {
        if (IsDisposed)
            throw new ObjectDisposedException(nameof(CheckLifetime));

        // if channel is disposed, do nothing
        if (_cancellationTokenSource.IsCancellationRequested)
            return default;

        // the lifespan is over: the channel closes
        if (_started && _lifespan.HasValue && FastDateTime.UtcNow - PacketStat.CreatedTime > _lifespan.Value &&
            TryStartClose()) {
            VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
                "PacketChannel lifetime is over. ChannelId: {ChannelId}, CreatedTime: {CreatedTime}, Lifespan: {Lifespan}",
                ChannelId, PacketStat.CreatedTime, _lifespan);

            SendClose();
        }

        // a closing channel goes after its grace, in which the packets already on their way arrive
        var closeTicks = Volatile.Read(ref _closeTicks);
        if (closeTicks != 0 && FastDateTime.UtcNow.Ticks - closeTicks > TransportDefaults.TcpGracefulTimeout.Ticks)
            Dispose();

        return default;
    }

    // Starts the channel's close, as its lifespan's end does, where Dispose would end it at once: it goes after its
    // grace, in which what is on its way still arrives. One already closing or gone is left as it is, with no throw,
    // as its owner's list may still hold a disposed channel
    public void Close()
    {
        if (IsDisposed || IsDisposing || !TryStartClose())
            return;

        SendClose();
    }

    // Disconnecting first, so the tunnel stops queueing on the channel; the first close wins
    private bool TryStartClose()
    {
        return Interlocked.CompareExchange(ref _closeTicks, FastDateTime.UtcNow.Ticks, 0) == 0;
    }

    // the close goes behind the packets already queued
    private void SendClose()
    {
        try {
            SendPacketQueued(PacketMessageHandler.CreateMessage(new ClosePacketMessage()));
        }
        catch (ObjectDisposedException) {
            // disposed since the check: a channel gone needs no close
        }
    }


    protected override void OnPacketReceived(IpPacket ipPacket)
    {
        if (!PacketMessageHandler.IsPacketMessage(ipPacket)) {
            base.OnPacketReceived(ipPacket);
            return;
        }

        // A message is the channel's own, so it is disposed here. One it cannot handle, such as a newer peer's, is
        // dropped as the base drops a packet it cannot deliver, rather than ending the channel
        try {
            if (PacketMessageHandler.ReadMessage(ipPacket) is ClosePacketMessage)
                ReceiveClose();
        }
        catch (Exception ex) {
            LogPacket(ipPacket, ex, "Dropping a packet message the channel could not handle.");
        }
        finally {
            ipPacket.Dispose();
        }
    }

    // The peer's close: the channel is Disconnecting and goes after its grace, as the peer's does. It sends no answer:
    // each end reads for its whole grace, so a packet queued as the close arrived still comes through, where an end
    // that closed on an answer would lose one queued behind it
    private void ReceiveClose()
    {
        if (!TryStartClose())
            return;

        VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel,
            "PacketChannel received close message. ChannelId: {ChannelId}, CreatedTime: {CreatedTime}, Lifespan: {Lifespan}",
            ChannelId, PacketStat.CreatedTime, _lifespan);
    }

    protected override async ValueTask SendPacketsAsync(IReadOnlyList<IpPacket> ipPackets)
    {
        ObjectDisposedException.ThrowIf(IsDisposed, this);
        var cancellationToken = _cancellationTokenSource.Token;

        // copy packets to buffer
        var buffer = _sendBuffer;
        var bufferIndex = 0;

        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            var packetBytes = ipPacket.Buffer;

            // flush current buffer if this packet does not fit
            if (bufferIndex > 0 && bufferIndex + packetBytes.Length > buffer.Length) {
                await WriteBuffer(buffer[..bufferIndex], cancellationToken).Vhc();
                bufferIndex = 0;
            }

            // Write the packet directly if it does not fit in the buffer or there is only one packet
            if (packetBytes.Length > buffer.Length || ipPackets.Count == 1) {
                await WriteBuffer(packetBytes, cancellationToken).Vhc();
                continue;
            }

            packetBytes.Span.CopyTo(buffer.Span[bufferIndex..]);
            bufferIndex += packetBytes.Length;
        }

        // send remaining buffer
        if (bufferIndex > 0)
            await WriteBuffer(buffer[..bufferIndex], cancellationToken).Vhc();
    }

    // the send limit is not waited on here: a throttled tunnel paces its packets before they reach a channel
    private async ValueTask WriteBuffer(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken)
    {
        await _streamConnection.Stream.WriteAsync(buffer, cancellationToken).Vhc();
        _trafficMeter?.OnSent(buffer.Length);
    }

    private async Task StartReadTask()
    {
        var cancellationToken = _cancellationTokenSource.Token;

        using var streamPacketReader =
            new StreamPacketReader(_streamConnection.Stream, _receiveBufferSize);

        // stop reading if State is not Connected (Such as getting the close request)
        while (!cancellationToken.IsCancellationRequested) {
            var ipPacket = await streamPacketReader.ReadAsync(cancellationToken).Vhc();
            if (ipPacket == null) {
                VhLogger.Instance.LogDebug(GeneralEventId.PacketChannel, "Packet stream ended. Terminating read task.");
                break;
            }

            // the receive limit, enforced by reading slower; a packet whose wait is cut is not delivered
            _trafficMeter?.OnReceived(ipPacket.PacketLength);
            if (_trafficMeter != null) {
                var throttleTask = _trafficMeter.ThrottleReceiveAsync(ipPacket.PacketLength, cancellationToken);
                if (!throttleTask.IsCompletedSuccessfully) {
                    try {
                        await throttleTask.Vhc();
                    }
                    catch {
                        ipPacket.Dispose();
                        throw;
                    }
                }
            }

            OnPacketReceived(ipPacket);
        }
    }

    protected override void DisposeManaged()
    {
        _cancellationTokenSource.TryCancel();

        // the connection's async close, not awaited: its sync one may wait for the peer, as a QUIC stream's does,
        // and a channel is disposed under the tunnel's lock and on its packet path
        _ = _streamConnection.TryDisposeAsync();

        _checkLifetimeJob.Dispose();
        _cancellationTokenSource.Dispose();
        base.DisposeManaged();
    }
}
