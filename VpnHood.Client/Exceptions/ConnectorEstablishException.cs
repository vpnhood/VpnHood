namespace VpnHood.Client.Exceptions;

internal class ConnectorEstablishException(string message, Exception innerException)
    : Exception(message, innerException);