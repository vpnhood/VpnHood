using System.Net;

namespace VpnHood.Core.Tunneling.Channels;

/// <summary>
/// Represents what takes each datagram a UDP socket receives: the server's listener host, which hands it to its
/// session, or a client's UDP channel, which takes its own.
/// </summary>
public interface IUdpDatagramHandler
{
    /// <summary>Takes a datagram the socket has received.</summary>
    /// <param name="transmitter">The transmitter whose socket received it.</param>
    /// <param name="datagram">The datagram, at least a header long, which is the handler's to open in place.</param>
    /// <param name="remoteEndPoint">The endpoint it came from.</param>
    void OnDatagramReceived(UdpChannelTransmitter transmitter, Memory<byte> datagram, IPEndPoint remoteEndPoint);
}
