namespace VpnHood.Client.Exceptions;

internal class ConnectorEstablishException : Exception
{
    public ConnectorEstablishException(string message, Exception innerException) : base(message, innerException)
    {
    }
}