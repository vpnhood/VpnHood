using VpnHood.Core.Common.Messaging;

namespace VpnHood.Core.Common.Exceptions;

/// <summary>
/// The exception that is thrown when the server is under maintenance: a <see cref="SessionException"/> of
/// <see cref="SessionErrorCode.Maintenance"/>.
/// </summary>
/// <param name="message">The message, or <see langword="null"/> for a standard one.</param>
public class MaintenanceException(string? message = null)
    : SessionException(SessionErrorCode.Maintenance, message ?? "The server is in maintenance mode! Please try again later.");
