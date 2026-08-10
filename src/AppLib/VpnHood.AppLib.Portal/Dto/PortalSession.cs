namespace VpnHood.AppLib.Portal.Dto;

/// <summary>The persisted session (like the Store package's apiKey.json).</summary>
public class PortalSession
{
    public required string AccessToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public required string UserId { get; init; }
    public required string Email { get; init; }

    /// <summary>The method id that established this session; renewal and sign-out target that provider.
    /// Null in files persisted before this field existed.</summary>
    public string? SignInMethod { get; init; }
}
