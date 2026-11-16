using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

/// <summary>
/// Provides the version of the contract between an app and its VPN engine: the requests, options and files they share.
/// </summary>
/// <remarks>
/// The version goes up only for a change that is not growth: a request, field or file removed or renamed, or one that
/// means something else. See "The VPN service contract" in <c>docs/client-architecture.md</c>.
/// </remarks>
public static class VpnServiceContract
{
    /// <summary>The newest version this library speaks: 1.</summary>
    public const int Version = 1;

    /// <summary>The oldest version this library still speaks: 1.</summary>
    public const int MinVersion = 1;

    /// <summary>
    /// Finds the highest version this library and an engine both speak, which the app writes in the options
    /// (<see cref="VpnServiceOptions.ContractVersion"/>).
    /// </summary>
    /// <param name="vpnEngineInfo">The engine's report of itself.</param>
    /// <returns>The highest version both speak.</returns>
    /// <exception cref="VpnServiceContractException">The two share no version.</exception>
    public static int Negotiate(VpnEngineInfo vpnEngineInfo)
    {
        var version = Math.Min(Version, vpnEngineInfo.MaxContractVersion);
        if (version < Math.Max(MinVersion, vpnEngineInfo.MinContractVersion))
            throw new VpnServiceContractException(
                $"This app speaks the VPN service contract's versions {MinVersion} to {Version}; its VPN engine, " +
                $"{vpnEngineInfo.Name} {vpnEngineInfo.Version}, speaks {vpnEngineInfo.MinContractVersion} to " +
                $"{vpnEngineInfo.MaxContractVersion}.");

        return version;
    }

    /// <summary>
    /// Checks that this library speaks the version options were written in, as an engine does before it reads them,
    /// rather than reading them as another.
    /// </summary>
    /// <param name="contractVersion">The options' version.</param>
    /// <exception cref="VpnServiceContractException">This library does not speak the version.</exception>
    public static void Verify(int contractVersion)
    {
        if (contractVersion is < MinVersion or > Version)
            throw new VpnServiceContractException(
                $"The app speaks the VPN service contract's version {contractVersion}; this VPN engine speaks " +
                $"{MinVersion} to {Version}.");
    }
}
