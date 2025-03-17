namespace VpnHood.Core.Client.VpnServices.Manager.Exceptions;

public class VpnServiceUnreachableException : Exception
{
    public VpnServiceUnreachableException(string message) :
        base(message)
    { }

    public VpnServiceUnreachableException(string message, Exception innerException) :
        base(message, innerException)
    { }

}