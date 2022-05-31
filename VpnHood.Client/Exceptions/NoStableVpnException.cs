using System;

namespace VpnHood.Client.Exceptions;

public class NoStableVpnException : Exception
{
    public NoStableVpnException() 
        : base("VPN was connected, but it looked like the connection was not stable!")
    {
    }
}