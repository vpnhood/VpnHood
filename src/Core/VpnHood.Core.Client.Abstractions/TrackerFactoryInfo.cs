using System.Text.Json;

namespace VpnHood.Core.Client.Abstractions;

// A tracker factory as the VPN service receives it: its type, and its settings - the factory's public
// properties - as JSON.
public class TrackerFactoryInfo
{
    public required string AssemblyQualifiedName { get; init; }
    public required JsonElement Settings { get; init; }
}
