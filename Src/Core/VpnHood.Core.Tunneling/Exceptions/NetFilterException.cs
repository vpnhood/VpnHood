namespace VpnHood.Core.Tunneling.Exceptions;

public class NetFilterException(string? message = null, Exception? innerException = null) 
    : Exception(message, innerException);