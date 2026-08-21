namespace VpnHood.AppLib.ClientProfiles;

public static class ClientProfileExtensions
{
    public static ClientProfileInfo ToInfo(this ClientProfile clientProfile, AppFeatures appFeatures)
    {
        return new ClientProfileInfo(clientProfile, appFeatures);
    }

    public static ClientProfileBaseInfo ToBaseInfo(this ClientProfileInfo clientProfileInfo)
    {
        return new ClientProfileBaseInfo {
            ClientProfileId = clientProfileInfo.ClientProfileId,
            ClientProfileName = clientProfileInfo.ClientProfileName,
            SupportId = clientProfileInfo.SupportId,
            CustomData = clientProfileInfo.CustomData,
            IsPremiumLocationSelected = clientProfileInfo.IsPremiumLocationSelected,
            AccessCodeRefusal = clientProfileInfo.AccessCodeRefusal,
            IsPremium = clientProfileInfo.IsPremium,
            SelectedLocationInfo = clientProfileInfo.SelectedLocationInfo,
            HasAccessCode = !string.IsNullOrEmpty(clientProfileInfo.AccessCode),
            CustomServerEndpoints = clientProfileInfo.CustomServerEndpoints,
            IsCustomServerEndpointsEnabled = clientProfileInfo.IsCustomServerEndpointsEnabled,
            CanGoPremium = clientProfileInfo.CanGoPremium,
            CanTryPremium = clientProfileInfo.CanTryPremium,
            CanImportAccessCode = clientProfileInfo.CanImportAccessCode,
            CanViewAccessCode = clientProfileInfo.CanViewAccessCode
        };
    }
}