namespace VpnHood.AppLib.Portal.Dto;

/// <summary>The persisted session (like the Store package's apiKey.json).</summary>
public class PortalSession
{
    public required string AccessToken { get; init; }
    public required string UserId { get; init; }
    public DateTime? ExpiresAt { get; init; }

    /// <summary>The method id that established this session; renewal and sign-out target that provider.</summary>
    public required string ProviderId { get; init; }
}
