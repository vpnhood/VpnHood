namespace VpnHood.Client.Exceptions;
public class UnreachableServerLocation(string? serverLocation = null) 
    : UnreachableServer(serverLocation);