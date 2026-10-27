using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Utils;

// ReSharper disable StringLiteralTypo

namespace VpnHood.Core.Common.Tokens;

public class Token
{
    [JsonPropertyName("v")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int Version => 4;

    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string? Name { get; set; }

    [JsonPropertyName("sid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string? SupportId { get; set; }

    [JsonPropertyName("tid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string TokenId { get; set; }

    [JsonPropertyName("iat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTime IssuedAt { get; set; } =
        DateTime.MinValue; // for backward compatibility. Default value it is not required

    [JsonPropertyName("sec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required byte[] Secret { get; set; }

    [JsonPropertyName("ser")]
    public required ServerToken ServerToken { get; set; }

    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    [JsonPropertyName("ispub")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPublic { get; set; }


    [JsonPropertyName("cpols")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ClientPolicy[]? ClientPolicies { get; set; }

    public string ToAccessKey()
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        });
        // "vh:" with no "//", the URI form that carries data rather than a host (RFC 3986), as mailto: does: as a
        // host the key is too long for .NET's Uri, which iOS's Avalonia parses an opened link with. Apps since 2023
        // read it, and still read the vh:// keys given out before.
        return "vh:" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static Token FromAccessKey(string base64)
    {
        // remove token prefix
        base64 = base64.Trim().Trim('\"');
        foreach (var prefix in new[] { "vh://", "vhkey://", "vh:", "vhkey:" })
            if (base64.StartsWith(prefix, StringComparison.Ordinal))
                base64 = base64[prefix.Length..];

        // Windows' protocol activation takes a vh:// key's base64 for the link's host and adds the root path, a '/',
        // which no key's base64 ends in: the JSON it carries ends in '}'
        base64 = base64.TrimEnd('/');

        // load
        var json = Encoding.UTF8.GetString(VhUtils.ConvertFromBase64AndFixPadding(base64));
        var tokenVersion = JsonUtils.Deserialize<TokenVersion>(json);

        return tokenVersion.Version switch {
            4 => JsonUtils.Deserialize<Token>(json),
            _ => throw new NotSupportedException($"Token version {tokenVersion.Version} is not supported!")
        };
    }
}