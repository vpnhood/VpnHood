using VpnHood.Core.Common.Exceptions;

namespace VpnHood.AppLib.Exceptions;

public class NoFillAdException : LoadAdException
{
    public NoFillAdException(string message) : base(message)
    {
    }

    public NoFillAdException(string message, Exception innerException) : base(message, innerException)
    {
    }
}