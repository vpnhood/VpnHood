namespace VpnHood.Core.Common.Exceptions;

public class NoWorkingEndPointException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "No working tcp end point is available after configuration!", innerException);
