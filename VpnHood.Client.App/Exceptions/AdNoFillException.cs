using VpnHood.Common.Exceptions;

namespace VpnHood.Client.App.Exceptions;

public class AdNoFillException : ShowAdException
{
    public AdNoFillException(string message) : base(message)
    {
    }

    public AdNoFillException(string message, Exception innerException) : base(message, innerException)
    {
    }
}