namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableServerException : Exception
{
    public UnreachableServerException(string message)
        : base(message)
    {
    }

    public UnreachableServerException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
