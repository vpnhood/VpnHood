namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UserCanceledException(string message)
    : OperationCanceledException(message);