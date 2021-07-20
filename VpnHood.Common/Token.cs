using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace VpnHood.Common
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
        
        [JsonPropertyName("isvdns")]
        public bool IsValidDns { get; set; }
        
        [JsonPropertyName("ch")]
        public byte[] CertificateHash { get; set; }

        [JsonPropertyName("ep")]
        public string[] ServerEndPoints { get; set; }

        [JsonPropertyName("pb")]
        public bool IsPublic { get; set; }

        [JsonPropertyName("url")]
        public string Url { get; set; }

        [JsonIgnore]
        public IPEndPoint ServerEndPoint { get => Util.ParseIpEndPoint(ServerEndPoints.FirstOrDefault()); set => ServerEndPoints = new string[] { value.ToString() }; }

        public static byte[] ComputePublicKeyHash(byte[] publicKey)
        {
            using var hashAlg = MD5.Create();
            return hashAlg.ComputeHash(publicKey);
        }

        public string ToAccessKey()
        {
            var json = JsonSerializer.Serialize(this);
            return "vh://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
        }

        public static Token FromAccessKey(string base64)
        {
            base64 = base64.Trim();
            if (base64.IndexOf("vh://", StringComparison.OrdinalIgnoreCase) == 0)
                base64 = base64[5..];
            var json = Encoding.UTF8.GetString( Convert.FromBase64String(base64));
            return JsonSerializer.Deserialize<Token>(json);
        }

        public object Clone()
        {
            return JsonSerializer.Deserialize<Token>(JsonSerializer.Serialize(this));
        }
    }
}
