namespace VpnHood.Core.Client.Abstractions.Exceptions;

public class NoStableVpnException()
    : Exception("VPN was connected, but it looked like the connection was not stable.");