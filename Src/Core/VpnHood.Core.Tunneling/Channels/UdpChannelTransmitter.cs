using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Channels;

public abstract class UdpChannelTransmitter : IDisposable
{
    // Packet format: version(1) | sessionId(8) | seq(8) | tag(16) | ciphertext
    // AAD: sessionId | seq

    private const int VersionOffset = 0;
    private const int VersionLength = 1;
    private const int SessionIdOffset = VersionOffset + VersionLength;
    private const int SessionIdLength = 8;
    private const int SeqOffset = SessionIdOffset + SessionIdLength;
    private const int SeqLength = 8;
    private const int TagOffset = SeqOffset + SeqLength;
    public const int TagLength = 16;

    private readonly EventReporter _udpSignReporter = new("Invalid udp signature.", GeneralEventId.UdpSign);
    private readonly EventReporter _invalidSessionReporter = new("Invalid UDP session.", GeneralEventId.UdpSign);
    private readonly Memory<byte> _sendBuffer = new byte[TunnelDefaults.MaxPacketSize];
    private readonly UdpClient _udpClient;
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private static ulong _sendSequenceNumber;
    private bool _disposed;
    private bool _isSendBufferSizeCustomized;
    private bool _isReceivedBufferSizeCustomized;

    protected abstract SessionUdpTransport? SessionIdToUdpTransport(ulong sessionId);

    public int MaxPacketSize { get; set; } = TunnelDefaults.MaxPacketSize;
    public const int HeaderLength = TagOffset + TagLength;
    public IPEndPoint LocalEndPoint { get; }
    public bool Connected => !_disposed;

    protected UdpChannelTransmitter(UdpClient udpClient)
    {
        _udpClient = udpClient;
        LocalEndPoint = udpClient.Client.GetLocalEndPoint();

        // Initialize send sequence number. one per application lifetime is sufficient.
        if (_sendSequenceNumber == 0) {
            Span<byte> seqBytes = stackalloc byte[8];
            RandomNumberGenerator.Fill(seqBytes);
            _sendSequenceNumber = BinaryPrimitives.ReadUInt64LittleEndian(seqBytes);
        }

        Task.Run(ReadLoopAsync);
    }

    public TransferBufferSize? BufferSize {
        get => new(_udpClient.Client.SendBufferSize, _udpClient.Client.ReceiveBufferSize);
        set {
            using var udpClient = new UdpClient(_udpClient.Client.AddressFamily);

            // Send buffer size
            if (value?.Send > 0) {
                _isSendBufferSizeCustomized = true;
                _udpClient.Client.SendBufferSize = value.Value.Send;
            }
            else if (_isSendBufferSizeCustomized) {
                _udpClient.Client.SendBufferSize = udpClient.Client.SendBufferSize;
            }

            // Receive buffer size
            if (value?.Receive > 0) {
                _isReceivedBufferSizeCustomized = true;
                _udpClient.Client.ReceiveBufferSize = value.Value.Receive;
            }
            else if (_isReceivedBufferSizeCustomized) {
                _udpClient.Client.ReceiveBufferSize = udpClient.Client.ReceiveBufferSize;
            }
        }
    }

    private static void BuildNonce(Span<byte> nonce, ulong sessionId, ulong seq)
    {
        // nonce = seq[8] + sessionSalt[4]
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[..8], seq);
        var sessionSalt = (uint)(sessionId ^ (sessionId >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(nonce.Slice(8, 4), sessionSalt);
    }

    internal async Task SendAsync(ulong sessionId, ReadOnlyMemory<byte> payload, IPEndPoint ipEndPoint, IChannelCryptor cryptor)
    {
        if (payload.Length + HeaderLength > MaxPacketSize)
            throw new ArgumentOutOfRangeException(nameof(payload));

        try {
            // encryption is not thread-safe; serialize send
            await _sendSemaphore.WaitAsync().Vhc();
            await SendCoreAsync(sessionId, ipEndPoint, payload, cryptor);
        }
        catch (Exception ex) when (SocketUtils.IsInvalidUdpStateException(ex)) {
            VhLogger.Instance.LogError(GeneralEventId.Udp, ex,
                "UdpChannelTransmitter: Socket is in invalid state. Disposing the transmitter. " +
                "DataLength: {DataLength}, DestinationIp: {DestinationIp}",
                payload.Length, VhLogger.Format(ipEndPoint));

            Dispose();
            throw;
        }
        catch (Exception ex) {
            VhLogger.Instance.LogError(GeneralEventId.Udp, ex,
                "UdpChannelTransmitter: Could not send data. DataLength: {DataLength}, DestinationIp: {DestinationIp}",
                payload.Length, VhLogger.Format(ipEndPoint));

            throw;
        }
        finally {
            _sendSemaphore.Release();
        }
    }

    private async Task SendCoreAsync(ulong sessionId, IPEndPoint ipEndPoint, ReadOnlyMemory<byte> payload,
        IChannelCryptor cryptor)
    {
        var currentSeq = _sendSequenceNumber++;
        var bufferSpan = _sendBuffer.Span;

        // sessionId[8]
        BinaryPrimitives.WriteUInt64LittleEndian(bufferSpan.Slice(SessionIdOffset, SessionIdLength), sessionId);

        // seq[8]
        BinaryPrimitives.WriteUInt64LittleEndian(bufferSpan.Slice(SeqOffset, SeqLength), currentSeq);

        // reserved[1]
        bufferSpan[VersionOffset] = 0;

        // tag[16]
        var tag = bufferSpan.Slice(TagOffset, TagLength);
        tag.Clear();

        // ciphertext (follows header)
        var ciphertext = bufferSpan.Slice(HeaderLength, payload.Length);

        // AAD = sessionId|seq
        var associatedData = bufferSpan.Slice(SessionIdOffset, SessionIdLength + SeqLength);

        Span<byte> nonce = stackalloc byte[12];
        BuildNonce(nonce, sessionId, currentSeq);

        cryptor.Encrypt(nonce, payload.Span, ciphertext, tag, associatedData);

        var totalLength = HeaderLength + payload.Length;
        var sent = await _udpClient.SendAsync(_sendBuffer[..totalLength], ipEndPoint)
            .Vhc();
        if (sent != totalLength)
            throw new Exception($"UdpClient: Sent {sent} bytes instead of {totalLength} bytes.");
    }

    private async Task ReadLoopAsync()
    {
        var nonce = new byte[12];
        var plainTextBuffer = new byte[MaxPacketSize];

        while (!_disposed) {
            try {
                var result = await _udpClient.ReceiveAsync().Vhc();
                var data = result.Buffer;
                if (data.Length < HeaderLength) {
                    _udpSignReporter.Raise();
                    continue;
                }

                var span = data.AsSpan();
                var sessionId = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(SessionIdOffset, SessionIdLength));
                var seq = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(SeqOffset, SeqLength));

                // Find transport
                var transport = SessionIdToUdpTransport(sessionId);
                if (transport is null) {
                    _invalidSessionReporter.Raise();
                    continue;
                }

                // update remote endpoint for server, because client may use different port due to NAT
                if (transport.IsServer)
                    transport.RemoteEndPoint = result.RemoteEndPoint;

                var cryptor = transport.ReceiveCryptor;

                // Extract fields
                _ = span[VersionOffset];
                var tag = span.Slice(TagOffset, TagLength);
                var ciphertext = span[HeaderLength..];
                var associatedData = span.Slice(SessionIdOffset, SessionIdLength + SeqLength);
                BuildNonce(nonce, sessionId, seq);

                // Decrypt
                var length = ciphertext.Length;
                if (length > plainTextBuffer.Length)
                    throw new InvalidOperationException(
                        $"Receive buffer too small. length={length}, buffer={plainTextBuffer.Length}");

                // Decrypt in a shared buffer to reduce memory allocation
                var plainTextSpan = plainTextBuffer.AsSpan(0, length);
                cryptor.Decrypt(nonce, ciphertext, tag, plainTextSpan, associatedData);

                // Copy to the already allocated result.Buffer to reduce memory copy
                var plainTextMemory = result.Buffer.AsMemory(0, length);
                plainTextSpan.CopyTo(plainTextMemory.Span);
                transport.DataReceived?.Invoke(plainTextMemory);
            }
            catch (Exception) when (_disposed) {
                break;
            }
            catch (Exception ex) when (SocketUtils.IsInvalidUdpStateException(ex)) {
                VhLogger.Instance.LogError(GeneralEventId.Udp, ex, "UdpChannelTransmitter: Read loop crashed.");
                Dispose();
                break;
            }
            catch (Exception) {
                _udpSignReporter.Raise();
            }
        }
    }

    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _udpClient.Dispose();
        _sendSemaphore.Dispose();
        _invalidSessionReporter.Dispose();
        _udpSignReporter.Dispose();
    }
}