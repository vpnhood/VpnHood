using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Core.Toolkit.Utils;

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

    [JsonPropertyName("ser")] public required ServerToken ServerToken { get; set; }

    [JsonPropertyName("tags")] public string[] Tags { get; set; } = [];

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
        return "vh://" + Convert.ToBase64String(Encoding.UTF8.GetBytes(json));
    }

    public static Token FromAccessKey(string base64)
    {
        // remove token prefix
        base64 = base64.Trim().Trim('\"');
        foreach (var prefix in new[] { "vh://", "vhkey://", "vh:", "vhkey:" })
            if (base64.StartsWith(prefix))
                base64 = base64[prefix.Length..];

        // load
        var json = Encoding.UTF8.GetString(VhUtils.ConvertFromBase64AndFixPadding(base64));
        var tokenVersion = JsonUtils.Deserialize<TokenVersion>(json);

        return tokenVersion.Version switch {
            4 => JsonUtils.Deserialize<Token>(json),
            _ => throw new NotSupportedException($"Token version {tokenVersion.Version} is not supported!")
        };
    }
}