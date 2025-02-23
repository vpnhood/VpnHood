using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using VpnHood.Core.Common.Converters;
using VpnHood.Core.Common.Utils;

namespace VpnHood.Core.Common.Tokens;

public class ServerToken
{
    [JsonPropertyName("ct")] public required DateTime CreatedTime { get; set; }

    [JsonPropertyName("hname")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required string HostName { get; set; }

    [JsonPropertyName("hport")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required int HostPort { get; set; }

    [JsonPropertyName("isv")]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public required bool IsValidHostName { get; set; }

    [JsonPropertyName("sec")] public required byte[]? Secret { get; set; }

    [JsonPropertyName("ch")] public byte[]? CertificateHash { get; set; }

    [JsonPropertyName("url")]
    [Obsolete("Use Urls. Version 558 or upper")]
    public string? Url {
        get => Urls?.FirstOrDefault();
        set {
            if (VhUtils.IsNullOrEmpty(Urls))
                Urls = value != null ? [value] : null;
        }
    }

    [JsonPropertyName("urls")] public string[]? Urls { get; set; }

    [JsonPropertyName("ep")]
    [JsonConverter(typeof(ArrayConverter<IPEndPoint, IPEndPointConverter>))]
    public IPEndPoint[]? HostEndPoints { get; set; }

    [JsonPropertyName("loc")]
    [Obsolete]
    public string[]? ServerLocationsLegacy {
        get => ServerLocations?.Select(x => x.Split("[").First()).ToArray();
        set {
            if (ServerLocations?.Length is null or 0)
                ServerLocations = value;
        }
    }

    [JsonPropertyName("loc2")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingDefault)]
    public string[]? ServerLocations { get; set; }

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