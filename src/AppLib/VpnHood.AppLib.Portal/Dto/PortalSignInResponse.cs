namespace VpnHood.AppLib.Portal.Dto;

/// <summary>
/// POST /auth/sessions response. Either a session (AccessToken set) or, for the password form when
/// a second factor is due, only <see cref="Challenge"/> — the fields are exclusive.
/// </summary>
public class PortalSignInResponse
{
    public string? AccessToken { get; init; }
    public DateTime? ExpiresAt { get; init; }
    public string? UserId { get; init; }

    /// <summary>Password form only: the second factor due before any session exists.</summary>
    public PortalSignInChallenge? Challenge { get; init; }

    /// <summary>Only after a challenge completion that spent the backup code: the replacement, shown once.</summary>
    public string? NewBackupCode { get; init; }
}
