using VpnHood.Common.Tokens;

namespace VpnHood.Client.App.ClientProfiles;

public class ClientProfile
{
    public required Guid ClientProfileId { get; init; }
    public required string? ClientProfileName { get; set; }
    public required Token Token { get; set; }
    public bool IsFavorite { get; set; }
    public string? CustomData { get; set; }
    public bool IsPremiumLocationSelected { get; set; }
    public string? SelectedLocation{ get; set; }
    public bool IsForAccount { get; set; }
    public bool IsBuiltIn { get; set; }
}