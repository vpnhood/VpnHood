using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using Microsoft.Extensions.Logging;
using VpnHood.Net.Toolkit.Extensions;
using VpnHood.Net.Toolkit.Logging;
using VpnHood.Net.Toolkit.Net;
using VpnHood.Net.Toolkit.Utils;
using VpnHood.Core.Tunneling.Utils;

namespace VpnHood.Core.Tunneling.Channels;

/// <summary>
/// Represents a UDP socket for sessions' datagrams: it hands each one it receives to its handler, and sends the sealed
/// ones it is given.
/// </summary>
/// <remarks>
/// A server's listener serves every session; a client's socket serves its own. A datagram shorter than a header never
/// reaches the handler, and a socket that fails beyond use disposes the transmitter.
/// </remarks>
public sealed class UdpChannelTransmitter : IDisposable
{
    private readonly EventReporter _udpSignReporter = new("Invalid udp signature.", GeneralEventId.UdpSign);
    private readonly EventReporter _receiveErrorReporter = new("Could not process a UDP datagram.", GeneralEventId.Udp);
    private readonly EventReporter _sendErrorReporter = new("Could not send a UDP datagram.", GeneralEventId.Udp);
    private readonly UdpClient _udpClient;
    private readonly IUdpDatagramHandler _handler;
    // volatile: the catch filters tell our own teardown from a dead socket by this flag, and the sessions' send loops
    // read Connected; all run on threads other than the one calling Dispose
    private volatile bool _disposed;
    private bool _isSendBufferSizeCustomized;
    private bool _isReceivedBufferSizeCustomized;

    /// <summary>Gets the socket's local endpoint.</summary>
    public IPEndPoint LocalEndPoint { get; }

    /// <summary>Gets a value indicating whether the transmitter is in use: it is not disposed.</summary>
    public bool Connected => !_disposed;

    /// <summary>Initializes a new instance of the <see cref="UdpChannelTransmitter"/> class, which reads at once.</summary>
    /// <param name="udpClient">The bound UDP client, which the transmitter owns.</param>
    /// <param name="handler">The handler that takes each datagram received.</param>
    /// <exception cref="InvalidOperationException">The UDP client's socket is not bound.</exception>
    public UdpChannelTransmitter(UdpClient udpClient, IUdpDatagramHandler handler)
    {
        _udpClient = udpClient;
        _handler = handler;
        LocalEndPoint = udpClient.Client.GetLocalEndPoint();
        Task.Run(ReadLoopAsync);
    }

    /// <summary>
    /// Gets or sets the socket's send and receive buffer sizes. A size above zero is set; any other gives back the
    /// system's default where a size was set before.
    /// </summary>
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

    /// <summary>Sends a sealed datagram.</summary>
    /// <param name="datagram">The datagram.</param>
    /// <param name="remoteEndPoint">The endpoint it goes to.</param>
    /// <returns>A task that completes once the socket has sent the datagram.</returns>
    /// <remarks>
    /// Sessions send at once: each seals in a buffer of its own, and the socket takes concurrent sends. A failure is
    /// thrown. While the transmitter is in use, an ordinary one is logged, the first of each report period as a warning
    /// with its error and the others at the debug level; a socket in a state beyond use is logged as an error and
    /// disposes the transmitter.
    /// </remarks>
    /// <exception cref="Exception">The socket sent less than the whole datagram.</exception>
    public async Task SendAsync(ReadOnlyMemory<byte> datagram, IPEndPoint remoteEndPoint)
    {
        try {
            var sent = await _udpClient.SendAsync(datagram, remoteEndPoint).Vhc();
            if (sent != datagram.Length)
                throw new Exception($"UdpClient: Sent {sent} bytes instead of {datagram.Length} bytes.");
        }
        catch (Exception ex) when (SocketUtils.IsInvalidUdpStateException(ex)) {
            VhLogger.Instance.LogError(GeneralEventId.Essential, ex,
                "UdpChannelTransmitter: Socket is in invalid state. Disposing the transmitter. " +
                "DataLength: {DataLength}, DestinationIp: {DestinationIp}",
                datagram.Length, VhLogger.Format(remoteEndPoint));

            Dispose();
            throw;
        }
        catch (Exception ex) when (!_disposed) {
            // the first of each report period with its exception, the others only counted: a flood must not flood
            // the log
            if (_sendErrorReporter.Raise())
                VhLogger.Instance.LogWarning(GeneralEventId.Udp, ex,
                    "UdpChannelTransmitter: Could not send a datagram. DestinationIp: {DestinationIp}",
                    VhLogger.Format(remoteEndPoint));
            else
                VhLogger.Instance.LogDebug(GeneralEventId.Udp, ex,
                    "UdpChannelTransmitter: Could not send a datagram. DestinationIp: {DestinationIp}",
                    VhLogger.Format(remoteEndPoint));

            throw;
        }
    }

    private async Task ReadLoopAsync()
    {
        while (!_disposed) {
            try {
                var result = await _udpClient.ReceiveAsync().Vhc();
                if (result.Buffer.Length < UdpDatagram.HeaderLength) {
                    _udpSignReporter.Raise();
                    continue;
                }

                // the received array is the datagram's own: it is opened in place, and its packets keep it
                _handler.OnDatagramReceived(this, result.Buffer, result.RemoteEndPoint);
            }
            catch (Exception) when (_disposed) {
                break;
            }
            catch (Exception ex) when (SocketUtils.IsInvalidUdpStateException(ex)) {
                VhLogger.Instance.LogError(GeneralEventId.Essential, ex, "UdpChannelTransmitter: Read loop crashed.");
                Dispose();
                break;
            }
            catch (AuthenticationTagMismatchException) {
                _udpSignReporter.Raise();
            }
            catch (Exception ex) {
                // the first of each report period with its exception, the others only counted: a flood must not flood
                // the log
                if (_receiveErrorReporter.Raise())
                    VhLogger.Instance.LogWarning(GeneralEventId.Udp, ex, "UdpChannelTransmitter: Could not process a datagram.");
                else
                    VhLogger.Instance.LogDebug(GeneralEventId.Udp, ex, "UdpChannelTransmitter: Could not process a datagram.");
            }
        }
    }

    /// <summary>Closes the socket, which ends the reading; a socket that fails to close is logged.</summary>
    public void Dispose()
    {
        // before the socket's close: the receive loop takes a failure after it as the close's own
        if (Interlocked.Exchange(ref _disposed, true))
            return;

        VhLogger.Instance.LogInformation(GeneralEventId.Essential,
            "UdpChannelTransmitter: Disposing the transmitter. LocalEndPoint: {LocalEndPoint}",
            VhLogger.Format(LocalEndPoint));

        // a socket that fails to close is logged, so its owner's close goes on
        VhInvoker.TryInvoke("close the UDP socket", LogLevel.Warning, _udpClient.Dispose);
        _udpSignReporter.Dispose();
        _receiveErrorReporter.Dispose();
        _sendErrorReporter.Dispose();
    }
}
