namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UserCanceledException(string message, Exception? innerException = null)
    : OperationCanceledException(message, innerException);
