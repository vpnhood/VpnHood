namespace VpnHood.AppLib.Abstractions.AdExceptions;

public class NoFillAdException : LoadAdException
{
    public NoFillAdException(string message) : base(message)
    {
    }

    public NoFillAdException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
