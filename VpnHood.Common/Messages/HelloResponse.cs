using System;

namespace VpnHood.Messages
{
    public class HelloResponse
    {
        public enum Code
        {
            Ok,
            Expired, //todo: test
            TrafficOverflow, //todo: test
            Error,
        }

        public long MaxTrafficByteCount { get; set; }
        public long SentTrafficByteCount { get; set; }
        public long ReceivedTrafficByteCount { get; set; }
        public DateTime? ExpirationTime { get; set; }
        public Code ResponseCode { get; set; }
        public string ErrorMessage { get; set; }
        public ulong SessionId { get; set; }
        public SuppressType SuppressedTo { get; set; }
    }


}
