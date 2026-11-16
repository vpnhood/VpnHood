using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Utils;

// ReSharper disable StringLiteralTypo

namespace VpnHood.Core.Common.Tokens;

/// <summary>
/// Represents an access key: what a client needs to connect to a VPN's servers, and the access it gives.
/// </summary>
/// <remarks>
/// The key travels as text, <see cref="ToAccessKey"/>'s form, whose JSON names are the short ones of the properties'
/// attributes, part of the VPN's contract.
/// </remarks>
public class Token
{
    /// <summary>Gets the access key's format version: always 4.</summary>
    [JsonPropertyName("v")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int Version => 4;

    /// <summary>Gets or sets the access key's name, which an app shows, or <see langword="null"/>.</summary>
    [JsonPropertyName("name")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string? Name { get; set; }

    /// <summary>
    /// Gets or sets the ID by which the key's issuer knows it in support, or <see langword="null"/>.
    /// </summary>
    [JsonPropertyName("sid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string? SupportId { get; set; }

    /// <summary>Gets or sets the ID by which the servers know the access.</summary>
    [JsonPropertyName("tid")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string TokenId { get; set; }

    /// <summary>
    /// Gets or sets when the key was issued, in UTC; <see cref="DateTime.MinValue"/> in a key that does not say.
    /// </summary>
    [JsonPropertyName("iat")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public DateTime IssuedAt { get; set; } =
        DateTime.MinValue; // for backward compatibility. Default value it is not required

    /// <summary>Gets or sets the access's secret, which the client proves to the server.</summary>
    [JsonPropertyName("sec")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required byte[] Secret { get; set; }

    /// <summary>Gets or sets the server token: how the client finds the servers and trusts them.</summary>
    [JsonPropertyName("ser")]
    public required ServerToken ServerToken { get; set; }

    /// <summary>
    /// Gets or sets the key's tags, such as those of <see cref="TokenRegisteredTags"/>. The default is empty.
    /// </summary>
    [JsonPropertyName("tags")]
    public string[] Tags { get; set; } = [];

    /// <summary>Gets or sets a value indicating whether the key is a public one, shared by many people.</summary>
    [JsonPropertyName("ispub")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public bool IsPublic { get; set; }

    /// <summary>
    /// Gets or sets what the key's issuer offers the people of each set of countries, or <see langword="null"/>.
    /// </summary>
    /// <remarks>
    /// An app takes the policy whose <see cref="ClientPolicy.ClientCountries"/> holds the device's country, else one
    /// that holds <c>*</c>.
    /// </remarks>
    [JsonPropertyName("cpols")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public ClientPolicy[]? ClientPolicies { get; set; }

    /// <summary>
    /// Writes the access key as text: <c>vh:</c> and the Base64 of its JSON, with the properties at their defaults left
    /// out, but for those marked to be always written, such as <see cref="Name"/>.
    /// </summary>
    /// <returns>The access key.</returns>
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

    /// <summary>
    /// Reads an access key from its text: the Base64 of its JSON, after <c>vh:</c>, <c>vh://</c>, <c>vhkey:</c> or
    /// <c>vhkey://</c>, if any.
    /// </summary>
    /// <param name="base64">
    /// The text. Surrounding white space and quotes, a trailing <c>/</c>, as Windows adds to a link, and missing Base64
    /// padding are allowed.
    /// </param>
    /// <returns>The access key.</returns>
    /// <exception cref="FormatException">
    /// The text is no valid Base64, or an endpoint or a duration in the key is not valid.
    /// </exception>
    /// <exception cref="NotSupportedException">The key's version is not 4.</exception>
    /// <exception cref="System.Text.Json.JsonException">The text is not a valid access key's JSON.</exception>
    /// <exception cref="InvalidDataException">The JSON is <c>null</c>.</exception>
    /// <exception cref="OverflowException">A duration in the key is out of range.</exception>
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