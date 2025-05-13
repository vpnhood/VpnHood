namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableServerExceptionLocation(string? serverLocation = null)
    : UnreachableServerException(serverLocation);