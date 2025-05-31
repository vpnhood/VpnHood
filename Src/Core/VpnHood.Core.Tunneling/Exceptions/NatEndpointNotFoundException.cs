namespace VpnHood.Core.Tunneling.Exceptions;

public class NatEndpointNotFoundException : Exception
{
    public NatEndpointNotFoundException()
        : base("The specified endpoint could not be found in NAT.")
    {
    }

    public NatEndpointNotFoundException(string message)
        : base(message)
    {
    }

    public NatEndpointNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}