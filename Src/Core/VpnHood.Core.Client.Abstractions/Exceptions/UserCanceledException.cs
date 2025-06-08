namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UserCanceledException : OperationCanceledException
{
    public UserCanceledException(string message) : base(message)
    {

    }

    public UserCanceledException(string message, Exception innerException) : base(message, innerException)
    {
    }
}