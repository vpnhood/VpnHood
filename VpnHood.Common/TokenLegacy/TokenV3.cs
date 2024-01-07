using System.Net;
using System.Text.Json.Serialization;
using VpnHood.Common.Converters;

namespace VpnHood.Common.TokenLegacy;

// deprecated in version 3.3.450 or upper
internal class TokenV3
{
    [JsonPropertyName("name")]
    public string? Name { get; set; }

    [JsonPropertyName("v")]
    public int Version { get; set; } = 3;

    [JsonPropertyName("sid")]
    public int SupportId { get; set; }

    [JsonPropertyName("tid")]
    public required string TokenId { get; set; }

    [JsonPropertyName("sec")]
    public required byte[] Secret { get; set; }

    [JsonPropertyName("isv")]
    public bool IsValidHostName { get; set; }

    [JsonPropertyName("hname")]
    public required string HostName { get; set; }

    [JsonPropertyName("hport")]
    public int HostPort { get; set; }

    [JsonPropertyName("ch")]
    public byte[]? CertificateHash { get; set; }

    [JsonPropertyName("pb")]
    public bool IsPublic { get; set; }

    [JsonPropertyName("url")]
    public string? Url { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    [JsonPropertyName("ep")]
    public IPEndPoint[]? HostEndPoints { get; set; }

    public Token ToToken()
    {
        var token = new Token
        {
            Name = Name,
            SupportId = SupportId,
            TokenId = TokenId,
            Secret = Secret,
            IsNewVersion = true,
            ServerToken = new ServerToken()
            {
                CreatedTime = DateTime.MinValue,
                IsValidHostName = IsValidHostName,
                HostName = HostName,
                HostPort = HostPort,
                CertificateHash = CertificateHash,
                Url = Url,
                HostEndPoints = HostEndPoints,
                Secret = null
            }
        };

        return token;
    }
}