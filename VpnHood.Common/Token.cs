using System;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using VpnHood.Common.Converters;
using VpnHood.Common.Logging;
using VpnHood.Common.Net;

// ReSharper disable StringLiteralTypo

namespace VpnHood.Common;

public class Token : ICloneable
{
    public Token(byte[] secret, byte[] certificateHash, string hostName)
    {
        if (Util.IsNullOrEmpty(secret)) throw new ArgumentException($"'{nameof(secret)}' cannot be null or empty.", nameof(secret));
        if (Util.IsNullOrEmpty(certificateHash)) throw new ArgumentException($"'{nameof(certificateHash)}' cannot be null or empty.", nameof(certificateHash));
        // after 2.2.276, hostName must exists; //remark for compatibility
        // if (string.IsNullOrEmpty(hostName)) throw new ArgumentException($"'{nameof(hostName)}' cannot be null or empty.", nameof(hostName));

        Secret = secret;
        CertificateHash = certificateHash;
        HostName = hostName;
    }

    [JsonPropertyName("name")] 
    public string? Name { get; set; }

    [JsonPropertyName("v")]
    public int Version { get; set; } = 3;

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

    [JsonPropertyName("ch")] 
    public byte[] CertificateHash { get; set; }

    [JsonPropertyName("pb")] 
    public bool IsPublic { get; set; }

    [JsonPropertyName("url")] 
    public string? Url { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    [JsonPropertyName("ep")]
    public IPEndPoint[]? HostEndPoints { get; set; }

    public object Clone()
    {
        return Util.JsonDeserialize<Token>(JsonSerializer.Serialize(this));
    }

    public string ToAccessKey()
    {
        var json = JsonSerializer.Serialize(this);
        return "vh://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static Token FromAccessKey(string base64)
    {
        base64 = base64.Trim().Trim('\"');
        if (base64.IndexOf("vh://", StringComparison.OrdinalIgnoreCase) == 0)
            base64 = base64[5..];
        var json = Encoding.UTF8.GetString(Convert.FromBase64String(base64));
        var ret = JsonSerializer.Deserialize<Token>(json) ??
                  throw new FormatException("Could not parse accessKey!");
        return ret;
    }

    private async Task<IPEndPoint[]> ResolveHostEndPointsInternalAsync()
    {
        if (IsValidHostName)
        {
            try
            {
                VhLogger.Instance.LogInformation($"Resolving IP from host name: {VhLogger.FormatDns(HostName)}...");
                var hostEntities = await Dns.GetHostEntryAsync(HostName);
                if (!Util.IsNullOrEmpty(hostEntities.AddressList))
                {
                    return hostEntities.AddressList
                        .Select(x => new IPEndPoint(x, HostPort))
                        .ToArray();
                }
            }
            catch (Exception ex)
            {
                VhLogger.Instance.LogError(ex, "Could not resolve IpAddress from hostname!");
            }
        }

        if (!Util.IsNullOrEmpty(HostEndPoints))
            return HostEndPoints;

        throw new Exception($"Could not resolve {nameof(HostEndPoints)} from token!");
    }

    public async Task<IPEndPoint[]> ResolveHostEndPointsAsync()
    {
        var endPoints = await ResolveHostEndPointsInternalAsync();
        if (Util.IsNullOrEmpty(endPoints))
            throw new Exception("Could not resolve any host endpoint from AccessToken!");

        var ipV4EndPoints = endPoints.Where(x => x.AddressFamily == AddressFamily.InterNetwork).ToArray();
        var ipV6EndPoints = endPoints.Where(x => x.AddressFamily == AddressFamily.InterNetworkV6).ToArray();

        if (ipV6EndPoints.Length == 0) return ipV4EndPoints;
        if (ipV4EndPoints.Length == 0) return ipV6EndPoints;
        var publicAddressesIpV6 = await IPAddressUtil.GetPublicIpAddress(AddressFamily.InterNetworkV6);
        return publicAddressesIpV6 != null ? ipV6EndPoints : ipV4EndPoints; //return IPv6 if user has access to IpV6
    }

    public async Task<IPEndPoint> ResolveHostEndPointAsync()
    {
        var endPoints = await ResolveHostEndPointsAsync();
        if (Util.IsNullOrEmpty(endPoints))
            throw new Exception("Could not resolve any host endpoint!");

        var rand = new Random();
        return endPoints[rand.Next(0, endPoints.Length)];
    }
}