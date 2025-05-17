namespace VpnHood.Core.Tunneling.Exceptions;

public class DropPacketException : Exception
{
    public DropPacketException(string message) : base(message)
    {
    }
    public DropPacketException(string message, Exception innerException) : base(message, innerException)
    {
    }
}