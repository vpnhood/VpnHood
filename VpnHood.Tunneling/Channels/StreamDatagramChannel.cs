using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Sockets;
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

    public event EventHandler<ChannelEventArgs>? OnFinished;
    public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
    public JobSection JobSection { get; } = new();
    public bool IsClosePending { get; private set; }
    public bool Connected { get; private set; }
    public Traffic Traffic { get; } = new();
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;

    public StreamDatagramChannel(IClientStream clientStream)
    : this(clientStream, Timeout.InfiniteTimeSpan)
    {
    }

    public StreamDatagramChannel(IClientStream clientStream, TimeSpan lifespan)
    {
        _clientStream = clientStream ?? throw new ArgumentNullException(nameof(clientStream));
        if (!VhUtil.IsInfinite(lifespan))
        {
            _lifeTime = FastDateTime.Now + lifespan;
            JobRunner.Default.Add(this);
        }
    }

    public Task Start()
    {
        if (Connected)
            throw new Exception("TcpDatagram has been already started.");

        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        Connected = true;
        return ReadTask();
    }

    public async Task SendPacketAsync(IPPacket[] ipPackets)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatType(this));

        try
        {
            await _sendSemaphore.WaitAsync();

            var dataLen = ipPackets.Sum(x => x.TotalPacketLength);
            if (dataLen > Mtu)
                throw new InvalidOperationException(
                    $"Total packets length is too big for {VhLogger.FormatType(this)}. MaxSize: {Mtu}, Packets Size: {dataLen}");

            // copy packets to buffer
            var buffer = _buffer;
            var bufferIndex = 0;

            foreach (var ipPacket in ipPackets)
            {
                Buffer.BlockCopy(ipPacket.Bytes, 0, buffer, bufferIndex, ipPacket.TotalPacketLength);
                bufferIndex += ipPacket.TotalPacketLength;
            }

            await _clientStream.Stream.WriteAsync(buffer, 0, bufferIndex);
            LastActivityTime = FastDateTime.Now;
            Traffic.Sent += bufferIndex;
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }

    private async Task ReadTask()
    {
        var stream = _clientStream.Stream;

        try
        {
            var streamPacketReader = new StreamPacketReader(stream);
            while (true)
            {
                var ipPackets = await streamPacketReader.ReadAsync();
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
            if (ex.InnerException is SocketException { SocketErrorCode: SocketError.ConnectionAborted or SocketError.OperationAborted })
                VhLogger.Instance.LogTrace(GeneralEventId.Udp, "StreamDatagram Connection has been aborted.");
            else
                VhLogger.Instance.LogTrace(GeneralEventId.Udp, ex, "Error in reading UDP from StreamDatagram.");
        }
        finally
        {
            await DisposeAsync();
        }
    }

    private bool ProcessMessage(IPPacket ipPacket)
    {
        if (!DatagramMessageHandler.IsDatagramMessage(ipPacket))
            return false;

        var message = DatagramMessageHandler.ReadMessage(ipPacket);
        if (message is not CloseDatagramMessage)
            return false;

        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.DatagramChannel,
            "Receiving the close message from the peer. Lifetime: {Lifetime}, CurrentClosePending: {IsClosePending}", _lifeTime, IsClosePending);

        // dispose if this channel is already sent its request and get the answer from peer
        if (IsClosePending)
            _ = DisposeAsync();
        else
            _ = SendCloseMessageAsync();

        return true;
    }

    private Task SendCloseMessageAsync()
    {
        // already send
        if (IsClosePending)
            return Task.CompletedTask;

        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.DatagramChannel,
            "Sending the close message to the peer... Lifetime: {Lifetime}", _lifeTime);

        var ipPacket = DatagramMessageHandler.CreateMessage(new CloseDatagramMessage());
        IsClosePending = true;
        return SendPacketAsync(new[] { ipPacket });
    }

    public async Task RunJob()
    {
        if (!IsClosePending && FastDateTime.Now > _lifeTime)
            await SendCloseMessageAsync();
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;

        await _clientStream.DisposeAsync();
        Connected = false;
        OnFinished?.Invoke(this, new ChannelEventArgs(this));
    }
}