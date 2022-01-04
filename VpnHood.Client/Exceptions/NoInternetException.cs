using System;

namespace VpnHood.Client.Exceptions
{
    public class NoInternetException : Exception
    {
        public NoInternetException() 
            : base("It looks like your device is not connected to the Internet or the connection is too slow!")
        {
        }
    }
}