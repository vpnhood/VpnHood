using Microsoft.Extensions.Logging;
using System.Buffers.Binary;
using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using System.Threading;
using VpnHood.Core.Common.Messaging;
using VpnHood.Core.Toolkit.Logging;
using VpnHood.Core.Toolkit.Net;
using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.Core.Tunneling.Channels;

public abstract class UdpChannelTransmitter : IDisposable
{
    private const int SessionIdLength = 8;
    private const int SeqLength = 8;
    private const int TagLength = 16;
    private readonly CancellationTokenSource _cancellationTokenSource = new ();
    public const int HeaderLength = SessionIdLength + SeqLength + TagLength;

    private readonly EventReporter _udpSignReporter = new("Invalid udp signature.", GeneralEventId.UdpSign);
    private readonly EventReporter _invalidSessionReporter = new( "Invalid UDP session.", GeneralEventId.UdpSign);

    private readonly Memory<byte> _sendBuffer = new byte[TunnelDefaults.MaxPacketSize];
    private readonly UdpClient _udpClient;

    // AesGcm is not thread-safe; serialize send
    private readonly SemaphoreSlim _sendSemaphore = new(1, 1);
    private static ulong _sendSequenceNumber;
    private bool _disposed;

    protected abstract SessionUdpTransport? SessionIdToUdpTransport(ulong sessionId);
    
    public int MaxPacketSize { get; set; } = TunnelDefaults.MaxPacketSize;
    public IPEndPoint LocalEndPoint { get; }

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
            _udpClient.Client.SendBufferSize = value?.Send > 0 ? value.Value.Send : udpClient.Client.SendBufferSize;
            _udpClient.Client.ReceiveTimeout =
                value?.Receive > 0 ? value.Value.Receive : udpClient.Client.ReceiveTimeout;
        }
    }

    private static void BuildNonce(Span<byte> nonce, ulong sessionId, ulong seq)
    {
        // nonce = seq[8] + sessionSalt[4]
        BinaryPrimitives.WriteUInt64LittleEndian(nonce[..8], seq);
        var sessionSalt = (uint)(sessionId ^ (sessionId >> 32));
        BinaryPrimitives.WriteUInt32LittleEndian(nonce.Slice(8, 4), sessionSalt);
    }

    internal async Task SendAsync(ulong sessionId, ReadOnlyMemory<byte> payload, IPEndPoint ipEndPoint, AesGcm aesGcm)
    {
        if (payload.Length + HeaderLength > MaxPacketSize)
            throw new ArgumentOutOfRangeException(nameof(payload));

        try {
            await _sendSemaphore.WaitAsync().Vhc();
            await SendCoreAsync(sessionId, ipEndPoint, payload, aesGcm);
        }
        catch (Exception ex) {
            if (IsInvalidState(ex))
                Dispose();

            VhLogger.Instance.LogError(
                GeneralEventId.Udp,
                ex,
                "UdpChannelTransmitter: Could not send data. DataLength: {DataLength}, DestinationIp: {DestinationIp}",
                payload.Length,
                VhLogger.Format(ipEndPoint));

            throw;
        }
        finally {
            _sendSemaphore.Release();
        }
    }

    private async Task SendCoreAsync(ulong sessionId, IPEndPoint ipEndPoint, ReadOnlyMemory<byte> payload, AesGcm aesGcm)
    {
        var currentSeq = _sendSequenceNumber++;
        var bufferSpan = _sendBuffer.Span;

        // sessionId[8]
        BinaryPrimitives.WriteUInt64LittleEndian(bufferSpan[..8], sessionId);

        // seq[8]
        BinaryPrimitives.WriteUInt64LittleEndian(bufferSpan.Slice(8, 8), currentSeq);

        // tag[16]
        var tag = bufferSpan.Slice(16, 16);

        // ciphertext
        var ciphertext = bufferSpan.Slice(HeaderLength, payload.Length);

        // AAD = sessionId|seq
        var aad = bufferSpan[..16];

        Span<byte> nonce = stackalloc byte[12];
        BuildNonce(nonce, sessionId, currentSeq);

        aesGcm.Encrypt(nonce, payload.Span, ciphertext, tag, aad);

        var totalLength = HeaderLength + payload.Length;
        var sent = await _udpClient.SendAsync(_sendBuffer[..totalLength], ipEndPoint, _cancellationTokenSource.Token).Vhc();
        if (sent != totalLength)
            throw new Exception($"UdpClient: Sent {sent} bytes instead of {totalLength} bytes.");
    }

    private async Task ReadLoopAsync()
    {
        var nonce = new byte[12];
        var plainTextBuffer = new byte[MaxPacketSize];

        while (!_disposed) {
            try {
                var result = await _udpClient.ReceiveAsync(_cancellationTokenSource.Token).Vhc();
                var data = result.Buffer;
                if (data.Length < HeaderLength) {
                    _udpSignReporter.Raise();
                    continue;
                }

                var span = data.AsSpan();
                var sessionId = BinaryPrimitives.ReadUInt64LittleEndian(span[..8]);
                var seq = BinaryPrimitives.ReadUInt64LittleEndian(span.Slice(8, 8));

                // Find transport
                var transport = SessionIdToUdpTransport(sessionId);
                if (transport is null) {
                    _invalidSessionReporter.Raise();
                    continue;
                }

                // update remote endpoint for server, because client may use different port due to NAT
                if (transport.IsServer)
                    transport.RemoteEndPoint = result.RemoteEndPoint;

                var aesGcm = transport.AesGcm;

                // Extract fields
                var tag = span.Slice(16, 16);
                var ciphertext = span[HeaderLength..];
                var aad = span[..16];
                BuildNonce(nonce, sessionId, seq);

                // Decrypt
                var length = ciphertext.Length;
                if (length > plainTextBuffer.Length)
                    throw new InvalidOperationException(
                        $"Receive buffer too small. length={length}, buffer={plainTextBuffer.Length}");

                // Decrypt in a shared buffer to reduce memory allocation
                var plainTextSpan = plainTextBuffer.AsSpan(0, length);
                aesGcm.Decrypt(nonce, ciphertext, tag, plainTextSpan, aad);

                // Copy to the already allocated result.Buffer to reduce memory copy
                var plainTextMemory = result.Buffer.AsMemory(0, length);
                plainTextSpan.CopyTo(plainTextMemory.Span);
                transport.DataReceived?.Invoke(plainTextMemory);
            }
            catch (Exception) when (_disposed) {
                break;
            }
            catch (Exception ex) when (IsInvalidState(ex)) {
                VhLogger.Instance.LogError(GeneralEventId.Udp, ex, "UdpChannelTransmitter: Read loop crashed.");
                Dispose();
                break;
            }
            catch (Exception) {
                _udpSignReporter.Raise();
            }
        }
    }

    private bool IsInvalidState(Exception ex)
    {
        return _disposed || ex is ObjectDisposedException or SocketException { SocketErrorCode: SocketError.InvalidArgument };
    }

    public virtual void Dispose()
    {
        if (_disposed)
            return;

        _disposed = true;
        _cancellationTokenSource.Cancel();
        _cancellationTokenSource.Dispose();
        _udpClient.Dispose();
        _sendSemaphore.Dispose();
    }
}