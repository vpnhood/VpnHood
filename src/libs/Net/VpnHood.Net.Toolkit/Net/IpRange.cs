using System.Net;
using System.Net.Sockets;
using System.Numerics;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Net;

// ReSharper disable PossibleMultipleEnumeration
/// <summary>
/// Represents a range of IP addresses of one family, from a first to a last address, both included.
/// </summary>
/// <remarks>
/// Its properties are read-only, and the addresses they return are its own, which a caller must not change. Its text
/// form is <c>10.0.0.1-10.0.0.9</c>, or the address alone for a range of one address, and JSON reads and writes a range
/// as that text, through <see cref="IpRangeConverter"/>.
/// <see cref="IpRangeOrderedList"/> holds a sorted set of ranges.
/// <para>
/// An IPv4-mapped IPv6 address, such as <c>::ffff:10.0.0.1</c>, is an IPv4 address in IPv6's form. A range of such
/// addresses is the range of their IPv4 addresses, and is made so: <c>::ffff:10.0.0.1-::ffff:10.0.0.9</c> is
/// <c>10.0.0.1-10.0.0.9</c>. A range that only reaches into them, as that of <c>::/80</c> does, is a range of IPv6
/// addresses like any other; and an address asked about is asked as its IPv4 address, which such a range does not hold.
/// </para>
/// </remarks>
[JsonConverter(typeof(IpRangeConverter))]
public class IpRange
{
    /// <summary>Initializes a new instance of the <see cref="IpRange"/> class that holds one address.</summary>
    /// <param name="ipAddress">The address.</param>
    public IpRange(IPAddress ipAddress)
        : this(ipAddress, ipAddress)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpRange"/> class from IPv4 addresses given as numbers, as
    /// <see cref="IPAddressUtil.ToLong"/> gives them: <c>10.0.0.1</c> is <c>0x0A000001</c>.
    /// </summary>
    /// <param name="firstIpAddress">The first address. Only its low 32 bits are read.</param>
    /// <param name="lastIpAddress">The last address. Only its low 32 bits are read.</param>
    /// <exception cref="ArgumentException">The last address is below the first.</exception>
    public IpRange(long firstIpAddress, long lastIpAddress)
        : this(IPAddressUtil.FromLong(firstIpAddress), IPAddressUtil.FromLong(lastIpAddress))
    {
    }

    /// <summary>Initializes a new instance of the <see cref="IpRange"/> class.</summary>
    /// <param name="firstIpAddress">The first address.</param>
    /// <param name="lastIpAddress">The last address, of the same family.</param>
    /// <exception cref="ArgumentException">
    /// The two addresses are of different families, or <paramref name="lastIpAddress"/> comes before
    /// <paramref name="firstIpAddress"/>.
    /// </exception>
    /// <remarks>
    /// Two IPv4-mapped IPv6 addresses make the range of their IPv4 addresses. Any other two are put in order by
    /// number, so a range from <c>::ffff:10.0.0.5</c> to <c>::1</c> is refused.
    /// </remarks>
    public IpRange(IPAddress firstIpAddress, IPAddress lastIpAddress)
    {
        if (firstIpAddress.AddressFamily != lastIpAddress.AddressFamily)
            throw new ArgumentException("Both ipAddress must have a same address family!");

        // Kept as IPv6, the range held no address a lookup asks about, as a mapped address is asked as IPv4
        if (firstIpAddress.IsIPv4MappedToIPv6 && lastIpAddress.IsIPv4MappedToIPv6) {
            firstIpAddress = firstIpAddress.MapToIPv4();
            lastIpAddress = lastIpAddress.MapToIPv4();
        }

        if (IPAddressUtil.CompareUnmapped(firstIpAddress, lastIpAddress) > 0)
            throw new ArgumentException(
                $"{nameof(lastIpAddress)} must be equal or greater than {nameof(firstIpAddress)}");

        // without their scope ids, which are no part of a range: with one, :: would not be the first address
        FirstIpAddress = IPAddressUtil.WithoutScopeId(firstIpAddress);
        LastIpAddress = IPAddressUtil.WithoutScopeId(lastIpAddress);
    }

    /// <summary>Creates a range that holds one address.</summary>
    /// <param name="ipAddress">The address.</param>
    /// <returns>The range.</returns>
    public static IpRange FromIpAddress(IPAddress ipAddress) => new(ipAddress);

    /// <summary>Gets the address family of the range's addresses.</summary>
    public AddressFamily AddressFamily => FirstIpAddress.AddressFamily;

    /// <summary>Gets the range's first address.</summary>
    public IPAddress FirstIpAddress { get; }

    /// <summary>Gets the range's last address.</summary>
    public IPAddress LastIpAddress { get; }

    /// <summary>Gets the number of addresses in the range.</summary>
    public BigInteger Total => new BigInteger(LastIpAddress.GetAddressBytesFast(stackalloc byte[16]), true, true) -
        new BigInteger(FirstIpAddress.GetAddressBytesFast(stackalloc byte[16]), true, true) + 1;

    /// <summary>Gets the fewest networks that hold exactly the range's addresses.</summary>
    /// <returns>The networks, from the lowest address up, made as the result is enumerated.</returns>
    public IEnumerable<IpNetwork> ToIpNetworks() => IpNetwork.FromRange(FirstIpAddress, LastIpAddress);

    /// <summary>Converts the text form of a range to an <see cref="IpRange"/>.</summary>
    /// <param name="value">
    /// An address (<c>10.0.0.1</c>); two addresses joined by <c>-</c> or <c>to</c> (<c>10.0.0.1-10.0.0.9</c>), white
    /// space around them allowed; or a network (<c>10.0.0.0/24</c>), as <see cref="IpNetwork.Parse"/> reads it.
    /// </param>
    /// <returns>The range.</returns>
    /// <exception cref="FormatException">
    /// <paramref name="value"/> is none of these forms, or an address in it is not valid.
    /// </exception>
    /// <exception cref="ArgumentException">
    /// The two addresses are of different families, or the second comes before the first.
    /// </exception>
    public static IpRange Parse(string value)
    {
        if (value.IndexOf('/') != -1)
            return IpNetwork.Parse(value).ToIpRange();

        var items = value.Replace("to", "-").Split('-');
        return items.Length switch {
            1 => new IpRange(ParseIpAddress(items[0].Trim())),
            2 => new IpRange(ParseIpAddress(items[0].Trim()), IPAddress.Parse(items[1].Trim())),
            _ => throw new FormatException($"Could not parse the IpRange from: {value}")
        };
    }

    private static IPAddress ParseIpAddress(string value)
    {
        if (IPAddress.TryParse(value, out var ipAddress))
            return ipAddress;

        var ex = new FormatException($"Could not parse the IpAddress from: {value}");
        ex.Data.Add("IpAddress", value);
        throw ex;
    }

    /// <summary>
    /// Returns the range's text form: <c>10.0.0.1-10.0.0.9</c>, or the address alone when the range holds one address.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return FirstIpAddress.Equals(LastIpAddress)
            ? $"{FirstIpAddress}"
            : $"{FirstIpAddress}-{LastIpAddress}";
    }

    /// <inheritdoc/>
    /// <remarks>Two ranges are equal when their ends are, as <see cref="IPAddress.Equals(object)"/> compares them.</remarks>
    public override bool Equals(object? obj)
    {
        return obj is IpRange ipRange &&
               FirstIpAddress.Equals(ipRange.FirstIpAddress) &&
               LastIpAddress.Equals(ipRange.LastIpAddress);
    }

    /// <inheritdoc/>
    public override int GetHashCode()
    {
        return HashCode.Combine(FirstIpAddress, LastIpAddress);
    }

    /// <summary>Determines whether an address is in the range.</summary>
    /// <param name="ipAddress">
    /// The address. An IPv4-mapped IPv6 address is asked about as its IPv4 address, and a scope id is ignored.
    /// </param>
    /// <returns><see langword="true"/> if the range holds the address; otherwise, <see langword="false"/>.</returns>
    /// <exception cref="ArgumentNullException"><paramref name="ipAddress"/> is <see langword="null"/>.</exception>
    public bool IsInRange(IPAddress ipAddress) => IsInRange(ipAddress.ToValue());

    /// <summary>Determines whether an address is in the range, with no object made.</summary>
    /// <param name="ipAddress">
    /// The address. An IPv4-mapped IPv6 address is asked about as its IPv4 address, and a scope id is ignored; the
    /// default value is in no range.
    /// </param>
    /// <returns><see langword="true"/> if the range holds the address; otherwise, <see langword="false"/>.</returns>
    public bool IsInRange(IpAddressValue ipAddress)
    {
        var value = ipAddress.IsIPv4MappedToIPv6 ? ipAddress.MapToIPv4() : ipAddress;
        return value.CompareAddress(FirstIpAddress.ToValue()) >= 0 &&
               value.CompareAddress(LastIpAddress.ToValue()) <= 0;
    }
}