using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Net;

/// <summary>Represents a range of ports, from a first to a last port, both included.</summary>
/// <remarks>
/// Its text form is <c>8000-8100</c>, or the port alone for a range of one port, and JSON reads and writes a range as
/// that text, through <see cref="PortRangeConverter"/>. Two instances of <see cref="PortRange"/> itself are equal when
/// their first and last ports are.
/// </remarks>
[JsonConverter(typeof(PortRangeConverter))]
public record PortRange
{
    /// <summary>Initializes a new instance of the <see cref="PortRange"/> class that holds one port.</summary>
    /// <param name="port">The port, from 0 to 65535.</param>
    /// <exception cref="ArgumentOutOfRangeException"><paramref name="port"/> is below 0 or above 65535.</exception>
    public PortRange(int port)
        : this(port, port)
    {
    }

    /// <summary>Initializes a new instance of the <see cref="PortRange"/> class.</summary>
    /// <param name="firstPort">The first port, from 0 to 65535.</param>
    /// <param name="lastPort">The last port, from <paramref name="firstPort"/> to 65535.</param>
    /// <exception cref="ArgumentOutOfRangeException">
    /// <paramref name="firstPort"/> is below 0, <paramref name="lastPort"/> is above 65535, or
    /// <paramref name="firstPort"/> is above <paramref name="lastPort"/>.
    /// </exception>
    public PortRange(int firstPort, int lastPort)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(firstPort, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lastPort, IPEndPoint.MaxPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstPort, lastPort);

        FirstPort = firstPort;
        LastPort = lastPort;
    }

    /// <summary>Gets the range's first port.</summary>
    public int FirstPort { get; }

    /// <summary>Gets the range's last port.</summary>
    public int LastPort { get; }

    /// <summary>Determines whether a port is in the range.</summary>
    /// <param name="port">The port.</param>
    /// <returns><see langword="true"/> if the range holds the port; otherwise, <see langword="false"/>.</returns>
    public bool IsInRange(int port)
    {
        return port >= FirstPort && port <= LastPort;
    }

    /// <summary>Converts the text form of a range, such as <c>8000-8100</c>, to a <see cref="PortRange"/>.</summary>
    /// <param name="value">
    /// A port (<c>443</c>) or two ports joined by <c>-</c> (<c>8000-8100</c>), in decimal digits; white space around a
    /// port is allowed.
    /// </param>
    /// <returns>The range.</returns>
    /// <exception cref="FormatException">
    /// <paramref name="value"/> is neither form, or a port is not decimal digits or is above <see cref="int.MaxValue"/>.
    /// </exception>
    /// <exception cref="ArgumentOutOfRangeException">
    /// A port is from 65536 to <see cref="int.MaxValue"/>, or the first is above the last.
    /// </exception>
    public static PortRange Parse(string value)
    {
        var items = value.Split('-');
        return items.Length switch {
            1 => new PortRange(ParsePort(items[0])),
            2 => new PortRange(ParsePort(items[0]), ParsePort(items[1])),
            _ => throw new FormatException($"Could not parse the PortRange from: {value}")
        };
    }

    private static int ParsePort(string value)
    {
        return int.TryParse(value.Trim(), NumberStyles.None, CultureInfo.InvariantCulture, out var port)
            ? port
            : throw new FormatException($"Could not parse the port from: {value}");
    }

    /// <summary>
    /// Returns the range's text form: <c>8000-8100</c>, or the port alone when the range holds one port.
    /// </summary>
    /// <returns>The text.</returns>
    public override string ToString()
    {
        return FirstPort == LastPort
            ? $"{FirstPort}"
            : $"{FirstPort}-{LastPort}";
    }
}
