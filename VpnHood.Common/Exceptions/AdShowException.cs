namespace VpnHood.Common.Exceptions;

public class AdShowException : AdException
{
    public AdShowException(string message) : base(message)
    {
    }

    public AdShowException(string message, Exception innerException) : base(message, innerException)
    {
    }
}