using VpnHood.Common.Utils;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfileUpdateParams
{
    public Patch<string?>? ClientProfileName { get; set; }
    public Patch<bool>? IsFavorite { get; set; }
    public Patch<string?>? LastPlan { get; set; }
}