using System.Net;
using System.Text;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>Represents an IP endpoint as a value: an address and a port, compared by value.</summary>
/// <remarks>
/// The address is held as an <see cref="IpAddressValue"/>, with no object on the heap, and <see cref="Address"/> makes
/// an <see cref="IPAddress"/> from it on each read. Two endpoints are equal when their ports are and their addresses
/// are, as <see cref="IPAddress.Equals(object)"/> compares them. The default value holds no address.
/// </remarks>
public readonly record struct IpEndPointValue
{
    /// <summary>
    /// Initializes a new instance of the <see cref="IpEndPointValue"/> structure from an address and a port.
    /// </summary>
    /// <param name="address">The address.</param>
    /// <param name="port">The port.</param>
    /// <exception cref="ArgumentNullException"><paramref name="address"/> is <see langword="null"/>.</exception>
    public IpEndPointValue(IPAddress address, int port)
    {
        ArgumentNullException.ThrowIfNull(address);
        AddressValue = address.ToValue();
        Port = port;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="IpEndPointValue"/> structure from an address value and a port.
    /// </summary>
    /// <param name="addressValue">The address.</param>
    /// <param name="port">The port.</param>
    public IpEndPointValue(IpAddressValue addressValue, int port)
    {
        AddressValue = addressValue;
        Port = port;
    }

    /// <summary>Gets the address as a value.</summary>
    public IpAddressValue AddressValue { get; init; }

    /// <summary>Gets the address.</summary>
    /// <remarks>Each read makes a new <see cref="IPAddress"/>; code that runs per packet reads
    /// <see cref="AddressValue"/>.</remarks>
    /// <exception cref="InvalidOperationException">The endpoint is the default value, which holds no address.</exception>
    /// <exception cref="ArgumentNullException">The address set is <see langword="null"/>.</exception>
    public IPAddress Address {
        get => AddressValue.ToIPAddress();
        init {
            ArgumentNullException.ThrowIfNull(value);
            AddressValue = value.ToValue();
        }
    }

    /// <summary>Gets the port.</summary>
    public int Port { get; init; }

    /// <summary>Deconstructs the endpoint into its address and port.</summary>
    /// <param name="address">When this method returns, the address.</param>
    /// <param name="port">When this method returns, the port.</param>
    /// <exception cref="InvalidOperationException">The endpoint is the default value, which holds no address.</exception>
    public void Deconstruct(out IPAddress address, out int port)
    {
        address = Address;
        port = Port;
    }

    /// <summary>Converts the value to an <see cref="IPEndPoint"/>.</summary>
    /// <returns>A new endpoint.</returns>
    /// <exception cref="InvalidOperationException">The endpoint is the default value, which holds no address.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The port is out of range.</exception>
    // ReSharper disable once InconsistentNaming
    public IPEndPoint ToIPEndPoint() => new(Address, Port);

    // Prints Address and Port only: AddressValue is the same address again.
    private bool PrintMembers(StringBuilder builder)
    {
        builder.Append("Address = ").Append(AddressValue).Append(", Port = ").Append(Port);
        return true;
    }
}
