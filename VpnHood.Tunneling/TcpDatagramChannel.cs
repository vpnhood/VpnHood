using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.JobController;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.DatagramMessaging;

namespace VpnHood.Tunneling;

public class TcpDatagramChannel : IDatagramChannel, IJob
{
    private readonly byte[] _buffer = new byte[0xFFFF];
    private const int Mtu = 0xFFFF;
    private readonly TcpClientStream _tcpClientStream;
    private bool _disposed;
    private readonly DateTime _lifeTime = DateTime.MaxValue;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    
    public event EventHandler<ChannelEventArgs>? OnFinished;
    public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
    public JobSection? JobSection => null;
    public bool IsClosePending { get; private set; }
    public bool Connected { get; private set; }
    public long SentByteCount { get; private set; }
    public long ReceivedByteCount { get; private set; }
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;

    public TcpDatagramChannel(TcpClientStream tcpClientStream)
    : this(tcpClientStream, Timeout.InfiniteTimeSpan)
    {
    }

    public TcpDatagramChannel(TcpClientStream tcpClientStream, TimeSpan lifespan)
    {
        _tcpClientStream = tcpClientStream ?? throw new ArgumentNullException(nameof(tcpClientStream));
        tcpClientStream.TcpClient.NoDelay = true;
        if (lifespan != Timeout.InfiniteTimeSpan)
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
            throw new ObjectDisposedException(VhLogger.FormatTypeName(this));

        Connected = true;
        return ReadTask();
    }

    public async Task SendPacketAsync(IPPacket[] ipPackets)
    {
        if (_disposed)
            throw new ObjectDisposedException(VhLogger.FormatTypeName(this));

        try
        {
            await _sendSemaphore.WaitAsync();

            var dataLen = ipPackets.Sum(x => x.TotalPacketLength);
            if (dataLen > Mtu)
                throw new InvalidOperationException(
                    $"Total packets length is too big for {VhLogger.FormatTypeName(this)}. MaxSize: {Mtu}, Packets Size: {dataLen}");

            // copy packets to buffer
            var buffer = _buffer;
            var bufferIndex = 0;

            foreach (var ipPacket in ipPackets)
            {
                Buffer.BlockCopy(ipPacket.Bytes, 0, buffer, bufferIndex, ipPacket.TotalPacketLength);
                bufferIndex += ipPacket.TotalPacketLength;
            }

            await _tcpClientStream.Stream.WriteAsync(buffer, 0, bufferIndex);
            LastActivityTime = FastDateTime.Now;
            SentByteCount += bufferIndex;
        }
        finally
        {
            _sendSemaphore.Release();
        }
    }

    private async Task ReadTask()
    {
        var tcpClient = _tcpClientStream.TcpClient;
        var stream = _tcpClientStream.Stream;

        try
        {
            var streamPacketReader = new StreamPacketReader(stream);
            while (tcpClient.Connected)
            {
                var ipPackets = await streamPacketReader.ReadAsync();
                if (ipPackets == null || _disposed)
                    break;

                LastActivityTime = FastDateTime.Now;
                ReceivedByteCount += ipPackets.Sum(x => x.TotalPacketLength);
                FireReceivedPackets(ipPackets);

                // check datagram message
                foreach (var ipPacket in ipPackets)
                    if (DatagramMessageHandler.IsDatagramMessage(ipPacket))
                        ProcessMessage(DatagramMessageHandler.ReadMessage(ipPacket));
            }
        }
        catch (Exception ex)
        {
            if (VhLogger.IsDiagnoseMode)
                VhLogger.Instance.LogError(GeneralEventId.Udp, ex, "Error in reading UDP.");
        }
        finally
        {
            Dispose();
        }
    }

    private void ProcessMessage(DatagramBaseMessage message)
    {
        if (message is not CloseDatagramMessage) return;

        VhLogger.Instance.Log(LogLevel.Information, GeneralEventId.DatagramChannel,
            "Receiving the close message from the peer. Lifetime: {Lifetime}, CurrentClosePending: {IsClosePending}", _lifeTime, IsClosePending);

        // dispose if this channel is already sent its request and get the answer from peer
        if (IsClosePending)
            Dispose();
        else
            _ = SendCloseMessageAsync();
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

    private void FireReceivedPackets(IPPacket[] ipPackets)
    {
        if (_disposed)
            return;

        try
        {
            OnPacketReceived?.Invoke(this, new ChannelPacketReceivedEventArgs(ipPackets, this));
        }
        catch (Exception ex)
        {
            VhLogger.Instance.Log(LogLevel.Warning, GeneralEventId.Udp,
                $"Error in processing received packets! Error: {ex.Message}");
        }
    }

    public Task RunJob()
    {
        if (!IsClosePending && FastDateTime.Now > _lifeTime)
            _ = SendCloseMessageAsync();

        return Task.CompletedTask;
    }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tcpClientStream.Dispose();
        Connected = false;
        OnFinished?.Invoke(this, new ChannelEventArgs(this));
    }
}