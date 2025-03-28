using VpnHood.Core.Common.Tokens;

namespace VpnHood.AppLib.ClientProfiles;

public class ClientProfileBaseInfo
{
    public required Guid ClientProfileId { get; init; }
    public required string ClientProfileName { get; init; }
    public required string? SupportId { get; init; }
    public required string? CustomData { get; init; }
    public required bool IsPremiumLocationSelected { get; init; }
    public required bool IsPremiumAccount { get; init; }
    public required ClientServerLocationInfo? SelectedLocationInfo { get; init; }
    public required bool HasAccessCode { get; set; }
    public required Uri? PurchaseUrl { get; init; }
    public required PurchaseUrlMode PurchaseUrlMode { get; init; }

}