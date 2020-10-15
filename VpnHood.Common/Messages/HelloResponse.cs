using System;

namespace VpnHood.Messages
{
    public class HelloResponse
    {
        public enum Code
        {
            Ok,
            Expired,
            Error,
        }

        public Code ResponseCode { get; set; }
        public string ErrorMessage { get; set; }
        public ulong SessionId { get; set; }
        public SuppressType SuppressedTo { get; set; }
    }


}
