using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.ClientProfiles;

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
    public bool IsPremium => !Token.IsPublic || Access?.IsPremium == true;
    public string? AccessCode { get; set; }
    public ClientProfileAccess? Access { get; set; }
}