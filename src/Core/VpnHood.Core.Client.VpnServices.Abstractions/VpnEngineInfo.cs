namespace VpnHood.Core.Client.VpnServices.Abstractions;

// What a VPN engine says of itself: the contract versions it speaks and the optional features it has. The head
// that bundles an engine declares it (AppOptions.VpnEngineInfo), so the app offers only what the engine does
// before it ever runs, and the engine reports the same in its status (ConnectionInfo.VpnEngineInfo).
public class VpnEngineInfo
{
    public required string Name { get; init; }
    public required string Version { get; init; }
    public required int MinContractVersion { get; init; }
    public required int MaxContractVersion { get; init; }
    public required VpnEngineFeatures Features { get; init; }

    // This library's own engine: every feature, and the versions this library speaks.
    public static VpnEngineInfo VpnHoodEngine { get; } = new() {
        Name = "VpnHood",
        Version = typeof(VpnEngineInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        MinContractVersion = VpnServiceContract.MinVersion,
        MaxContractVersion = VpnServiceContract.Version,
        Features = VpnEngineFeatures.All
    };
}
