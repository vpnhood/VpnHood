namespace VpnHood.Core.Tunneling.Exceptions;

public class NatEndPointNotFoundException : Exception
{
    public NatEndPointNotFoundException()
        : base("The specified endpoint could not be found in NAT.")
    {
    }

    public NatEndPointNotFoundException(string message)
        : base(message)
    {
    }

    public NatEndPointNotFoundException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}