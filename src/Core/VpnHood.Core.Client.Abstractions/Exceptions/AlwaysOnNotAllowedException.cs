namespace VpnHood.Core.Client.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when the VPN service is asked to connect as always on, and the app does not allow it
/// (<see cref="ClientOptions.AllowAlwaysOn"/>).
/// </summary>
/// <param name="message">The message.</param>
public class AlwaysOnNotAllowedException(string message)
    : UnauthorizedAccessException(message);