namespace VpnHood.Core.Tunneling.Exceptions;

public class PacketDropException(string message, Exception? innerException = null)
    : Exception(message, innerException);
