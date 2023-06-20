using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using PacketDotNet;
using VpnHood.Common.Logging;
using VpnHood.Common.Messaging;
using VpnHood.Common.Utils;

namespace VpnHood.Tunneling.Channels;

public class UdpChannel2 : IDatagramChannel
{
    private IPEndPoint? _lastRemoteEp;
    private readonly byte[] _buffer = new byte[0xFFFF];
    private UdpChannelTransmitter? _udpChannelTransmitter;
    private readonly ulong _sessionId;
    private readonly bool _isServer;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly BufferCryptor _sessionCryptor;
    private bool _disposed;
    private readonly long _cryptorPosBase;
    private readonly List<IPPacket> _receivedIpPackets = new();

    public event EventHandler<ChannelPacketReceivedEventArgs>? OnPacketReceived;
    public string ChannelId { get; } = Guid.NewGuid().ToString();

    public UdpChannel2(ulong sessionId, byte[] sessionUdpKey, bool isServer)
    {
        _sessionId = sessionId;
        _isServer = isServer;
        _cryptorPosBase = isServer ? DateTime.UtcNow.Ticks : 0; // make sure server does not use client position as IV
        _sessionCryptor = new BufferCryptor(sessionUdpKey);
    }

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

    public async Task SendPacket(IPPacket[] ipPackets)
    {
        try
        {
            // this is shared buffer and client so we need to sync
            // Using multiple UdpClient will not increase performance
            await _semaphore.WaitAsync();

            var bufferIndex = UdpChannelTransmitter.HeaderLength;

            // copy packets
            foreach (var ipPacket in ipPackets)
            {
                Buffer.BlockCopy(ipPacket.Bytes, 0, _buffer, bufferIndex, ipPacket.TotalPacketLength);
                bufferIndex += ipPacket.TotalPacketLength;
            }

            // encrypt packets
            var sessionCryptoPosition = _cryptorPosBase + Traffic.Sent;
            _sessionCryptor.Cipher(_buffer, UdpChannelTransmitter.HeaderLength, bufferIndex - UdpChannelTransmitter.HeaderLength, sessionCryptoPosition);

            // send buffer
            if (_lastRemoteEp == null) throw new InvalidOperationException("RemoveEndPoint has not been initialized yet in UdpChannel.");
            if (_udpChannelTransmitter == null) throw new InvalidOperationException("UdpChannelTransmitter has not been initialized yet in UdpChannel.");
            var ret = await _udpChannelTransmitter.SendAsync(_lastRemoteEp, _sessionId, sessionCryptoPosition, _buffer, bufferIndex);

            Traffic.Sent += ret;
            LastActivityTime = FastDateTime.Now;
        }
        catch (Exception ex)
        {
            if (IsInvalidState(ex))
                await DisposeAsync();
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
        _sessionCryptor.Cipher(buffer, bufferIndex, buffer.Length - bufferIndex, cryptorPosition);

        // read all packets
        try
        {

            while (bufferIndex < buffer.Length)
            {
                var ipPacket = PacketUtil.ReadNextPacket(buffer, ref bufferIndex);
                Traffic.Received += ipPacket.TotalPacketLength;
                _receivedIpPackets.Add(ipPacket);
            }

            OnPacketReceived?.Invoke(this, new ChannelPacketReceivedEventArgs(_receivedIpPackets.ToArray(), this));
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

    public ValueTask DisposeAsync()
    {
        if (_disposed) return default;
        _disposed = true;
        Connected = false;
        if (!_isServer)
            _udpChannelTransmitter?.Dispose();

        return default;
    }
}