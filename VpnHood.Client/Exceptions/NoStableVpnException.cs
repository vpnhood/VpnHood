using System;

namespace VpnHood.Client.Exceptions
{
    public class NoStableVpnException : Exception
    {
        public NoStableVpnException() : base("Vpn is connected but it looks the connection is not stable!") { }
    }
}
