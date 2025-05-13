namespace VpnHood.Core.Tunneling.Exceptions;

public class NetFilterException : Exception
{
    public NetFilterException(string message) : base(message)
    {
    }
    public NetFilterException(string message, Exception innerException) 
        : base(message, innerException)
    {
    }
}