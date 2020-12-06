using System;

namespace VpnHood.Tunneling.Messages
{
    public class HelloRequest 
    {
        public Guid TokenId { get; set; }
        public Guid ClientId { get; set; }
        public string UserToken { get; set; }
        public byte[] EncryptedClientId { get; set; }
    }
}
