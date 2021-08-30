using System;
using System.Net;

namespace VpnHood.Client.Exceptions
{
    public class RedirectHostException : Exception
    {
        public RedirectHostException(IPEndPoint redirectHostEndPoint, string? message)
            : base(message)
        {
            RedirectHostEndPoint =
                redirectHostEndPoint ?? throw new ArgumentNullException(nameof(redirectHostEndPoint));
        }

        public IPEndPoint RedirectHostEndPoint { get; }
    }
}