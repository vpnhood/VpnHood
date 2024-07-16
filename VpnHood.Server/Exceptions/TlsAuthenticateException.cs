namespace VpnHood.Server.Exceptions;

internal class TlsAuthenticateException(string message, Exception innerException)
    : Exception(message, innerException);