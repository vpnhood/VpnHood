namespace VpnHood.Core.Tunneling.Exceptions;

public class PacketDropException : Exception
{
    public PacketDropException(string message) : base(message)
    {
    }
    public PacketDropException(string message, Exception innerException) : base(message, innerException)
    {
    }
}