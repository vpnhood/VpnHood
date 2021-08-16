using Microsoft.Extensions.Logging;
using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
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

        [JsonPropertyName("dns")]
        public string ServerAuthority { get; set; }

        [JsonPropertyName("isvdns")]
        public bool IsValidServerAuthority { get; set; }

        [JsonPropertyName("ch")]
        public byte[] CertificateHash { get; set; }

        [JsonPropertyName("sep")]
        [JsonConverter(typeof(IPEndPointConverter))]
        public IPEndPoint? ServerEndPoint { get; set; }

        [JsonPropertyName("pb")]
        public bool IsPublic { get; set; }

        [JsonPropertyName("url")]
        public string? Url { get; set; }

        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        [JsonPropertyName("ep")]
        [Obsolete("Deprecated from version 1.4.258")]
        public string[]? ServerEndPoints
        {
            set
            { 
                if (!Util.IsNullOrEmpty(value))
                    ServerEndPoint = IPEndPointConverter.Parse(value[0]);
            }
            get => null;
        }

        [JsonIgnore]
        public string ServerAuthorityHostName
        {
            get
            {
                var url = ServerAuthority;
                if (!url.Contains(Uri.SchemeDelimiter))
                    url = string.Concat(Uri.UriSchemeHttps, Uri.SchemeDelimiter, url);
                Uri uri = new(url);
                return uri.Host;
            }
        }

        public Token(byte[] secret, byte[] certificateHash, string serverAuthority)
        {
            if (Util.IsNullOrEmpty(secret)) throw new ArgumentException($"'{nameof(secret)}' cannot be null or empty.", nameof(secret));
            if (Util.IsNullOrEmpty(certificateHash)) throw new ArgumentException($"'{nameof(certificateHash)}' cannot be null or empty.", nameof(certificateHash));
            if (string.IsNullOrEmpty(serverAuthority)) throw new ArgumentException($"'{nameof(serverAuthority)}' cannot be null or empty.", nameof(serverAuthority));
            if (serverAuthority.Contains(Uri.SchemeDelimiter)) throw new FormatException($"{nameof(serverAuthority)} should not have SchemeDelimiter!");

            Secret = secret;
            CertificateHash = certificateHash;
            ServerAuthority = serverAuthority;
        }

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
            var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
            var ret = JsonSerializer.Deserialize<Token>(json) ?? throw new FormatException("Could not parse accessKey!");
            return ret;
        }

        public object Clone()
        {
            var ret = JsonSerializer.Deserialize<Token>(JsonSerializer.Serialize(this)) ?? throw new Exception($"Couldn't clone nameof {nameof(Token)}");
            return ret;
        }

        public async Task<IPEndPoint> ResolveServerEndPointAsync()
        {
            var random = new Random();
            if (IsValidServerAuthority)
            {
                try
                {
                    string url = ServerAuthority;
                    if (!url.Contains(Uri.SchemeDelimiter))
                        url = string.Concat(Uri.UriSchemeHttps, Uri.SchemeDelimiter, url);
                    Uri uri = new(url);

                    VhLogger.Instance.LogInformation($"Resolving IP from host name: {VhLogger.FormatDns(uri.Host)}...");
                    var hostEntry = await Dns.GetHostEntryAsync(uri.Host);
                    if (hostEntry.AddressList.Length == 0)
                        throw new Exception("Could not resolve Server Address!");

                    var index = random.Next(0, hostEntry.AddressList.Length);
                    var ip = hostEntry.AddressList[index];
                    IPEndPoint ret = new(ip, uri.Port);
                    VhLogger.Instance.LogInformation($"{hostEntry.AddressList.Length} IP founds. {ret} has been Selected!");
                    return ret;
                }
                catch (Exception ex)
                {
                    VhLogger.Instance.LogError(ex, $"Could not resolve IpAddress from hostname!");
                }
            }

            if (ServerEndPoint != null)
                return ServerEndPoint;

            throw new Exception($"Could not resolve {nameof(ServerEndPoint)} from token!");
        }

    }
}
