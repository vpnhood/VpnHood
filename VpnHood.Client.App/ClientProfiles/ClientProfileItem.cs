using VpnHood.Common.Tokens;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfileItem(ClientProfile clientProfile)
{
    private ClientProfileInfo? _clientProfileInfo;
    public ClientProfile ClientProfile { get; } = clientProfile;
    public Guid ClientProfileId => ClientProfile.ClientProfileId;
    public Token Token => ClientProfile.Token;
    public ClientProfileBaseInfo BaseInfo => ClientProfile.ToBaseInfo();

    public ClientProfileInfo ClientProfileInfo {
        get {
            _clientProfileInfo ??= new ClientProfileInfo(ClientProfile);
            return _clientProfileInfo;
        }
    }

    internal void Refresh()
    {
        _clientProfileInfo = null;
    }
}