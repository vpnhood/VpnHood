using System;

namespace VpnHood.Messages
{
    public class HelloRequest 
    {
        public Guid TokenId { get; set; }
        public Guid ClientId { get; set; }
        public byte[] EncryptedClientId { get; set; }
    }
}
