namespace VpnHood.AppLib.Exceptions;

public class NoStableVpnException()
    : Exception("VPN was connected, but it looked like the connection was not stable.");