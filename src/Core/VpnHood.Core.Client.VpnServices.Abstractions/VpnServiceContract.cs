using VpnHood.Core.Client.VpnServices.Abstractions.Exceptions;

namespace VpnHood.Core.Client.VpnServices.Abstractions;

// The version of the contract between an app and its VPN engine (docs/client-architecture.md, "The VPN
// service contract"). It goes up only for a change that is not growth - a request, field or file removed or
// renamed, or one that means something else; MinVersion is the oldest this library still speaks.
public static class VpnServiceContract
{
    public const int Version = 1;
    public const int MinVersion = 1;

    // The highest version this library and the engine both speak, which the app writes in the options.
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

    // An engine refuses options of a version it does not speak, rather than reading them as another.
    public static void Verify(int contractVersion)
    {
        if (contractVersion is < MinVersion or > Version)
            throw new VpnServiceContractException(
                $"The app speaks the VPN service contract's version {contractVersion}; this VPN engine speaks " +
                $"{MinVersion} to {Version}.");
    }
}
