namespace VpnHood.AppLib.Portal.Dto;

/// <summary>The persisted session (like the Store package's apiKey.json).</summary>
public class PortalSession
{
    public required string AccessToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public required string UserId { get; init; }
    public required string Email { get; init; }
}
