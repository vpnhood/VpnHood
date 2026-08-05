namespace VpnHood.AppLib.Portal.Dto;

/// <summary>auth.token response.</summary>
public class PortalSignInResponse
{
    public required string AccessToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public required string UserId { get; init; }
    public required PortalAccount Account { get; init; }
    public required string State { get; init; }
}
