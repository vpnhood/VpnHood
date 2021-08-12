using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Common
{
    public class Token : ICloneable
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }
        
        [JsonPropertyName("v")]
        public int Version { get; set; } = 1;
        
        [JsonPropertyName("sid")]
        public int SupportId { get; set; }
        
        [JsonPropertyName("tid")]
        public Guid TokenId { get; set; }

        [JsonPropertyName("sec")]
        public byte[] Secret { get; set; } = null!;
        
        [JsonPropertyName("dns")]
        public string DnsName { get; set; } = null!;

        [JsonPropertyName("isvdns")]
        public bool IsValidDns { get; set; }
        
        [JsonPropertyName("ch")]
        public byte[] CertificateHash { get; set; } = null!;

        [JsonPropertyName("ep")]
        public string[] ServerEndPoints { get; set; } = null!;

        [JsonPropertyName("pb")]
        public bool IsPublic { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonIgnore]
        public IPEndPoint ServerEndPoint { get => IPEndPointConverter.Parse(ServerEndPoints.FirstOrDefault()); set => ServerEndPoints = new string[] { value.ToString() }; }

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
            var ret = JsonSerializer.Deserialize<Token>(json) ?? throw new FormatException("Could not parse accessKey!");
            return ret;
        }

        public object Clone()
        {
            var ret = JsonSerializer.Deserialize<Token>(JsonSerializer.Serialize(this)) ?? throw new Exception($"Couldn't clone nameof {nameof(Token)}");
            return ret;
        }
    }
}
