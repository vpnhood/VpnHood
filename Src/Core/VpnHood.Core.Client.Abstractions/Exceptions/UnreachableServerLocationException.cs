namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableServerLocationException : UnreachableServerException
{
    public UnreachableServerLocationException(string message)
        : base(message)
    {
    }
    public UnreachableServerLocationException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}

