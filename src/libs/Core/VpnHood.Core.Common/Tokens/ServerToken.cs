using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Net.Toolkit.Converters;
using VpnHood.Net.Toolkit.Utils;

namespace VpnHood.Core.Common.Tokens;

/// <summary>
/// Represents a server token: how a client finds the servers of a farm or a standalone server, and trusts them.
/// MANAGER calls a farm's its farm token.
/// </summary>
/// <remarks>
/// It travels inside an access key, and, encrypted (<see cref="Encrypt"/>), at its update URLs, from which a client
/// takes a newer one.
/// </remarks>
public class ServerToken
{
    /// <summary>Gets or sets when the token was made, in UTC, which tells a newer one (<see cref="ServerTokenExtensions.IsTokenUpdated"/>).</summary>
    [JsonPropertyName("ct")]
    public required DateTime CreatedTime { get; set; }

    /// <summary>
    /// Gets or sets the servers' host name, which the client gives in its TLS handshake, and looks up when it is a
    /// real one (<see cref="IsValidHostName"/>).
    /// </summary>
    [JsonPropertyName("hname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string HostName { get; set; }

    /// <summary>Gets or sets the port of the host name's addresses.</summary>
    [JsonPropertyName("hport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required int HostPort { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the host name is a real one, which resolves to the servers: the client
    /// then looks it up for their addresses, unless its strategy is <see cref="EndPointStrategy.IpOnly"/> with
    /// <see cref="HostEndPoints"/> listed. When <see langword="false"/>, the client uses <see cref="HostEndPoints"/>
    /// alone.
    /// </summary>
    [JsonPropertyName("isv")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required bool IsValidHostName { get; set; }

    /// <summary>
    /// Gets or sets the farm's secret: the AES key of the token at its update URLs, from which the client also derives
    /// the key that signs its proofs to the servers. A client of this version refuses an access key whose server token
    /// has none.
    /// </summary>
    [JsonPropertyName("sec")]
    public required byte[]? Secret { get; set; }

    /// <summary>
    /// Gets or sets the hash of the servers' certificate (<see cref="System.Security.Cryptography.X509Certificates.X509Certificate.GetCertHash()"/>),
    /// or <see langword="null"/>. A client accepts a certificate of this hash even when the system's checks of its
    /// chain, name or dates fail; any other certificate must pass them, though the client may supply a Let's Encrypt
    /// root the system lacks, the other checks still applying.
    /// </summary>
    [JsonPropertyName("ch")]
    public byte[]? CertificateHash { get; set; }

    /// <summary>
    /// Gets or sets the first update URL, for clients before <see cref="Urls"/>; setting it sets <see cref="Urls"/>
    /// only while that is empty.
    /// </summary>
    [JsonPropertyName("url")]
    [Obsolete("Use Urls. Version 558 or upper")]
    public string? Url {
        get => Urls?.FirstOrDefault();
        set {
            if (VhUtils.IsNullOrEmpty(Urls))
                Urls = value != null ? [value] : null;
        }
    }

    /// <summary>
    /// Gets or sets the update URLs: where a client finds a newer token, encrypted with <see cref="Secret"/>, or
    /// <see langword="null"/>.
    /// </summary>
    [JsonPropertyName("urls")]
    public string[]? Urls { get; set; }

    /// <summary>Gets or sets the servers' endpoints, which a client may connect to without DNS, or <see langword="null"/>.</summary>
    [JsonPropertyName("ep")]
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? HostEndPoints { get; set; }

    /// <summary>
    /// Gets or sets the path the servers' requests go under, such as <c>/servers/serverId</c>, or
    /// <see langword="null"/>.
    /// </summary>
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    [JsonPropertyName("path")]
    public string? PathBase { get; set; } // such as /servers/serverId

    /// <summary>
    /// Gets or sets how a client orders the host name's addresses and <see cref="HostEndPoints"/>, when its own
    /// setting is <see cref="EndPointStrategy.Auto"/>.
    /// </summary>
    [JsonPropertyName("ep_st")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public EndPointStrategy EndPointsStrategy { get; set; }

    /// <summary>
    /// Gets or sets the server locations without their tags, for older clients; setting it sets
    /// <see cref="ServerLocations"/> only while that is empty.
    /// </summary>
    [JsonPropertyName("loc")]
    [Obsolete]
    public string[]? ServerLocationsLegacy {
        get => ServerLocations?.Select(x => x.Split("[").First()).ToArray();
        set {
            if (ServerLocations?.Length is null or 0)
                ServerLocations = value;
        }
    }

    /// <summary>
    /// Gets or sets the servers' locations, each as <see cref="ServerLocationInfo.Parse"/> reads one, such as
    /// <c>US/California [#premium]</c>, or <see langword="null"/>.
    /// </summary>
    [JsonPropertyName("loc2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string[]? ServerLocations { get; set; }

    /// <summary>
    /// Encrypts the token, for its update URLs: AES in CBC mode with PKCS #7 padding, keyed with
    /// <see cref="Secret"/>, of its JSON with the properties at their defaults left out, but for those marked to be
    /// always written, such as <see cref="IsValidHostName"/>.
    /// </summary>
    /// <param name="iv">The initialization vector, or <see langword="null"/> for random bytes as long as the secret.</param>
    /// <returns>The Base64 of the IV, a dot, and the Base64 of the encrypted JSON.</returns>
    /// <remarks>
    /// The IV must be 16 bytes, AES's block, so a random one fits only a 16-byte secret. Nothing authenticates the
    /// result.
    /// </remarks>
    /// <exception cref="Exception">The token has no secret.</exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">
    /// The IV is not 16 bytes, or the secret is no AES key's length.
    /// </exception>
    public string Encrypt(byte[]? iv = null)
    {
        var json = JsonSerializer.Serialize(this, new JsonSerializerOptions {
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingDefault
        });

        if (Secret is null)
            throw new Exception("There is no Secret in ServerToken.");

        // generate IV
        if (iv == null) {
            using var rng = RandomNumberGenerator.Create();
            iv = new byte[Secret.Length];
            rng.GetBytes(iv);
        }

        // aes
        using var aesAlg = Aes.Create();
        aesAlg.Mode = CipherMode.CBC;
        aesAlg.Key = Secret;
        aesAlg.IV = iv;
        aesAlg.Padding = PaddingMode.PKCS7;

        using var msEncrypt = new MemoryStream();

        // dispose CryptoStream and StreamWriter before using msEncrypt.ToArray()
        using (var encryptor = aesAlg.CreateEncryptor())
        using (var csEncrypt = new CryptoStream(msEncrypt, encryptor, CryptoStreamMode.Write))
        using (var streamWriter = new StreamWriter(csEncrypt)) {
            streamWriter.Write(json);
            streamWriter.Flush();
        }

        var serverToken = Convert.ToBase64String(iv) + "." + Convert.ToBase64String(msEncrypt.ToArray());
        return serverToken;
    }

    /// <summary>Decrypts a token that <see cref="Encrypt"/> made.</summary>
    /// <param name="serverSecret">The farm's secret.</param>
    /// <param name="base64">The encrypted token: the Base64 of the IV, a dot, and the Base64 of the encrypted JSON.</param>
    /// <returns>The token.</returns>
    /// <exception cref="FormatException">
    /// The text is not two Base64 parts with a dot between, or an endpoint in the token is not valid.
    /// </exception>
    /// <exception cref="System.Security.Cryptography.CryptographicException">The secret is wrong, or the data is corrupt.</exception>
    /// <exception cref="System.Text.Json.JsonException">The decrypted text is not a valid token's JSON.</exception>
    /// <exception cref="InvalidDataException">The decrypted JSON is <c>null</c>.</exception>
    public static ServerToken Decrypt(byte[] serverSecret, string base64)
    {
        var parts = base64.Trim().Split('.');
        if (parts.Length != 2)
            throw new FormatException("Could not parse server token data.");

        // aes
        using var aesAlg = Aes.Create();
        aesAlg.Mode = CipherMode.CBC;
        aesAlg.Key = serverSecret;
        aesAlg.IV = Convert.FromBase64String(parts[0]);
        aesAlg.Padding = PaddingMode.PKCS7;

        using var decryptor = aesAlg.CreateDecryptor();
        using var msEncrypt = new MemoryStream(Convert.FromBase64String(parts[1]));
        using var csEncrypt = new CryptoStream(msEncrypt, decryptor, CryptoStreamMode.Read);
        using var streamReader = new StreamReader(csEncrypt);

        var json = streamReader.ReadToEnd();
        var serverToken = JsonUtils.Deserialize<ServerToken>(json);
        return serverToken;
    }
}