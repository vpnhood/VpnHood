using VpnHood.Common;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfile
{
    public required Guid ClientProfileId { get; set; }
    public required string? ClientProfileName { get; set; }
    public required Token Token { get; set; }
    public bool IsForAccount { get; set; }

    public ClientProfileInfo ToInfo()
    {
        return new ClientProfileInfo(this);
    }

    public ClientProfileBaseInfo ToBaseInfo()
    {
        return new ClientProfileBaseInfo(this);
    }
}