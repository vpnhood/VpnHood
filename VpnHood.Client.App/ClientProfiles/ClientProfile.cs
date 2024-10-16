using VpnHood.Common;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfile
{
    public required Guid ClientProfileId { get; init; }
    public required string? ClientProfileName { get; set; }
    public required Token Token { get; set; }
    public bool IsFavorite { get; set; }
    public bool IsForAccount { get; set; }
    public bool IsBuiltIn { get; set; }

}