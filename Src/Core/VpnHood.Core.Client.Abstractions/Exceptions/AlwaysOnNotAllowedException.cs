namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class AlwaysOnNotAllowedException(string message) 
    : UnauthorizedAccessException(message);