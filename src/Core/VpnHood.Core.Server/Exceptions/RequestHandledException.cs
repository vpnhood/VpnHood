namespace VpnHood.Core.Server.Exceptions;

/// <summary>
/// Indicates that the request has been fully handled and no further processing is needed.
/// </summary>
public class RequestHandledException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "The request has been handled.", innerException);
