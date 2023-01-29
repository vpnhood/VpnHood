using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling;

public class TcpDatagramChannel : IDatagramChannel
{
    private readonly byte[] _buffer = new byte[0xFFFF];
    private const int Mtu = 0xFFFF;
    private readonly TcpClientStream _tcpClientStream;
    private bool _disposed;

    public TcpDatagramChannel(TcpClientStream tcpClientStream)
    {
        _tcpClientStream = tcpClientStream ?? throw new ArgumentNullException(nameof(tcpClientStream));
        tcpClientStream.TcpClient.NoDelay = true;
    }

    public event EventHandler<ChannelEventArgs>? OnFinished;
    public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
    public bool Connected { get; private set; }
    public long SentByteCount { get; private set; }
    public long ReceivedByteCount { get; private set; }
    public DateTime LastActivityTime { get; private set; } = FastDateTime.Now;

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

        var maxDataLen = Mtu;
        var dataLen = ipPackets.Sum(x => x.TotalPacketLength);
        if (dataLen > maxDataLen)
            throw new InvalidOperationException(
                $"Total packets length is too big for {VhLogger.FormatTypeName(this)}. MaxSize: {maxDataLen}, Packets Size: {dataLen} !");

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
            }
        }
        catch
        {
            // ignored
        }
        finally
        {
            Dispose();
        }
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

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _tcpClientStream.Dispose();
        Connected = false;
        OnFinished?.Invoke(this, new ChannelEventArgs(this));
    }

}