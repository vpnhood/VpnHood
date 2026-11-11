namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when a session ends because the device's network has no address of the server's IP
/// version, only of the other one, so the session can no longer reach its server.
/// </summary>
/// <remarks>
/// A new connect finds the server again with an address the network can reach, which a VPN service that can connect
/// by itself makes at once.
/// </remarks>
/// <param name="message">The message that describes the error.</param>
/// <param name="innerException">The exception that is the cause of this exception, or <see langword="null"/>.</param>
// An IPv4-only Wi-Fi after mobile data, say, which gave the session an IPv6 server address
public class ServerIpVersionUnsupportedException(string message, Exception? innerException = null)
    : Exception(message, innerException);
