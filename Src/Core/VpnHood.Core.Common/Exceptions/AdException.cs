namespace VpnHood.Core.Common.Exceptions;

public class AdException : Exception
{
    public AdException(string message, Exception innerException) : base(message, innerException)
    {
    }

    public AdException(string message) : base(message)
    {
    }
}