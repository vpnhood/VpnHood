using VpnHood.Common.Exceptions;

namespace VpnHood.Client.App.Exceptions;

public class NoFillAdException : LoadAdException
{
    public NoFillAdException(string message) : base(message)
    {
    }

    public NoFillAdException(string message, Exception innerException) : base(message, innerException)
    {
    }
}