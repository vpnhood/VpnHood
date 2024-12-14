using VpnHood.Common.Utils;

namespace VpnHood.AppFramework.ClientProfiles;

public class ClientProfileUpdateParams
{
    public Patch<string?>? ClientProfileName { get; set; }
    public Patch<bool>? IsFavorite { get; set; }
    public Patch<string?>? SelectedLocation { get; set; }
    public Patch<string?>? CustomData { get; set; }
    public Patch<bool>? IsPremiumLocationSelected { get; set; }
    public Patch<string>? AccessCode { get; set; }
}