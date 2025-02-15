namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class UnreachableServerLocation(string? serverLocation = null)
    : UnreachableServer(serverLocation);