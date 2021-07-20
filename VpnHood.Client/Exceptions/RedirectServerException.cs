using System;
using System.Net;

namespace VpnHood.Client.Exceptions
{
    public class RedirectServerException : Exception
    {
        public IPEndPoint RedirectServerEndPoint { get; }
        public RedirectServerException(IPEndPoint redirectServerEndPoint, string message) 
            : base(message)
        {
            RedirectServerEndPoint = redirectServerEndPoint;
        }
    }
}
