using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VpnHood.Core.Tunneling.ProbeResistance;

/// <summary>
/// Provides the proof that a request's client holds the server farm's secret, which the server asks for before it
/// takes an HTTP body or a WebSocket upgrade: a JSON Web Token, signed with HS256 by a key derived from the secret.
/// </summary>
/// <param name="farmSecret">The server farm's secret, which the access key carries; at least 16 bytes.</param>
/// <remarks>TCP and QUIC share it, and it runs only when a stream opens, never for the stream's packets.</remarks>
/// <exception cref="ArgumentNullException"><paramref name="farmSecret"/> is <see langword="null"/>.</exception>
/// <exception cref="ArgumentException"><paramref name="farmSecret"/> is shorter than 16 bytes.</exception>
public sealed class RequestProof(byte[] farmSecret)
{
    /// <summary>The HTTP protocol version a proof is for: 15.</summary>
    public const int ProtocolVersion = 15;

    internal const string Purpose = "vpnhood-request";

    /// <summary>How long a proof holds from its issue: 5 minutes.</summary>
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);

    /// <summary>How far the client's clock may be from the server's: 30 seconds either way.</summary>
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(30);
    private const int MaxTokenLength = 2048;
    private const string Header = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
    private readonly byte[] _signingKey = DeriveSigningKey(farmSecret);

    internal static byte[] DeriveSigningKey(byte[] farmSecret)
    {
        ArgumentNullException.ThrowIfNull(farmSecret);
        if (farmSecret.Length < 16)
            throw new ArgumentException("The farm secret must contain at least 16 bytes.", nameof(farmSecret));

        // The farm's existing AES key is 16 bytes. JWT validators require a 32-byte HS256 key;
        // derivation also keeps this use separate from encryption with that same farm secret.
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, farmSecret, 32,
            info: "VpnHood.Protocol15.RequestProof.HS256"u8.ToArray());
    }

    /// <summary>Creates a proof, issued now.</summary>
    /// <param name="protocolVersion">The protocol version, which must be <see cref="ProtocolVersion"/>.</param>
    /// <param name="now">The time, by the client's clock as the server's would read it.</param>
    /// <returns>The proof, as a JSON Web Token.</returns>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="protocolVersion"/> is not <see cref="ProtocolVersion"/>.
    /// </exception>
    public string Create(int protocolVersion, DateTimeOffset now)
    {
        if (protocolVersion != ProtocolVersion)
            throw new ArgumentOutOfRangeException(nameof(protocolVersion));
        var issuedAt = now.ToUnixTimeSeconds();
        // The farm key authorizes every route to the farm. A reverse proxy may rewrite Host.
        var payload = JsonSerializer.SerializeToUtf8Bytes(new {
            purpose = Purpose,
            version = protocolVersion,
            iat = issuedAt,
            exp = issuedAt + (long)Lifetime.TotalSeconds
        });
        var signedText = Header + "." + Encode(payload);
        var signature = HMACSHA256.HashData(_signingKey, Encoding.ASCII.GetBytes(signedText));
        return signedText + "." + Encode(signature);
    }

    /// <summary>
    /// Verifies a proof: its signature, its header and fields, and that it was issued within <see cref="Lifetime"/>
    /// before now, give or take <see cref="ClockTolerance"/>.
    /// </summary>
    /// <param name="token">The proof.</param>
    /// <param name="protocolVersion">The protocol version, which must be <see cref="ProtocolVersion"/>.</param>
    /// <param name="now">The time.</param>
    /// <returns><see langword="true"/> if the proof holds; otherwise, <see langword="false"/>.</returns>
    public bool Verify(string token, int protocolVersion, DateTimeOffset now)
    {
        if (protocolVersion != ProtocolVersion || string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
            return false;
        var parts = token.Split('.');
        if (parts.Length != 3 || parts[0].Length > 128 || parts[2].Length != 43)
            return false;

        try {
            var signature = Decode(parts[2]);
            var signedText = token[..(token.Length - parts[2].Length - 1)];
            var expected = HMACSHA256.HashData(_signingKey, Encoding.ASCII.GetBytes(signedText));
            if (!CryptographicOperations.FixedTimeEquals(expected, signature))
                return false;

            using var header = JsonDocument.Parse(Decode(parts[0]));
            var headerRoot = header.RootElement;
            if (!HasFields(headerRoot, "alg", "typ") ||
                headerRoot.GetProperty("alg").GetString() != "HS256" ||
                headerRoot.GetProperty("typ").GetString() != "JWT")
                return false;

            using var payload = JsonDocument.Parse(Decode(parts[1]));
            var root = payload.RootElement;
            if (!HasFields(root, "purpose", "version", "iat", "exp") ||
                root.GetProperty("purpose").GetString() != Purpose ||
                !root.GetProperty("version").TryGetInt32(out var version) || version != protocolVersion ||
                !root.GetProperty("iat").TryGetInt64(out var issuedAt) ||
                !root.GetProperty("exp").TryGetInt64(out var expiresAt))
                return false;

            var currentTime = now.ToUnixTimeSeconds();
            var tolerance = (long)ClockTolerance.TotalSeconds;
            var lifetime = (long)Lifetime.TotalSeconds;
            return issuedAt <= currentTime + tolerance && issuedAt >= currentTime - lifetime - tolerance &&
                   expiresAt == issuedAt + lifetime && expiresAt > currentTime - tolerance;
        }
        catch (FormatException) {
            return false;
        }
        catch (JsonException) {
            return false;
        }
        catch (InvalidOperationException) {
            return false;
        }
    }

    private static bool HasFields(JsonElement value, params IReadOnlyList<string> names)
    {
        if (value.ValueKind != JsonValueKind.Object)
            return false;
        var fields = value.EnumerateObject().ToArray();
        return fields.Length == names.Count && names.All(name => fields.Count(field => field.Name == name) == 1);
    }

    private static string Encode(byte[] bytes) =>
        Convert.ToBase64String(bytes).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    private static byte[] Decode(string value)
    {
        if (value.Length == 0 || value.Any(c => !char.IsAsciiLetterOrDigit(c) && c is not '-' and not '_'))
            throw new FormatException("Invalid proof encoding.");
        var base64 = value.Replace('-', '+').Replace('_', '/');
        base64 = base64.PadRight((base64.Length + 3) / 4 * 4, '=');
        return Convert.FromBase64String(base64);
    }
}
