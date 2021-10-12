using System;

namespace VpnHood.Client.Exceptions
{
    public class NoStableVpnException : Exception
    {
        public NoStableVpnException() 
            : base("Vpn was connected but it looked the connection was not stable!")
        {
        }
    }
}