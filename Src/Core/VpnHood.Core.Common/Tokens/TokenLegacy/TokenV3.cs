using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Converters;

namespace VpnHood.Core.Common.Tokens.TokenLegacy;

[Obsolete("deprecated in version 3.3.451 or upper")]
public class TokenV3
{
    [JsonPropertyName("name")] public string? Name { get; set; }

    [JsonPropertyName("v")] public int Version { get; set; } = 3;

    [JsonPropertyName("sid")] public int SupportId { get; set; }

    [JsonPropertyName("tid")] public required string TokenId { get; set; }

    [JsonPropertyName("sec")] public required byte[] Secret { get; set; }

    [JsonPropertyName("isv")] public bool IsValidHostName { get; set; }

    [JsonPropertyName("hname")] public required string HostName { get; set; }

    [JsonPropertyName("hport")] public int HostPort { get; set; }

    [JsonPropertyName("ch")] public byte[]? CertificateHash { get; set; }

    [JsonPropertyName("pb")] public bool IsPublic { get; set; }

    [JsonPropertyName("url")] public string? Url { get; set; }

    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    [JsonPropertyName("ep")]
    public IPEndPoint[]? HostEndPoints { get; set; }

    public Token ToToken()
    {
        var token = new Token {
            Name = Name,
            IssuedAt = DateTime.MinValue,
            SupportId = SupportId.ToString(),
            TokenId = TokenId,
            Secret = Secret,
            ServerToken = new ServerToken {
                CreatedTime = DateTime.MinValue,
                IsValidHostName = IsValidHostName,
                HostName = HostName,
                HostPort = HostPort,
                CertificateHash = CertificateHash,
                Urls = Url != null ? [Url] : null,
                HostEndPoints = HostEndPoints,
                Secret = null
            }
        };

        return token;
    }

    public string ToAccessKey()
    {
        var json = JsonSerializer.Serialize(this);
        return "vh://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static TokenV3 FromToken(Token token)
    {
        // convert token to token v3
        var tokenV3 = new TokenV3 {
            Name = token.Name,
            SupportId = token.SupportId != null ? int.Parse(token.SupportId) : 0,
            TokenId = token.TokenId,
            Secret = token.Secret,
            IsValidHostName = token.ServerToken.IsValidHostName,
            HostName = token.ServerToken.HostName,
            HostPort = token.ServerToken.HostPort,
            CertificateHash = token.ServerToken.CertificateHash,
            Url = token.ServerToken.Urls?.FirstOrDefault(), // deprecated
            HostEndPoints = token.ServerToken.HostEndPoints,
            Version = 3,
            IsPublic = false
        };

        return tokenV3;
    }
}