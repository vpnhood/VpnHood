using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Core.Common.Jobs;
using VpnHood.Core.Common.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Common.Utils;
using VpnHood.Core.Tunneling.ClientStreams;
using VpnHood.Core.Tunneling.DatagramMessaging;

namespace VpnHood.Core.Tunneling.Channels;

public class StreamDatagramChannel : IDatagramChannel, IJob
{
    private readonly byte[] _buffer = new byte[0xFFFF];
    private const int Mtu = 0xFFFF;
    private readonly IClientStream _clientStream;
    private readonly DateTime _lifeTime = DateTime.MaxValue;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _isCloseSent;
    private bool _isCloseReceived;

    public event EventHandler<ChannelPacketReceivedEventArgs>? PacketReceived;
    public JobSection JobSection { get; } = new();
    public string ChannelId { get; }
    public bool Connected { get; private set; }
    public Traffic Traffic { get; } = new();
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;
    public bool IsStream => true;

    public StreamDatagramChannel(IClientStream clientStream, string channelId)
        : this(clientStream, channelId, Timeout.InfiniteTimeSpan)
    {
    }

    public StreamDatagramChannel(IClientStream clientStream, string channelId, TimeSpan lifespan)
    {
        ChannelId = channelId;
        _clientStream = clientStream ?? throw new ArgumentNullException(nameof(clientStream));
        if (!VhUtil.IsInfinite(lifespan)) {
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
            throw new ObjectDisposedException("StreamDatagramChannel");

        if (Connected)
            throw new Exception("StreamDatagramChannel has been already started.");

        Connected = true;
        try {
            await ReadTask(_cancellationTokenSource.Token).VhConfigureAwait();
            await SendClose().VhConfigureAwait();
        }
        finally {
            Connected = false;
            _ = DisposeAsync();
        }
    }

    public Task SendPacket(IList<IPPacket> ipPackets)
    {
        return SendPacket(ipPackets, false);
    }

    public async Task SendPacket(IList<IPPacket> ipPackets, bool disconnect)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        try {
            await _sendSemaphore.WaitAsync(_cancellationTokenSource.Token).VhConfigureAwait();

            // check channel connectivity
            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
            if (!Connected)
                throw new Exception($"The StreamDatagramChannel is disconnected. ChannelId: {ChannelId}.");

            // copy packets to buffer
            var buffer = _buffer;
            var bufferIndex = 0;

            var dataLen = 0;
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < ipPackets.Count; i++) {
                var ipPacket = ipPackets[i];

                // check MTU
                dataLen += ipPackets[i].TotalPacketLength;
                if (dataLen > Mtu)
                    throw new InvalidOperationException(
                        $"Total packets length is too big for this StreamDatagramChannel. ChannelId {ChannelId}, MaxSize: {Mtu}, Packets Size: {dataLen}");

                // copy to buffer
                Buffer.BlockCopy(ipPacket.Bytes, 0, buffer, bufferIndex, ipPacket.TotalLength);
                bufferIndex += ipPacket.TotalLength;
            }

            await _clientStream.Stream.WriteAsync(buffer, 0, bufferIndex, _cancellationTokenSource.Token)
                .VhConfigureAwait();
            LastActivityTime = FastDateTime.Now;
            Traffic.Sent += bufferIndex;
        }
        finally {
            if (disconnect) Connected = false;
            _sendSemaphore.Release();
        }
    }

    private async Task ReadTask(CancellationToken cancellationToken)
    {
        var stream = _clientStream.Stream;

        try {
            await using var streamPacketReader = new StreamPacketReader(stream);
            while (!cancellationToken.IsCancellationRequested && !_isCloseReceived) {
                var ipPackets = await streamPacketReader.ReadAsync(cancellationToken).VhConfigureAwait();
                if (ipPackets == null || _disposed)
                    break;

                LastActivityTime = FastDateTime.Now;
                Traffic.Received += ipPackets.Sum(x => x.TotalLength);

                // check datagram message
                List<IPPacket>? processedPackets = null;
                // ReSharper disable once ForCanBeConvertedToForeach
                for (var i = 0; i < ipPackets.Length; i++) {
                    var ipPacket = ipPackets[i];
                    if (ProcessMessage(ipPacket)) {
                        processedPackets ??= [];
                        processedPackets.Add(ipPacket);
                    }
                }

                // remove all processed packets
                if (processedPackets != null)
                    ipPackets = ipPackets.Except(processedPackets).ToArray();

                // fire new packets
                if (ipPackets.Length > 0)
                    PacketReceived?.Invoke(this, new ChannelPacketReceivedEventArgs(ipPackets, this));
            }
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.Packet, ex, "Could not read packet from StreamDatagram.");
            throw;
        }
    }

    private bool ProcessMessage(IPPacket ipPacket)
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

    private Task SendClose(bool throwException = false)
    {
        try {
            // already send
            if (_isCloseSent)
                return Task.CompletedTask;
            _isCloseSent = true;
            _cancellationTokenSource.CancelAfter(TunnelDefaults.TcpGracefulTimeout);

            // send close message to peer
            var ipPacket = DatagramMessageHandler.CreateMessage(new CloseDatagramMessage());
            VhLogger.Instance.LogTrace(GeneralEventId.DatagramChannel,
                "StreamDatagramChannel sending the close message to the remote. ChannelId: {ChannelId}, Lifetime: {Lifetime}",
                ChannelId, _lifeTime);

            return SendPacket([ipPacket], true);
        }
        catch (Exception ex) {
            VhLogger.LogError(GeneralEventId.DatagramChannel, ex,
                "Could not set the close message to the remote. ChannelId: {ChannelId}, Lifetime: {Lifetime}",
                ChannelId, _lifeTime);

            if (throwException)
                throw;

            return Task.CompletedTask;
        }
    }

    public Task RunJob()
    {
        if (Connected && FastDateTime.Now > _lifeTime) {
            VhLogger.Instance.LogTrace(GeneralEventId.DatagramChannel,
                "StreamDatagramChannel lifetime ended. ChannelId: {ChannelId}, Lifetime: {Lifetime}",
                ChannelId, _lifeTime);

            return SendClose();
        }

        return Task.CompletedTask;
    }

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

        await _clientStream.DisposeAsync(graceful).VhConfigureAwait();
        _disposed = true;
    }
}