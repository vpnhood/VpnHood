using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace VpnHood.Core.Tunneling.ProbeResistance;

// Proves possession of the farm key before either an HTTP body or a WebSocket upgrade is accepted.
// This is shared by TCP and QUIC, and runs only when a stream opens, never for its packets.
public sealed class RequestProof(byte[] farmSecret)
{
    public const int ProtocolVersion = 15;
    public const string Purpose = "vpnhood-request";
    public static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(5);
    public static readonly TimeSpan ClockTolerance = TimeSpan.FromSeconds(30);
    private const int MaxTokenLength = 2048;
    private const string Header = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9";
    private readonly byte[] _signingKey = DeriveSigningKey(farmSecret);

    public static byte[] DeriveSigningKey(byte[] farmSecret)
    {
        ArgumentNullException.ThrowIfNull(farmSecret);
        if (farmSecret.Length < 16)
            throw new ArgumentException("The farm secret must contain at least 16 bytes.", nameof(farmSecret));

        // The farm's existing AES key is 16 bytes. JWT validators require a 32-byte HS256 key;
        // derivation also keeps this use separate from encryption with that same farm secret.
        return HKDF.DeriveKey(HashAlgorithmName.SHA256, farmSecret, 32,
            info: "VpnHood.Protocol15.RequestProof.HS256"u8.ToArray());
    }

    public string Create(string hostName, int protocolVersion, DateTimeOffset now)
    {
        if (protocolVersion != ProtocolVersion)
            throw new ArgumentOutOfRangeException(nameof(protocolVersion));
        var host = NormalizeHost(hostName) ?? throw new ArgumentException("Invalid server host.", nameof(hostName));
        var issuedAt = now.ToUnixTimeSeconds();
        var payload = JsonSerializer.SerializeToUtf8Bytes(new {
            purpose = Purpose,
            version = protocolVersion,
            host,
            iat = issuedAt,
            exp = issuedAt + (long)Lifetime.TotalSeconds
        });
        var signedText = Header + "." + Encode(payload);
        var signature = HMACSHA256.HashData(_signingKey, Encoding.ASCII.GetBytes(signedText));
        return signedText + "." + Encode(signature);
    }

    public bool Verify(string token, string hostName, int protocolVersion, DateTimeOffset now)
    {
        if (protocolVersion != ProtocolVersion || string.IsNullOrEmpty(token) || token.Length > MaxTokenLength)
            return false;
        var host = NormalizeHost(hostName);
        if (host == null)
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
            if (!HasFields(root, "purpose", "version", "host", "iat", "exp") ||
                root.GetProperty("purpose").GetString() != Purpose ||
                !root.GetProperty("version").TryGetInt32(out var version) || version != protocolVersion ||
                root.GetProperty("host").GetString() != host ||
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

    private static string? NormalizeHost(string hostName)
    {
        // Bind to the HTTP Host, as proxies may terminate TLS under a different certificate.
        // Ports and DNS case/trailing dots do not change the server's hostname.
        if (string.IsNullOrEmpty(hostName) || hostName.Length > 512 ||
            hostName.Any(c => char.IsWhiteSpace(c) || c is '/' or '\\' or '?' or '#' or '@') ||
            !Uri.TryCreate("https://" + hostName + "/", UriKind.Absolute, out var uri) ||
            uri.HostNameType is UriHostNameType.Unknown or UriHostNameType.Basic)
            return null;
        return uri.IdnHost.TrimEnd('.').ToLowerInvariant();
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
