using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.ClientStreams;
using VpnHood.Tunneling.DatagramMessaging;

namespace VpnHood.Tunneling.Channels;

public class StreamDatagramChannel : IDatagramChannel, IJob
{
    private readonly byte[] _buffer = new byte[0xFFFF];
    private const int Mtu = 0xFFFF;
    private readonly IClientStream _clientStream;
    private bool _disposed;
    private readonly DateTime _lifeTime = DateTime.MaxValue;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private bool _isCloseSent;
    private bool _isCloseReceived;

    public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
    public JobSection JobSection { get; } = new();
    public string ChannelId { get; }
    public bool Connected { get; private set; }
    public Traffic Traffic { get; } = new();
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;

    public StreamDatagramChannel(IClientStream clientStream, string channelId)
    : this(clientStream, channelId, Timeout.InfiniteTimeSpan)
    {
    }

    public StreamDatagramChannel(IClientStream clientStream, string channelId, TimeSpan lifespan)
    {
        ChannelId = channelId;
        _clientStream = clientStream ?? throw new ArgumentNullException(nameof(clientStream));
        if (!VhUtil.IsInfinite(lifespan))
        {
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
        try
        {
            await ReadTask(_cancellationTokenSource.Token);
            await SendClose();
        }
        finally
        {
            Connected = false;
            _ = DisposeAsync();
        }
    }

    public Task SendPacket(IPPacket[] ipPackets)
    {
        return SendPacket(ipPackets, false);
    }

    public async Task SendPacket(IPPacket[] ipPackets, bool disconnect)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        try
        {
            await _sendSemaphore.WaitAsync(_cancellationTokenSource.Token);

            // check channel connectivity
            _cancellationTokenSource.Token.ThrowIfCancellationRequested();
            if (!Connected)
                throw new Exception($"The StreamDatagramChannel is disconnected. ChannelId: {ChannelId}.");

            // check MTU
            var dataLen = ipPackets.Sum(x => x.TotalPacketLength);
            if (dataLen > Mtu)
                throw new InvalidOperationException(
                    $"Total packets length is too big for this StreamDatagramChannel. ChannelId {ChannelId}, MaxSize: {Mtu}, Packets Size: {dataLen}");

            // copy packets to buffer
            var buffer = _buffer;
            var bufferIndex = 0;

            foreach (var ipPacket in ipPackets)
            {
                Buffer.BlockCopy(ipPacket.Bytes, 0, buffer, bufferIndex, ipPacket.TotalPacketLength);
                bufferIndex += ipPacket.TotalPacketLength;
            }

            await _clientStream.Stream.WriteAsync(buffer, 0, bufferIndex, _cancellationTokenSource.Token);
            LastActivityTime = FastDateTime.Now;
            Traffic.Sent += bufferIndex;
        }
        finally
        {
            if (disconnect) Connected = false;
            _sendSemaphore.Release();
        }
    }

    private async Task ReadTask(CancellationToken cancellationToken)
    {
        var stream = _clientStream.Stream;

        try
        {
            using var streamPacketReader = new StreamPacketReader(stream);
            while (!cancellationToken.IsCancellationRequested && !_isCloseReceived)
            {
                var ipPackets = await streamPacketReader.ReadAsync(cancellationToken);
                if (ipPackets == null || _disposed)
                    break;

                LastActivityTime = FastDateTime.Now;
                Traffic.Received += ipPackets.Sum(x => x.TotalPacketLength);

                // check datagram message
                List<IPPacket>? processedPackets = null;
                foreach (var ipPacket in ipPackets)
                    if (ProcessMessage(ipPacket))
                    {
                        processedPackets ??= new List<IPPacket>();
                        processedPackets.Add(ipPacket);
                    }

                // remove all processed packets
                if (processedPackets != null)
                    ipPackets = ipPackets.Except(processedPackets).ToArray();

                // fire new packets
                if (ipPackets.Length > 0)
                    OnPacketReceived?.Invoke(this, new ChannelPacketReceivedEventArgs(ipPackets, this));
            }
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.Udp, ex, "Could not read UDP from StreamDatagram.");
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
        try
        {
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

            return SendPacket(new[] { ipPacket }, true);
        }
        catch (Exception ex)
        {
            VhLogger.LogError(GeneralEventId.DatagramChannel, ex,
                "Could not set the close message to the remote. ChannelId: {ChannelId}, Lifetime: {Lifetime}",
                ChannelId, _lifeTime);
            
            if (throwException)
                throw;

            return Task.CompletedTask;
        }
    }

    public async Task RunJob()
    {
        if (Connected && FastDateTime.Now > _lifeTime)
        {
            VhLogger.Instance.LogTrace(GeneralEventId.DatagramChannel,
                "StreamDatagramChannel lifetime ended. ChannelId: {ChannelId}, Lifetime: {Lifetime}", 
                ChannelId, _lifeTime);

            await SendClose();
        }
    }

    private readonly AsyncLock _disposeLock = new();
    private ValueTask? _disposeTask;
    public ValueTask DisposeAsync()
    {
        return DisposeAsync(true);
    }

    public ValueTask DisposeAsync(bool graceFul)
    {
        lock (_disposeLock)
            _disposeTask ??= DisposeAsyncCore(graceFul);
        return _disposeTask.Value;
    }

    private async ValueTask DisposeAsyncCore(bool graceFul)
    {
        if (graceFul)
            await SendClose(); // this won't throw any error

        await _clientStream.DisposeAsync(graceFul);
        _disposed = true;
    }
}