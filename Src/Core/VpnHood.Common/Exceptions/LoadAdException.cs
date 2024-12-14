namespace VpnHood.Common.Exceptions;

public class LoadAdException : AdException
{
    public LoadAdException(string message) : base(message)
    {
    }

    public LoadAdException(string message, Exception innerException) : base(message, innerException)
    {
    }
}