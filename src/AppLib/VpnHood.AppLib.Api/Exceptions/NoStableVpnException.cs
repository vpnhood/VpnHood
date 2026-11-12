namespace VpnHood.AppLib.Api.Exceptions;

/// <summary>Represents the error of a VPN that connected but was not stable.</summary>
public class NoStableVpnException()
    : Exception("VPN was connected, but it looked like the connection was not stable.");