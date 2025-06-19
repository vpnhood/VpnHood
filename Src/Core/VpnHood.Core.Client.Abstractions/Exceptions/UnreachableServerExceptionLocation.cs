namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableServerExceptionLocation : UnreachableServerException
{
    public UnreachableServerExceptionLocation(string message)
        : base(message)
    {
    }
    public UnreachableServerExceptionLocation(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

