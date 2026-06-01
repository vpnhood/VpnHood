namespace VpnHood.Core.Client.Device.Exceptions;

public class VpnServiceUnreachableException : VpnServiceException
{
    public VpnServiceUnreachableException(string message) :
        base(message)
    {
    }

    public VpnServiceUnreachableException(string message, Exception innerException) :
        base(message, innerException)
    {
    }
}
