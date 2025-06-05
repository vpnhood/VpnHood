using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Channels;

public abstract class UdpChannelTransmitter : IDisposable
{
    private readonly EventReporter _udpSignReporter = new("Invalid udp signature.", GeneralEventId.UdpSign);
    private readonly byte[] _buffer = new byte[TunnelDefaults.MaxPacketSize];
    private readonly UdpClient _udpClient;
    private readonly SemaphoreSlim _semaphore = new(1, 1);
    private readonly BufferCryptor _serverEncryptor;
    private readonly BufferCryptor _serverDecryptor; // decryptor must be different object for thread safety
    private readonly RandomNumberGenerator _randomGenerator = RandomNumberGenerator.Create();
    private bool _disposed;
    public const int HeaderLength = 32; //IV (8) + Sign (2) + Reserved (6) + SessionId (8) + SessionPos (8)
    public const int SendHeaderLength = HeaderLength - 8; //IV will not be encrypted
    
    public int? ReceiveBufferSize {
        get => _udpClient.Client.ReceiveBufferSize;
        set => _udpClient.Client.ReceiveBufferSize = value ?? new UdpClient().Client.ReceiveBufferSize;
    }

    public int? SendBufferSize {
        get => _udpClient.Client.SendBufferSize;
        set => _udpClient.Client.SendBufferSize = value ?? new UdpClient().Client.SendBufferSize;
    }

    protected UdpChannelTransmitter(UdpClient udpClient, byte[] serverKey)
    {
        _udpClient = udpClient;
        _serverEncryptor = new BufferCryptor(serverKey);
        _serverDecryptor = new BufferCryptor(serverKey);
        _ = ReadTask();
    }

    public IPEndPoint LocalEndPoint => _udpClient.Client.GetLocalEndPoint();

    public async Task<int> SendAsync(IPEndPoint? ipEndPoint, ulong sessionId, long sessionCryptoPosition,
        Memory<byte> buffer, int protocolVersion)
    {
        try {
            await _semaphore.WaitAsync().Vhc();
            var bufferSpan = buffer.Span;
            Span<byte> sendIv = stackalloc byte[8];

            // add random packet iv
            _randomGenerator.GetBytes(sendIv);
            sendIv.CopyTo(bufferSpan);

            // add packet signature
            bufferSpan[8] = (byte)'O';
            bufferSpan[9] = (byte)'K';

            // write session info into the buffer
            BinaryPrimitives.WriteUInt64LittleEndian(bufferSpan.Slice(16, 8), sessionId);
            BinaryPrimitives.WriteInt64LittleEndian(bufferSpan.Slice(24, 8), sessionCryptoPosition);


            // encrypt session info part. It encrypts the session number and counter by server key and perform bitwise XOR on header.
            // the data is not important, but it removes the signature of our packet by obfuscating the packet head. Also, the counter of session 
            // never repeats so ECB generate a new key each time. It is just for the head obfuscation and the reset of data will encrypt
            // by session key and that counter
            Span<byte> sendHeadKeyBuffer = stackalloc byte[SendHeaderLength]; // IV will not be encrypted
            _serverEncryptor.Cipher(sendHeadKeyBuffer, BitConverter.ToInt64(sendIv));
            for (var i = 0; i < sendHeadKeyBuffer.Length; i++)
                bufferSpan[sendIv.Length + i] ^= sendHeadKeyBuffer[i]; //simple XOR with generated unique key

            // copy buffer to byte array because SendAsync does not support Memory<byte> in .NET standard 2.1
            bufferSpan.CopyTo(_buffer.AsSpan());

            // send packet to destination
            var ret = ipEndPoint != null
                ? await _udpClient.SendAsync(_buffer, bufferSpan.Length, ipEndPoint).Vhc()
                : await _udpClient.SendAsync(_buffer, bufferSpan.Length).Vhc();

            if (ret != buffer.Length)
                throw new Exception($"UdpClient: Send {ret} bytes instead {buffer.Length} bytes.");

            return ret;
        }
        catch (Exception ex) {
            if (IsInvalidState(ex))
                Dispose();

            VhLogger.Instance.LogError(GeneralEventId.Udp, ex,
                "UdpChannelTransmitter: Could not send data. DataLength: {DataLength}, DestinationIp: {DestinationIp}",
                buffer.Length, VhLogger.Format(ipEndPoint));
            throw;
        }
        finally {
            _semaphore.Release();
        }
    }

    private async Task ReadTask()
    {
        Memory<byte> headKeyBuffer = new byte[SendHeaderLength];

        // wait for all incoming UDP packets
        while (!_disposed) {
            IPEndPoint? remoteEndPoint = null;
            try {
                remoteEndPoint = null;
                var udpResult = await _udpClient.ReceiveAsync().Vhc();
                remoteEndPoint = udpResult.RemoteEndPoint;
                var buffer = udpResult.Buffer;
                if (buffer.Length < HeaderLength)
                    throw new Exception("Invalid UDP packet size. Could not find its header.");

                // build header key
                var bufferIndex = 0;
                var iv = BitConverter.ToInt64(buffer, 0);
                headKeyBuffer.Span.Clear();
                _serverDecryptor.Cipher(headKeyBuffer.Span, iv);
                bufferIndex += 8;

                // decrypt header
                for (var i = 0; i < headKeyBuffer.Length; i++)
                    buffer[bufferIndex + i] ^= headKeyBuffer.Span[i]; //simple XOR with the generated unique key

                // check packet signature OK
                if (buffer[8] != 'O' || buffer[9] != 'K') {
                    _udpSignReporter.Raise();
                    throw new Exception("Packet signature does not match.");
                }

                // read session and session position
                bufferIndex += 8;
                var sessionId = BitConverter.ToUInt64(buffer, bufferIndex); 
                bufferIndex += 8;
                var channelCryptorPosition = BitConverter.ToInt64(buffer, bufferIndex);
                bufferIndex += 8;

                OnReceiveData(sessionId, udpResult.RemoteEndPoint, 
                    udpResult.Buffer.AsMemory(bufferIndex), channelCryptorPosition);
            }
            catch (Exception ex) {
                // finish if disposed
                if (_disposed) {
                    VhLogger.Instance.LogInformation(GeneralEventId.Essential,
                        "UdpChannelTransmitter has been stopped.");
                    break;
                }

                // break only for the first call and means that the local endpoint can not be bind
                if (remoteEndPoint == null) {
                    VhLogger.LogError(GeneralEventId.Essential, ex, "UdpChannelTransmitter has stopped reading.");
                    break;
                }

                VhLogger.Instance.LogWarning(GeneralEventId.Udp,
                    "Error in receiving UDP packets. RemoteEndPoint: {RemoteEndPoint}, Message: {Message}",
                    VhLogger.Format(remoteEndPoint), ex.Message);
            }
        }

        Dispose();
    }

    protected abstract void OnReceiveData(ulong sessionId, IPEndPoint remoteEndPoint,
        Memory<byte> buffer,
        long channelCryptorPosition);

    private bool IsInvalidState(Exception ex)
    {
        return
            _disposed ||
            ex is ObjectDisposedException or SocketException { SocketErrorCode: SocketError.InvalidArgument };
    }

    public void Dispose()
    {
        _disposed = true;
        _udpClient.Dispose();
    }
}