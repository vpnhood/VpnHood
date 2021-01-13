using System;

namespace VpnHood.Client.Diagnosing
{
    public class NoInternetException : Exception
    {
        public NoInternetException() : base("It looks your device is not connected to the Internet!") { }
    }
}
