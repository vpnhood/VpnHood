namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableServerException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "There is no reachable server at this moment. Please try again later.", innerException);