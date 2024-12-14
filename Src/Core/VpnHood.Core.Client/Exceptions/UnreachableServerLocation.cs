namespace VpnHood.Core.Client.Exceptions;
public class UnreachableServerLocation(string? serverLocation = null) 
    : UnreachableServer(serverLocation);