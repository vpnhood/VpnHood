
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.VpnProfiles;

namespace VpnHood.AppLib.App.VpnProfiles;

/// <summary>Provides extension methods for <see cref="VpnProfile"/>.</summary>
public static class VpnProfileExtensions
{
    /// <summary>Returns a VPN profile as the API carries it, with what this build offers on it.</summary>
    /// <param name="vpnProfile">The profile.</param>
    /// <param name="appDescriptor">What this build offers (<see cref="VpnHoodApp.Descriptor"/>).</param>
    /// <returns>The profile's information.</returns>
    public static VpnProfileInfo ToInfo(this VpnProfile vpnProfile, AppDescriptor appDescriptor)
    {
        return VpnProfileInfoBuilder.Build(vpnProfile, appDescriptor);
    }

    internal static VpnProfileBaseInfo ToBaseInfo(this VpnProfileInfo vpnProfileInfo)
    {
        return new VpnProfileBaseInfo {
            VpnProfileId = vpnProfileInfo.VpnProfileId,
            VpnProfileName = vpnProfileInfo.VpnProfileName,
            SupportId = vpnProfileInfo.SupportId,
            CustomData = vpnProfileInfo.CustomData,
            IsPremiumLocationSelected = vpnProfileInfo.IsPremiumLocationSelected,
            AccessCodeRefusal = vpnProfileInfo.AccessCodeRefusal,
            IsPremium = vpnProfileInfo.IsPremium,
            SelectedLocationInfo = vpnProfileInfo.SelectedLocationInfo,
            HasAccessCode = !string.IsNullOrEmpty(vpnProfileInfo.AccessCode),
            CustomServerEndpoints = vpnProfileInfo.CustomServerEndpoints,
            IsCustomServerEndpointsEnabled = vpnProfileInfo.IsCustomServerEndpointsEnabled,
            CanGoPremium = vpnProfileInfo.CanGoPremium,
            CanTryPremium = vpnProfileInfo.CanTryPremium,
            CanImportAccessCode = vpnProfileInfo.CanImportAccessCode,
            CanViewAccessCode = vpnProfileInfo.CanViewAccessCode
        };
    }
}