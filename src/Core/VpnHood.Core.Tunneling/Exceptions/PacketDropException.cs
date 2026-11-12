namespace VpnHood.Core.Tunneling.Exceptions;

/// <summary>Represents the error of a packet the tunnel drops, as one it cannot carry.</summary>
/// <param name="message">The error's message.</param>
/// <param name="innerException">The error that caused it, or <see langword="null"/>.</param>
public class PacketDropException(string message, Exception? innerException = null)
    : Exception(message, innerException);
