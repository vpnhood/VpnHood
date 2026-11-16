namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

/// <summary>
/// The exception that is thrown when the system or another VPN app takes the VPN from the service.
/// </summary>
/// <param name="message">The message, or <see langword="null"/> for the default one.</param>
/// <param name="innerException">The exception that caused this one, or <see langword="null"/>.</param>
public class VpnServiceRevokedException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "VPN connection has been revoked by the system or another VPN app.", innerException);