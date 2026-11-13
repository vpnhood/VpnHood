using System.Net;
using System.Net.Sockets;
using System.Security.Cryptography;
using VpnHood.Net.Toolkit.Net;

namespace VpnHood.Net.Toolkit.Utils;

/// <summary>
/// Redacts identifying values for logs and screens: a globally routable address becomes a keyed token that cannot be
/// turned back without the process's key, which dies with it; a private or local address stays readable, and host
/// names and IDs keep some of their characters.
/// <para>
/// Whether redaction applies is carried by the instance, not by the method you pick, so a call site states
/// its own guarantee. <see cref="Always" /> redacts whatever the settings say — the app UI and the tracking
/// log need that, since neither should start printing real addresses because logging was switched out of
/// anonymous mode. 
/// </para>
/// <para>
/// <c>Redact*</c> produces a string now; <c>Format*</c> wraps the same work in a <see cref="RedactedValue{T}" />
/// that redacts on its <c>ToString</c>, which a logger calls when it writes the record.
/// </para>
/// </summary>
/// <param name="isAnonymousMode">Whether the instance redacts; otherwise, it writes the values as they are.</param>
public class Redactor(bool isAnonymousMode)
{
    /// <summary>
    /// Carries a value into a log record without redacting it yet.
    /// <para>
    /// A log argument is an ordinary method argument, so <c>VhLogger.Format(x)</c> runs before the logger is
    /// ever asked whether the level is enabled. Returning a string there means every disabled hot-path
    /// statement still pays for the redaction — an allocation, and since the redaction became keyed, an
    /// HMAC. This type defers that work to <see cref="ToString" />, which a logger calls when it writes the
    /// record; any other call to it redacts too.
    /// </para>
    /// <para>
    /// The point is that no call site has to remember anything. Guarding hot statements with a level check
    /// works too, but it is a ritual that the next log statement someone adds will not repeat.
    /// </para>
    /// </summary>
    /// <typeparam name="T">The type of the value.</typeparam>
    /// <param name="redactor">The redactor that redacts it.</param>
    /// <param name="value">The value, or <see langword="null"/>.</param>
    /// <param name="redact">The redaction.</param>
    public readonly struct RedactedValue<T>(Redactor redactor, T? value, Func<Redactor, T, string> redact)
        where T : class
    {
        /// <summary>Redacts the value now.</summary>
        /// <returns>The redacted text, or <c>&lt;null&gt;</c>.</returns>
        public override string ToString() => value is null ? "<null>" : redact(redactor, value);
    }

    // The key lives only in memory: it is created when the process starts and dies with it, and is never
    // written anywhere. That is what makes this anonymization rather than masking — an address cannot be
    // recovered from a token without the key, and once the process ends no key exists to recover it with,
    // so a log file left on disk can never be resolved back to anybody. Within one run the same address
    // always maps to the same token, which is all an operator needs to follow one client through a log.
    //
    // Hashing alone would not be enough and the key is what fixes it: the whole IPv4 space is only 2^32
    // addresses, so an unkeyed hash of one can be reversed by exhaustive search in seconds.
    //
    // Truncation was the obvious alternative and it does not work. An IPv4 /24 leaves 256 candidates,
    // and an IPv6 /48 is a single subscriber at many ISPs — neither stops a person being singled out,
    // which is the test anonymization actually has to pass.
    //
    // Static on purpose, never per instance: every Redactor must agree on the token for a given address,
    // or the UI and the log would disagree, and replacing Default would silently rotate every token
    // mid-run — destroying the within-run linkability the whole design exists to provide.
    private static readonly byte[] IpRedactionKey = RandomNumberGenerator.GetBytes(32);

    /// <summary>Redacts unconditionally, for callers whose output must never depend on a logging setting.</summary>
    public static Redactor Always { get; } = new(isAnonymousMode: true);


    /// <summary>Gets a value indicating whether this redactor redacts; otherwise, it writes the values as they are.</summary>
    public bool IsAnonymousMode { get; } = isAnonymousMode;

    /// <summary>
    /// Redacts an address: a globally routable address becomes a keyed token, the same for the same address within the run; a private or
    /// local one stays as it is.
    /// </summary>
    /// <param name="ipAddress">The address.</param>
    /// <returns>The text.</returns>
    public string RedactIpAddress(IPAddress ipAddress)
    {
        return IsAnonymousMode ? RedactIpAddressCore(ipAddress) : ipAddress.ToString();
    }

    /// <summary>Redacts an endpoint's address as <see cref="RedactIpAddress"/> does, and keeps its port.</summary>
    /// <param name="endPoint">The endpoint; one that is not an IP endpoint is written as it is.</param>
    /// <returns>The text.</returns>
    public string RedactEndPoint(EndPoint endPoint)
    {
        if (endPoint is not IPEndPoint ipEndPoint)
            return endPoint.ToString() ?? "<null>";

        return ipEndPoint.AddressFamily is AddressFamily.InterNetwork or AddressFamily.InterNetworkV6
            ? $"{RedactIpAddress(ipEndPoint.Address)}:{ipEndPoint.Port}"
            : ipEndPoint.ToString();
    }

    /// <summary>Redacts a network's prefix as <see cref="RedactIpAddress"/> does, and keeps its length.</summary>
    /// <param name="ipNetwork">The network.</param>
    /// <returns>The text.</returns>
    public string RedactIpNetwork(IpNetwork ipNetwork)
    {
        return IsAnonymousMode
            ? $"{RedactIpAddress(ipNetwork.Prefix)}/{ipNetwork.PrefixLength}"
            : ipNetwork.ToString();
    }

    /// <summary>Redacts addresses, separated by commas.</summary>
    /// <param name="ipAddresses">The addresses.</param>
    /// <returns>The text.</returns>
    public string RedactIpAddresses(IReadOnlyList<IPAddress> ipAddresses)
    {
        return string.Join(", ", ipAddresses.Select(RedactIpAddress));
    }

    /// <summary>Redacts networks, separated by commas.</summary>
    /// <param name="ipNetworks">The networks.</param>
    /// <returns>The text.</returns>
    public string RedactIpNetworks(IReadOnlyList<IpNetwork> ipNetworks)
    {
        return string.Join(", ", ipNetworks.Select(RedactIpNetwork));
    }

    /// <summary>
    /// Redacts a host name: one that is an address or an endpoint as one; otherwise, in anonymous mode, all but its first
    /// two and last four characters, for a short name all but its last four, and a name of four characters or fewer
    /// whole.
    /// </summary>
    /// <param name="dnsName">The host name, or <see langword="null"/>.</param>
    /// <returns>The text, or <c>&lt;null&gt;</c>.</returns>
    public string RedactHostName(string? dnsName)
    {
        if (dnsName == null) return "<null>";
        if (IPAddress.TryParse(dnsName, out var ipAddress)) return RedactIpAddress(ipAddress);
        if (IPEndPoint.TryParse(dnsName, out var ipEndPoint)) return RedactEndPoint(ipEndPoint);
        if (!IsAnonymousMode) return dnsName;

        // its last four characters would be the whole of it
        if (dnsName.Length <= 4)
            return "***";

        return dnsName.Length <= 8
            ? "***" + dnsName[^4..]
            : dnsName[..2] + "***" + dnsName[^4..];
    }

    /// <summary>Redacts an ID: in anonymous mode, only its second half shows, after <c>**</c>.</summary>
    /// <param name="id">The ID, or <see langword="null"/>.</param>
    /// <returns>The text, or <c>&lt;null&gt;</c>.</returns>
    public string RedactId(object? id)
    {
        if (id == null) return "<null>";

        var str = id.ToString() ?? "";
        return IsAnonymousMode ? "**" + str[(str.Length / 2)..] : str;
    }

    /// <summary>
    /// Redacts the addresses in a packet's text, after each of its <c>SourceAddress</c>, <c>DestinationAddress</c>,
    /// <c>Src</c> and <c>Dst</c> labels, as <see cref="RedactIpAddress"/> does. A value ends at a comma, a space or the
    /// text's end; one that is no address is written as <c>***</c>, and the rest of the text stays as it is.
    /// </summary>
    /// <param name="ipPacketText">The packet's text.</param>
    /// <returns>The text.</returns>
    public string RedactPacketText(string ipPacketText)
    {
        if (!IsAnonymousMode)
            return ipPacketText;

        ipPacketText = RedactIpAddressInText(ipPacketText, "SourceAddress");
        ipPacketText = RedactIpAddressInText(ipPacketText, "DestinationAddress");
        ipPacketText = RedactIpAddressInText(ipPacketText, "Src");
        ipPacketText = RedactIpAddressInText(ipPacketText, "Dst");
        return ipPacketText;
    }

    // the lambdas are static, so the compiler caches each one in a static field and no delegate is
    // allocated per call — an instance method group could not be cached that way, being bound to its receiver
    /// <summary>Wraps an endpoint for a log line; it redacts on its <c>ToString</c>.</summary>
    /// <param name="endPoint">The value, or <see langword="null"/>.</param>
    /// <returns>The wrapped value.</returns>
    public RedactedValue<EndPoint> Format(EndPoint? endPoint)
    {
        return new RedactedValue<EndPoint>(this, endPoint, static (r, v) => r.RedactEndPoint(v));
    }

    /// <summary>Wraps an endpoint for a log line; it redacts on its <c>ToString</c>.</summary>
    /// <param name="endPoint">The value, or <see langword="null"/>.</param>
    /// <returns>The wrapped value.</returns>
    public RedactedValue<EndPoint> Format(IpEndPointValue? endPoint)
    {
        return new RedactedValue<EndPoint>(this, endPoint?.ToIPEndPoint(), static (r, v) => r.RedactEndPoint(v));
    }

    /// <summary>Wraps an address for a log line; it redacts on its <c>ToString</c>.</summary>
    /// <param name="ipAddress">The value, or <see langword="null"/>.</param>
    /// <returns>The wrapped value.</returns>
    public RedactedValue<IPAddress> Format(IPAddress? ipAddress)
    {
        return new RedactedValue<IPAddress>(this, ipAddress, static (r, v) => r.RedactIpAddress(v));
    }

    /// <summary>Wraps a network for a log line; it redacts on its <c>ToString</c>.</summary>
    /// <param name="ipNetwork">The value, or <see langword="null"/>.</param>
    /// <returns>The wrapped value.</returns>
    public RedactedValue<IpNetwork> Format(IpNetwork? ipNetwork)
    {
        return new RedactedValue<IpNetwork>(this, ipNetwork, static (r, v) => r.RedactIpNetwork(v));
    }

    // the sequence is materialized now and formatted later: deferring the enumeration itself would let a
    // collection change between the log call and the record being written
    /// <summary>Wraps addresses for a log line; it redacts on its <c>ToString</c>.</summary>
    /// <param name="ipAddresses">The value.</param>
    /// <returns>The wrapped value.</returns>
    public RedactedValue<IReadOnlyList<IPAddress>> Format(IEnumerable<IPAddress> ipAddresses)
    {
        return new RedactedValue<IReadOnlyList<IPAddress>>(this,
            ipAddresses as IReadOnlyList<IPAddress> ?? [.. ipAddresses], static (r, v) => r.RedactIpAddresses(v));
    }

    /// <summary>Wraps networks for a log line; it redacts on its <c>ToString</c>.</summary>
    /// <param name="ipNetworks">The value.</param>
    /// <returns>The wrapped value.</returns>
    public RedactedValue<IReadOnlyList<IpNetwork>> Format(IEnumerable<IpNetwork> ipNetworks)
    {
        return new RedactedValue<IReadOnlyList<IpNetwork>>(this,
            ipNetworks as IReadOnlyList<IpNetwork> ?? [.. ipNetworks], static (r, v) => r.RedactIpNetworks(v));
    }

    private static string RedactIpAddressCore(IPAddress ipAddress)
    {
        // a dual-stack socket hands us the mapped form of an address that also arrives as plain IPv4
        // elsewhere; without this the same host would get two unrelated tokens in one log
        if (ipAddress.IsIPv4MappedToIPv6)
            ipAddress = ipAddress.MapToIPv4();

        var addressBytes = ipAddress.GetAddressBytesFast(stackalloc byte[16]);

        // NAT64's well-known prefix carries the IPv4 address it reaches in its last 32 bits: the same
        // host, so the same token, as that address
        if (addressBytes.StartsWith(Nat64WellKnownPrefix))
            addressBytes = addressBytes[12..];

        var isV4 = addressBytes.Length == 4;

        // loopback, unspecified, link-local, multicast, broadcast and the private ranges of the user's own
        // LAN. None of them can point at a subscriber or at a site, and an operator needs to read them as
        // they are — split routing and adapter faults are far harder to diagnose without them.
        if (!IsGloballyRoutable(addressBytes, isV4))
            return ipAddress.ToString();

        // the family byte separates the two address spaces, so a 4-byte and a 16-byte input can never
        // be hashed to the same token by coincidence
        Span<byte> input = stackalloc byte[17];
        input[0] = (byte)(isV4 ? 4 : 6);
        addressBytes.CopyTo(input[1..]);

        Span<byte> hash = stackalloc byte[HMACSHA256.HashSizeInBytes];
        HMACSHA256.HashData(IpRedactionKey, input[..(addressBytes.Length + 1)], hash);

        // The key, not the width, is what makes this irreversible. Testing a guess means computing the
        // HMAC of it, which needs the key; without the key the 2^32 IPv4 space cannot be enumerated at
        // all, and searching for a key that maps a guess to a given token is pointless because roughly
        // 2^232 of them do. That covers every threat that matters here — a log on disk, a log a user
        // sends us, a log handed to a third party — because the key is gone the moment the process ends.
        //
        // Against someone holding the live key it is a different story, and worth being honest about: a
        // token can be tested against a guessed address, and while 24 bits leave ~256 IPv4 candidates
        // (2^32/2^24), those candidates are not equally plausible — most are dead address space, so a
        // guess of a real site is confirmed in practice. The truncation is a fallback that limits damage
        // if the key ever leaks, not the primary defence. Anyone who can read this process's memory can
        // read its traffic, which is the more direct attack.
        //
        // 32 bits, because the width is not what protects anything and collisions are a real cost: two
        // unrelated destinations sharing a token is a wrong answer to the one question this exists to
        // answer. At 24 bits a thousand distinct addresses in one log collide about 3% of the time; at
        // 32 that falls to roughly 0.01%.
        return (isV4 ? "v4-" : "v6-") + Convert.ToHexStringLower(hash[..4]);
    }

    // 64:ff9b::/96 and 64:ff9b:1::/48 (RFC 6052, RFC 8215)
    private static ReadOnlySpan<byte> Nat64WellKnownPrefix => [0x00, 0x64, 0xff, 0x9b, 0, 0, 0, 0, 0, 0, 0, 0];
    private static ReadOnlySpan<byte> Nat64LocalUsePrefix => [0x00, 0x64, 0xff, 0x9b, 0x00, 0x01];

    private static bool IsGloballyRoutable(ReadOnlySpan<byte> addressBytes, bool isV4)
    {
        // 2000::/3 is the only globally routable IPv6 range; everything else is loopback, unspecified,
        // link-local, unique-local or multicast, but for NAT64's local-use prefix, which carries the IPv4
        // address it reaches at a place the network's prefix length decides
        if (!isV4)
            return (addressBytes[0] & 0xE0) == 0x20 || addressBytes.StartsWith(Nat64LocalUsePrefix);

        return addressBytes[0] switch {
            0 or 127 => false, // unspecified, loopback
            10 => false, // 10.0.0.0/8
            172 => (addressBytes[1] & 0xF0) != 16, // 172.16.0.0/12
            192 => addressBytes[1] != 168, // 192.168.0.0/16
            169 => addressBytes[1] != 254, // 169.254.0.0/16 link-local
            >= 224 => false, // multicast, reserved, broadcast
            _ => true
        };
    }

    private string RedactIpAddressInText(string text, string keyText)
    {
        var label = keyText + "=";
        var searchStart = 0;
        while (true) {
            var labelStart = text.IndexOf(label, searchStart, StringComparison.Ordinal);
            if (labelStart == -1)
                return text; // a text without the label has nothing of it to hide

            // the end of a longer label is another label
            var start = labelStart + label.Length;
            if (labelStart > 0 && char.IsLetterOrDigit(text[labelStart - 1])) {
                searchStart = start;
                continue;
            }

            var length = text.AsSpan(start).IndexOfAny(',', ' ');
            var end = length == -1 ? text.Length : start + length;

            // an address as its token; what is no address is hidden too, never left as it came
            var redacted = IPAddress.TryParse(text.AsSpan(start, end - start), out var ipAddress)
                ? RedactIpAddress(ipAddress)
                : "***";

            text = string.Concat(text.AsSpan(0, start), redacted, text.AsSpan(end));
            searchStart = start + redacted.Length;
        }
    }
}
