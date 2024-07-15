using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;
using VpnHood.Tunneling.Utils;

namespace VpnHood.Tunneling.Channels;

public class UdpChannel(ulong sessionId, byte[] sessionKey, bool isServer, int protocolVersion)
    : IDatagramChannel
{
    private IPEndPoint? _lastRemoteEp;
    private readonly byte[] _buffer = new byte[0xFFFF];
    private UdpChannelTransmitter? _udpChannelTransmitter;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly BufferCryptor _sessionCryptorWriter = new(sessionKey);
    private readonly BufferCryptor _sessionCryptorReader = new(sessionKey);
    private bool _disposed;
    private readonly long _cryptorPosBase = isServer ? DateTime.UtcNow.Ticks : 0; // make sure server does not use client position as IV
    private readonly List<IPPacket> _receivedIpPackets = [];

    public event EventHandler<ChannelPacketReceivedEventArgs>? PacketReceived;
    public string ChannelId { get; } = Guid.NewGuid().ToString();
    public bool IsStream => false;

    public bool IsClosePending => false;
    public bool Connected { get; private set; }
    public DateTime LastActivityTime { get; private set; }
    public Traffic Traffic { get; } = new();
    public void Start()
    {
        if (_disposed)
            throw new ObjectDisposedException(GetType().Name);

        if (Connected)
            throw new InvalidOperationException("The udpChannel is already started.");

        Connected = true;
        LastActivityTime = FastDateTime.Now;
    }

    public async Task SendPacket(IList<IPPacket> ipPackets)
    {
        try
        {
            // this is shared buffer and client, so we need to sync
            // Using multiple UdpClient will not increase performance
            await _semaphore.WaitAsync().VhConfigureAwait();

            var bufferIndex = UdpChannelTransmitter.HeaderLength;

            // copy packets
            // ReSharper disable once ForCanBeConvertedToForeach
            for (var i = 0; i < ipPackets.Count; i++)
            {
                var ipPacket = ipPackets[i];
                Buffer.BlockCopy(ipPacket.Bytes, 0, _buffer, bufferIndex, ipPacket.TotalLength);
                bufferIndex += ipPacket.TotalPacketLength;
            }

            // encrypt packets
            var sessionCryptoPosition = _cryptorPosBase + Traffic.Sent;
            _sessionCryptorWriter.Cipher(_buffer, UdpChannelTransmitter.HeaderLength, bufferIndex - UdpChannelTransmitter.HeaderLength, sessionCryptoPosition);

            // send buffer
            if (_lastRemoteEp == null) throw new InvalidOperationException("RemoveEndPoint has not been initialized yet in UdpChannel.");
            if (_udpChannelTransmitter == null) throw new InvalidOperationException("UdpChannelTransmitter has not been initialized yet in UdpChannel.");
            var ret = await _udpChannelTransmitter.SendAsync(_lastRemoteEp, sessionId, 
                sessionCryptoPosition, _buffer, bufferIndex, protocolVersion).VhConfigureAwait();

            Traffic.Sent += ret;
            LastActivityTime = FastDateTime.Now;
        }
        catch (Exception ex)
        {
            if (IsInvalidState(ex))
                await DisposeAsync().VhConfigureAwait();
        }
        finally
        {
            _semaphore.Release();
        }
    }

    public void SetRemote(UdpChannelTransmitter udpChannelTransmitter, IPEndPoint remoteEndPoint)
    {
        _udpChannelTransmitter = udpChannelTransmitter;
        _lastRemoteEp = remoteEndPoint;
    }

    public void OnReceiveData(long cryptorPosition, byte[] buffer, int bufferIndex)
    {
        _sessionCryptorReader.Cipher(buffer, bufferIndex, buffer.Length - bufferIndex, cryptorPosition);

        // read all packets
        try
        {
            while (bufferIndex < buffer.Length)
            {
                var ipPacket = PacketUtil.ReadNextPacket(buffer, ref bufferIndex);
                Traffic.Received += ipPacket.TotalLength;
                _receivedIpPackets.Add(ipPacket);
            }

            PacketReceived?.Invoke(this, new ChannelPacketReceivedEventArgs(_receivedIpPackets.ToArray(), this));
            LastActivityTime = FastDateTime.Now;
            _receivedIpPackets.Clear();

        }
        catch (Exception ex)
        {
            VhLogger.Instance.LogWarning(GeneralEventId.Udp, ex, "Error in processing packets.");
        }
    }

    private bool IsInvalidState(Exception ex)
    {
        return _disposed || ex is ObjectDisposedException or SocketException { SocketErrorCode: SocketError.InvalidArgument };
    }

    public ValueTask DisposeAsync(bool graceful)
    {
        _ = graceful;
        return DisposeAsync();
    }

    public ValueTask DisposeAsync()
    {
        if (_disposed) return default;
        _disposed = true;
        Connected = false;
        if (!isServer)
            _udpChannelTransmitter?.Dispose();

        return default;
    }
}