namespace VpnHood.Common.Exceptions;

public class ShowAdException : AdException
{
    public ShowAdException(string message) : base(message)
    {
    }

    public ShowAdException(string message, Exception innerException) : base(message, innerException)
    {
    }
}