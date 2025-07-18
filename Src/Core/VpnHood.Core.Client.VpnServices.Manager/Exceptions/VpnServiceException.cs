namespace VpnHood.Core.Client.VpnServices.Manager.Exceptions;

public class VpnServiceException : Exception
{
    public VpnServiceException(string message) :
        base(message)
    { }
    public VpnServiceException(string message, Exception innerException) :
        base(message, innerException)
    { }
}