namespace VpnHood.AppLib.Api.Exceptions;

public class NoStableVpnException()
    : Exception("VPN was connected, but it looked like the connection was not stable.");