using System;

namespace VpnHood.Tunneling.Messages
{
    public class HelloRequest 
    {
        public string ClientVersion { get; set; }
        public int ClientProtocolVersion { get; set; }
        public Guid TokenId { get; set; }
        public Guid ClientId { get; set; }
        public string UserToken { get; set; }
        public byte[] EncryptedClientId { get; set; }
        public bool UseUdpChannel { get; set; }
        public string UserAgent { get; set; }
    }
}
