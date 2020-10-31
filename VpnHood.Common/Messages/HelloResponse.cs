using System;
using VpnHood.Client;

namespace VpnHood.Messages
{
    public class HelloResponse
    {
        public AccessUsage AccessUsage{get; set;}
        public ResponseCode ResponseCode { get; set; }
        public string ErrorMessage { get; set; }
        public ulong SessionId { get; set; }
        public SuppressType SuppressedTo { get; set; }
    }


}
