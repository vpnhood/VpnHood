using System;
using System.Net;
using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood
{
    public class Token : ICloneable
    {
        [JsonPropertyName("name")]
        public string Name { get; set; }
        
        [JsonPropertyName("v")]
        public int Version { get; set; } = 1;
        
        [JsonPropertyName("sid")]
        public int SupportId { get; set; }
        
        [JsonPropertyName("tid")]
        public Guid TokenId { get; set; }
        
        [JsonPropertyName("sec")]
        public byte[] Secret { get; set; }
        
        [JsonPropertyName("dns")]
        public string DnsName { get; set; }
        
        [JsonPropertyName("pkh")]
        public byte[] PublicKeyHash { get; set; }

        [JsonPropertyName("ep")]
        public string ServerEndPoint { get; set; }

        public static byte[] ComputePublicKeyHash(byte[] publicKey)
        {
            using var hashAlg = MD5.Create();
            return hashAlg.ComputeHash(publicKey);
        }

        public string ToAccessKey()
        {
            var json = JsonSerializer.Serialize(this);
            return Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        public static Token FromAccessKey(string base64)
        {
            var json = Encoding.UTF8.GetString( Convert.FromBase64String(base64));
            return JsonSerializer.Deserialize<Token>(json);
        }

        public object Clone()
        {
            return JsonSerializer.Deserialize<Token>(JsonSerializer.Serialize(this));
        }
    }
}
