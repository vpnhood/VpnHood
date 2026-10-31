using System.Globalization;
using System.Net;
using System.Text.Json.Serialization;

namespace VpnHood.Net.Toolkit.Net;

[JsonConverter(typeof(PortRangeConverter))]
public record PortRange
{
    public PortRange(int port)
        : this(port, port)
    {
    }

    public PortRange(int firstPort, int lastPort)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(firstPort, IPEndPoint.MinPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(lastPort, IPEndPoint.MaxPort);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(firstPort, lastPort);

        FirstPort = firstPort;
        LastPort = lastPort;
    }

    public int FirstPort { get; }
    public int LastPort { get; }

    public bool IsInRange(int port)
    {
        return port >= FirstPort && port <= LastPort;
    }

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

    public override string ToString()
    {
        return FirstPort == LastPort
            ? $"{FirstPort}"
            : $"{FirstPort}-{LastPort}";
    }
}
