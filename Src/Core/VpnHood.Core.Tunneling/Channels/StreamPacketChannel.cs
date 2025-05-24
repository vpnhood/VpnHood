using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Packets;
using VpnHood.Core.PacketTransports;
using VpnHood.Core.Toolkit.Jobs;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.DatagramMessaging;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamPacketChannel : PacketTransport, IPacketChannel, IJob
{
    private readonly Memory<byte> _buffer = new byte[0xFFFF * 4];
    private readonly IClientStream _clientStream;
    private readonly DateTime _lifeTime = DateTime.MaxValue;
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _isCloseSent;
    private bool _isCloseReceived;

    public JobSection JobSection { get; } = new();
    public string ChannelId { get; }
    public bool Connected { get; private set; }
    public DateTime LastActivityTime => PacketStat.LastActivityTime;
    public Traffic Traffic => new(PacketStat.SentBytes, PacketStat.ReceivedBytes);

    public bool IsStream => true;

    public StreamPacketChannel(StreamPacketChannelOptions options)
        : base(new PacketTransportOptions {
            AutoDisposePackets = options.AutoDisposePackets,
            Blocking = options.Blocking
        })
    {
        var lifespan = options.Lifespan ?? Timeout.InfiniteTimeSpan;
        ChannelId = options.ChannelId;
        _clientStream = options.ClientStream;
        if (!VhUtils.IsInfinite(lifespan)) {
            _lifeTime = FastDateTime.Now + lifespan;
            JobRunner.Default.Add(this);
        }
    }

    public void Start()
    {
        _ = StartInternal();
    }

    public async Task StartInternal()
    {
        if (_disposed)
            throw new ObjectDisposedException("StreamPacketChannel");

        if (Connected)
            throw new Exception("StreamPacketChannel has been already started.");

        Connected = true;
        try {
            await ReadTask(_cancellationTokenSource.Token).VhConfigureAwait();
            await SendClose().VhConfigureAwait();
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.DatagramChannel, ex, "StreamPacketChannel has been stopped unexpectedly.");
        }
        finally {
            Connected = false;
            await DisposeAsync();
        }
    }

    protected override async ValueTask SendPacketsAsync(IList<IpPacket> ipPackets)
    {
        if (_disposed) throw new ObjectDisposedException(VhLogger.FormatType(this));
        var cancellationToken = _cancellationTokenSource.Token;

        // check channel connectivity
        cancellationToken.ThrowIfCancellationRequested();
        if (!Connected)
            throw new Exception($"The StreamDatagramChannel is disconnected. ChannelId: {ChannelId}.");

        // copy packets to buffer
        var buffer = _buffer;
        var bufferIndex = 0;

        // ReSharper disable once ForCanBeConvertedToForeach
        for (var i = 0; i < ipPackets.Count; i++) {
            var ipPacket = ipPackets[i];
            var packetBytes = ipPacket.Buffer;

            // flush current buffer if this packet does not fit
            if (bufferIndex > 0 && bufferIndex + packetBytes.Length > buffer.Length) {
                await _clientStream.Stream.WriteAsync(buffer[..bufferIndex], cancellationToken).VhConfigureAwait();
                bufferIndex = 0;
            }

            // Write the packet directly if it does not fit in the buffer or there is only one packet
            if (packetBytes.Length > buffer.Length || ipPackets.Count == 1) {
                // send packet
                await _clientStream.Stream.WriteAsync(packetBytes, cancellationToken).VhConfigureAwait();
                continue;
            }

            packetBytes.Span.CopyTo(buffer.Span[bufferIndex..]);
            bufferIndex += packetBytes.Length;
        }

        // send remaining buffer
        if (bufferIndex > 0) {
            await _clientStream.Stream.WriteAsync(buffer[..bufferIndex], cancellationToken).VhConfigureAwait();
        }
    }

    private async Task ReadTask(CancellationToken cancellationToken)
    {
        var streamPacketReader = new StreamPacketReader(_clientStream.Stream, TunnelDefaults.StreamPacketReaderBufferSize);
        while (!cancellationToken.IsCancellationRequested && !_isCloseReceived && !_disposed) {
            var ipPacket = await streamPacketReader.ReadAsync(cancellationToken).VhConfigureAwait();
            if (ipPacket == null)
                break;

            // check datagram message
            if (ProcessMessage(ipPacket)) {
                ipPacket.Dispose();
                continue;
            }

            OnPacketReceived(ipPacket);
        }
    }

    private bool ProcessMessage(IpPacket ipPacket)
    {
        if (!DatagramMessageHandler.IsDatagramMessage(ipPacket))
            return false;

        var message = DatagramMessageHandler.ReadMessage(ipPacket);
        if (message is not CloseDatagramMessage)
            return false;

        _isCloseReceived = true;
        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.DatagramChannel,
            "Receiving the close message from the peer. ChannelId: {ChannelId}, Lifetime: {Lifetime}, IsCloseSent: {IsCloseSent}",
            ChannelId, _lifeTime, _isCloseSent);

        return true;
    }

    private async Task SendClose()
    {
        try {
            // already send
            if (_isCloseSent)
                return;

            _isCloseSent = true;
            _cancellationTokenSource.CancelAfter(TunnelDefaults.TcpGracefulTimeout);

            // send close message to peer
            using var ipPacket = DatagramMessageHandler.CreateMessage(new CloseDatagramMessage());
            VhLogger.Instance.LogDebug(GeneralEventId.DatagramChannel,
                "StreamDatagramChannel sending the close message to the remote. ChannelId: {ChannelId}, Lifetime: {Lifetime}",
                ChannelId, _lifeTime);

            await SendPacketsAsync([ipPacket]);
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.DatagramChannel, ex,
                "Could not send the close message to the remote. ChannelId: {ChannelId}, Lifetime: {Lifetime}",
                ChannelId, _lifeTime);
        }
        finally {
            Connected = false;
        }
    }

    public Task RunJob()
    {
        if (Connected && FastDateTime.Now > _lifeTime) {
            VhLogger.Instance.LogDebug(GeneralEventId.DatagramChannel,
                "StreamDatagramChannel lifetime ended. ChannelId: {ChannelId}, Lifetime: {Lifetime}",
                ChannelId, _lifeTime);

            return SendClose();
        }

        return Task.CompletedTask;
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing) {
            //_clientStream.Dispose(); //todo: do later
        }

        base.Dispose(disposing);
    }

    //todo remove
    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }

    private bool _disposed;
    private readonly AsyncLock _disposeLock = new();

    public async ValueTask DisposeAsync(bool graceful)
    {
        using var lockResult = await _disposeLock.LockAsync().VhConfigureAwait();
        if (_disposed) return;

        if (graceful)
            await SendClose().VhConfigureAwait(); // this won't throw any error

        _clientStream.Dispose();
        _disposed = true;
    }
}