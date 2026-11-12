namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Represents what a VPN engine says of itself: the contract versions it speaks and the optional features it has.
/// </summary>
/// <remarks>
/// The head that bundles an engine declares it (<c>AppOptions.VpnEngineInfo</c>), so the app offers only what the
/// engine does before it ever runs, and the engine reports the same in its status
/// (<see cref="ConnectionInfo.VpnEngineInfo"/>).
/// </remarks>
public class VpnEngineInfo
{
    /// <summary>Gets the engine's name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the engine's version, as text.</summary>
    public required string Version { get; init; }

    /// <summary>Gets the oldest contract version the engine speaks (<see cref="VpnServiceContract"/>).</summary>
    public required int MinContractVersion { get; init; }

    /// <summary>Gets the newest contract version the engine speaks.</summary>
    public required int MaxContractVersion { get; init; }

    /// <summary>Gets the optional features the engine has.</summary>
    public required VpnEngineFeatures Features { get; init; }

    /// <summary>
    /// Gets this library's own engine: VpnHood, at this assembly's version, with every feature and the contract
    /// versions this library speaks.
    /// </summary>
    public static VpnEngineInfo VpnHoodEngine { get; } = new() {
        Name = "VpnHood",
        Version = typeof(VpnEngineInfo).Assembly.GetName().Version?.ToString(3) ?? "0.0.0",
        MinContractVersion = VpnServiceContract.MinVersion,
        MaxContractVersion = VpnServiceContract.Version,
        Features = VpnEngineFeatures.All
    };
}
