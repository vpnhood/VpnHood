namespace VpnHood.AppLib.ClientProfiles;

public static class ClientProfileExtensions
{
    public static ClientProfileInfo ToInfo(this ClientProfile clientProfile)
    {
        return new ClientProfileInfo(clientProfile);
    }

    public static ClientProfileBaseInfo ToBaseInfo(this ClientProfileInfo clientProfileInfo)
    {
        return new ClientProfileBaseInfo {
            ClientProfileId = clientProfileInfo.ClientProfileId,
            ClientProfileName = clientProfileInfo.ClientProfileName,
            SupportId = clientProfileInfo.SupportId,
            CustomData = clientProfileInfo.CustomData,
            IsPremiumLocationSelected = clientProfileInfo.IsPremiumLocationSelected,
            IsPremiumAccount = clientProfileInfo.IsPremiumAccount,
            SelectedLocationInfo = clientProfileInfo.SelectedLocationInfo,
            HasAccessCode = !string.IsNullOrEmpty(clientProfileInfo.AccessCode),
            CustomServerEndpoints = clientProfileInfo.CustomServerEndpoints
        };
    }
}