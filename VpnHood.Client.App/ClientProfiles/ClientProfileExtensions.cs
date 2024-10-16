namespace VpnHood.Client.App.ClientProfiles;

public static class ClientProfileExtensions
{
    public static ClientProfileBaseInfo ToBaseInfo(this ClientProfile clientProfile)
    {
        return new ClientProfileBaseInfo(clientProfile);
    }
}