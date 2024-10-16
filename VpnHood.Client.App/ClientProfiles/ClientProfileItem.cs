using VpnHood.Common;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfileItem(ClientProfile clientProfile, string clientCountry)
{
    private ClientProfileInfo? _clientProfileInfo;
    private string _clientCountry = clientCountry;
    public ClientProfile ClientProfile { get; } = clientProfile;
    public Guid ClientProfileId => ClientProfile.ClientProfileId;
    public Token Token => ClientProfile.Token;
    public ClientProfileBaseInfo BaseInfo => ClientProfile.ToBaseInfo();

    public ClientProfileInfo ClientProfileInfo {
        get {
            _clientProfileInfo ??= new ClientProfileInfo(ClientProfile, _clientCountry);
            return _clientProfileInfo;
        }
    }

    internal void Refresh(string clientCountry)
    {
        _clientCountry = clientCountry;
        _clientProfileInfo = null;
    }
}