namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class AutoStartNotSupportedException : OperationCanceledException 
{
    public AutoStartNotSupportedException(string message) : base(message)
    {

    }

    public AutoStartNotSupportedException(string message, Exception innerException) : base(message, innerException)
    {
    }
}