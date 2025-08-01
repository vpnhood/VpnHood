using VpnHood.Core.Client.Device.Exceptions;

namespace VpnHood.Core.Client.VpnServices.Manager.Exceptions;

public class VpnServiceUnreachableException : VpnServiceException
{
    public VpnServiceUnreachableException(string message) :
        base(message)
    { }

    public VpnServiceUnreachableException(string message, Exception innerException) :
        base(message, innerException)
    { }

}