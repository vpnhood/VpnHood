
using VpnHood.AppLib.Api.App;
using VpnHood.AppLib.Api.VpnProfiles;

namespace VpnHood.AppLib.App.VpnProfiles;

public static class VpnProfileExtensions
{
    public static VpnProfileInfo ToInfo(this VpnProfile vpnProfile, AppFeatures appFeatures)
    {
        return VpnProfileInfoBuilder.Build(vpnProfile, appFeatures);
    }

    public static VpnProfileBaseInfo ToBaseInfo(this VpnProfileInfo vpnProfileInfo)
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