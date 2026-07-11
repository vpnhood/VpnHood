namespace VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

public class VpnServiceRevokedException(string? message = null, Exception? innerException = null)
    : Exception(message ?? "VPN connection has been revoked by the system or another VPN app.", innerException);