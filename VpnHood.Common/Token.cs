using Microsoft.Extensions.Logging;
using System;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using VpnHood.Common.Converters;
using VpnHood.Logging;

namespace VpnHood.Common
{
    public class Token : ICloneable
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("v")]
        public int Version { get; set; } = 2;

        [JsonPropertyName("sid")]
        public int SupportId { get; set; }

        [JsonPropertyName("tid")]
        public Guid TokenId { get; set; }

        [JsonPropertyName("sec")]
        public byte[] Secret { get; set; }

        [JsonPropertyName("isv")]
        public bool IsValidHostName { get; set; }
        [JsonPropertyName("hname")]
        public string HostName { get; set; }

        [JsonPropertyName("hport")]
        public int HostPort { get; set; }

        [JsonPropertyName("hep")]
        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint? HostEndPoint { get; set; }

        [JsonPropertyName("ch")]
        public byte[] CertificateHash { get; set; }

        [JsonPropertyName("pb")]
        public bool IsPublic { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("dns")]
        [Obsolete("Deprecated from version 1.4.258")]
        public string? DeprecatedDns
        {
            set { if (!string.IsNullOrEmpty(value)) HostName = DeprecatedDns!;}
            get => null;
        }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("ep")]
        [Obsolete("Deprecated from version 1.4.258")]
        public string[]? HostEndPoints
        {
            set
            {
                if (!Util.IsNullOrEmpty(value))
                {
                    HostEndPoint = IPEndPointConverter.Parse(value[0]);
                    HostPort = HostEndPoint.Port;
                }
            }
            get => null;
        }

        public Token(byte[] secret, byte[] certificateHash, string hostName)
        {
            if (Util.IsNullOrEmpty(secret)) throw new ArgumentException($"'{nameof(secret)}' cannot be null or empty.", nameof(secret));
            if (Util.IsNullOrEmpty(certificateHash)) throw new ArgumentException($"'{nameof(certificateHash)}' cannot be null or empty.", nameof(certificateHash));
            if (string.IsNullOrEmpty(hostName)) throw new ArgumentException($"'{nameof(hostName)}' cannot be null or empty.", nameof(hostName));

            Secret = secret;
            CertificateHash = certificateHash;
            HostName = hostName;
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
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var ret = JsonSerializer.Deserialize<Token>(json) ?? throw new FormatException("Could not parse accessKey!");
            return ret;
        }

        public object Clone()
        {
            var ret = JsonSerializer.Deserialize<Token>(JsonSerializer.Serialize(this)) ?? throw new Exception($"Couldn't clone nameof {nameof(Token)}");
            return ret;
        }

        public async Task<IPEndPoint> ResolveHostPointAsync()
        {
            var random = new Random();
            if (IsValidHostName)
            {
                try
                {
                    VhLogger.Instance.LogInformation($"Resolving IP from host name: {VhLogger.FormatDns(HostName)}...");
                    var hostEntry = await Dns.GetHostEntryAsync(HostName);
                    if (hostEntry.AddressList.Length == 0)
                        throw new Exception("Could not resolve Server Address!");

                    var index = random.Next(0, hostEntry.AddressList.Length);
                    var ip = hostEntry.AddressList[index];
                    IPEndPoint ret = new(ip, HostPort);
                    VhLogger.Instance.LogInformation($"{hostEntry.AddressList.Length} IP founds. {ret} has been Selected!");
                    return ret;
                }
                catch (Exception ex)
                {
                    VhLogger.Instance.LogError(ex, "Could not resolve IpAddress from hostname!");
                }
            }

            if (HostEndPoint != null)
                return HostEndPoint;

            throw new Exception($"Could not resolve {nameof(HostEndPoint)} from token!");
        }

    }
}
