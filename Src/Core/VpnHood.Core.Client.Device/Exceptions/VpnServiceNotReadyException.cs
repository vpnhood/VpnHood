namespace VpnHood.Core.Client.Device.Exceptions;

public class VpnServiceNotReadyException : VpnServiceException
{
    public VpnServiceNotReadyException(string message) :
        base(message)
    {
    }

    public VpnServiceNotReadyException(string message, Exception innerException) :
        base(message, innerException)
    {
    }
}
