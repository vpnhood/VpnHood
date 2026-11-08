using System.Net;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>Represents an IP endpoint as a value: an address and a port, compared by value.</summary>
/// <param name="Address">The address.</param>
/// <param name="Port">The port.</param>
public readonly record struct IpEndPointValue(IPAddress Address, int Port)
{
    /// <summary>Converts the value to an <see cref="IPEndPoint"/>.</summary>
    /// <returns>A new endpoint.</returns>
    /// <exception cref="ArgumentNullException">The address is <see langword="null"/>.</exception>
    /// <exception cref="ArgumentOutOfRangeException">The port is out of range.</exception>
    // ReSharper disable once InconsistentNaming
    public IPEndPoint ToIPEndPoint() => new(Address, Port);
}