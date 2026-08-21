using VpnHood.Core.Toolkit.Utils;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientProfileUpdateParams
{
    public Patch<string?>? ClientProfileName { get; set; }
    public Patch<bool>? IsFavorite { get; set; }
    public Patch<string?>? SelectedLocation { get; set; }
    public Patch<string?>? CustomData { get; set; }
    public Patch<bool>? IsPremiumLocationSelected { get; set; }
    /// <summary>
    /// A code somebody typed HERE. It owes the account an upload until one lands, which is why
    /// nothing on this patch can say otherwise — see
    /// <see cref="ClientProfileService.SetAccountAccessCode" /> for the account's side.
    /// </summary>
    public Patch<string?>? AccessCode { get; set; }
    public Patch<string[]?>? CustomServerEndpoints { get; set; }
    public Patch<bool>? IsCustomServerEndpointsEnabled { get; set; }
}