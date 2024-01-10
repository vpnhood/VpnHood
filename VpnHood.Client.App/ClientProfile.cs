using System.Text.Json.Serialization;
using VpnHood.Common;
using VpnHood.Common.Utils;

namespace VpnHood.Client.App;

public class ClientProfile
{
    public required Guid ClientProfileId { get; set; }
    public required string? ClientProfileName { get; set; }
    public required Token Token { get; set; }

    public ClientProfileInfo ToInfo()
    {
        return new ClientProfileInfo(this);
    }
}