using System.Buffers.Binary;
using System.Net;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>
/// Represents an IP address as a value: what an <see cref="IPAddress"/> holds, with no object on the heap.
/// </summary>
/// <remarks>
/// Two values are equal when the two <see cref="IPAddress"/> objects would be: in the version, the address and the
/// IPv6 scope id. So 10.0.0.2, ::10.0.0.2 and ::ffff:10.0.0.2 are three addresses, and fe80::1%3 is not fe80::1. The
/// default value holds no address: it is neither IPv4 nor IPv6, it is written as an empty string, and converting it
/// throws.
/// </remarks>
public readonly struct IpAddressValue : IEquatable<IpAddressValue>
{
    private const string NoAddressMessage = "The value holds no IP address.";

    // Two ulongs, not one UInt128: UInt128 is 16-byte aligned and would make the value 32 bytes instead of 24.
    // Together they are the address as a big-endian number, an IPv4 address in the low 32 bits.
    private readonly ulong _high;
    private readonly ulong _low;
    private readonly uint _scopeId;
    private readonly IpVersion _version;

    /// <summary>
    /// Initializes a new instance of the <see cref="IpAddressValue"/> structure from the bytes of an address, as an IP
    /// header holds them.
    /// </summary>
    /// <param name="address">The address in network byte order: 4 bytes for IPv4, 16 bytes for IPv6.</param>
    /// <exception cref="ArgumentException"><paramref name="address"/> is neither 4 nor 16 bytes long.</exception>
    public IpAddressValue(ReadOnlySpan<byte> address)
    {
        this = FromBytes(address, scopeId: 0);
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpAddressValue"/> structure from an <see cref="IPAddress"/>, with
    /// its scope id.
    /// </summary>
    /// <param name="ipAddress">The address.</param>
    /// <exception cref="ArgumentNullException"><paramref name="ipAddress"/> is <see langword="null"/>.</exception>
    public IpAddressValue(IPAddress ipAddress)
    {
        ArgumentNullException.ThrowIfNull(ipAddress);
        var scopeId = ipAddress.IsV6() ? (uint)ipAddress.ScopeId : 0;
        this = FromBytes(ipAddress.GetAddressBytesFast(stackalloc byte[16]), scopeId);
    }

    private IpAddressValue(ulong high, ulong low, uint scopeId, IpVersion version)
    {
        _high = high;
        _low = low;
        _scopeId = scopeId;
        _version = version;
    }

    /// <summary>
    /// Gets whether the address is an IPv4-mapped IPv6 address, such as ::ffff:10.0.0.2.
    /// </summary>
    /// <value>
    /// <see langword="true"/> if the address is an IPv4-mapped IPv6 address; otherwise, <see langword="false"/>.
    /// </value>
    public bool IsIPv4MappedToIPv6 => _version == IpVersion.IPv6 && _high == 0 && (_low >> 32) == 0xFFFF;

    /// <summary>Determines whether the address is an IPv4 address.</summary>
    /// <returns><see langword="true"/> if the address is IPv4; otherwise, <see langword="false"/>.</returns>
    public bool IsV4() => _version == IpVersion.IPv4;

    /// <summary>Determines whether the address is an IPv6 address.</summary>
    /// <returns><see langword="true"/> if the address is IPv6; otherwise, <see langword="false"/>.</returns>
    public bool IsV6() => _version == IpVersion.IPv6;

    /// <summary>
    /// Maps the address to IPv4, as <see cref="IPAddress.MapToIPv4"/> does: an IPv4 address stays as it is, and an
    /// IPv6 address gives its last 4 bytes.
    /// </summary>
    /// <returns>An IPv4 address.</returns>
    /// <exception cref="InvalidOperationException">The value holds no address.</exception>
    public IpAddressValue MapToIPv4() => _version switch {
        IpVersion.IPv4 => this,
        IpVersion.IPv6 => new IpAddressValue(0, (uint)_low, 0, IpVersion.IPv4),
        _ => throw new InvalidOperationException(NoAddressMessage)
    };

    /// <summary>
    /// Tries to write the address to a span of bytes in network byte order, as an IP header holds it.
    /// </summary>
    /// <param name="destination">The span to write to.</param>
    /// <param name="bytesWritten">
    /// When this method returns, the number of bytes written: 4 for IPv4, 16 for IPv6, or 0 when
    /// <paramref name="destination"/> is too short.
    /// </param>
    /// <returns>
    /// <see langword="true"/> if the address was written; <see langword="false"/> if <paramref name="destination"/> is
    /// too short.
    /// </returns>
    /// <exception cref="InvalidOperationException">The value holds no address.</exception>
    public bool TryWriteBytes(Span<byte> destination, out int bytesWritten)
    {
        var length = _version switch {
            IpVersion.IPv4 => 4,
            IpVersion.IPv6 => 16,
            _ => throw new InvalidOperationException(NoAddressMessage)
        };

        if (destination.Length < length) {
            bytesWritten = 0;
            return false;
        }

        if (length == 4) {
            BinaryPrimitives.WriteUInt32BigEndian(destination, (uint)_low);
        }
        else {
            BinaryPrimitives.WriteUInt64BigEndian(destination, _high);
            BinaryPrimitives.WriteUInt64BigEndian(destination[8..], _low);
        }

        bytesWritten = length;
        return true;
    }

    /// <summary>Converts the value to an <see cref="IPAddress"/>, with its scope id.</summary>
    /// <returns>A new address.</returns>
    /// <exception cref="InvalidOperationException">The value holds no address.</exception>
    public IPAddress ToIPAddress()
    {
        Span<byte> bytes = stackalloc byte[16];
        BinaryPrimitives.WriteUInt64BigEndian(bytes, _high);
        BinaryPrimitives.WriteUInt64BigEndian(bytes[8..], _low);
        return _version switch {
            IpVersion.IPv4 => new IPAddress(bytes[12..]),
            IpVersion.IPv6 => new IPAddress(bytes, _scopeId),
            _ => throw new InvalidOperationException(NoAddressMessage)
        };
    }

    /// <inheritdoc/>
    /// <remarks>Written as <see cref="IPAddress.ToString"/> writes the address; the default value gives an empty
    /// string.</remarks>
    public override string ToString() => _version == default ? string.Empty : ToIPAddress().ToString();

    /// <inheritdoc/>
    public bool Equals(IpAddressValue other) =>
        _low == other._low &&
        _high == other._high &&
        _scopeId == other._scopeId &&
        _version == other._version;

    /// <inheritdoc/>
    public override bool Equals(object? obj) => obj is IpAddressValue other && Equals(other);

    // Without it, value.Equals(ipAddress) would compile through Equals(object) and always be false.
    /// <summary>
    /// Throws <see cref="NotSupportedException"/>: an <see cref="IPAddress"/> is compared with a value once it is
    /// converted to one.
    /// </summary>
    /// <param name="other">The address.</param>
    /// <returns>Never returns.</returns>
    /// <exception cref="NotSupportedException">Always.</exception>
    [Obsolete("Convert the address to a value once, with ToValue(), and compare the two values.", error: true)]
    public bool Equals(IPAddress? other) => throw new NotSupportedException("Compare two IpAddressValue values.");

    // HashCode is seeded per process, as IPAddress's hash is: a fixed formula would let a client choose destination
    // addresses that collide in the server's tables.
    /// <inheritdoc/>
    public override int GetHashCode() => HashCode.Combine(_high, _low, _version);

    /// <summary>Determines whether two values hold the same address.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values are equal; otherwise, <see langword="false"/>.</returns>
    public static bool operator ==(IpAddressValue left, IpAddressValue right) => left.Equals(right);

    /// <summary>Determines whether two values hold different addresses.</summary>
    /// <param name="left">The first value to compare.</param>
    /// <param name="right">The second value to compare.</param>
    /// <returns><see langword="true"/> if the values differ; otherwise, <see langword="false"/>.</returns>
    public static bool operator !=(IpAddressValue left, IpAddressValue right) => !left.Equals(right);

    private static IpAddressValue FromBytes(ReadOnlySpan<byte> address, uint scopeId) => address.Length switch {
        4 => new IpAddressValue(0, BinaryPrimitives.ReadUInt32BigEndian(address), 0, IpVersion.IPv4),
        16 => new IpAddressValue(BinaryPrimitives.ReadUInt64BigEndian(address),
            BinaryPrimitives.ReadUInt64BigEndian(address[8..]), scopeId, IpVersion.IPv6),
        _ => throw new ArgumentException("An IP address is 4 or 16 bytes long.", nameof(address))
    };
}
