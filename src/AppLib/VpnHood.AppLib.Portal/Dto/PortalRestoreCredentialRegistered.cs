namespace VpnHood.AppLib.Portal.Dto;

/// <summary>The portal's answer to a restore-key registration.</summary>
public class PortalRestoreCredentialRegistered
{
    /// <summary>The credential's handle (base64url); sign-out retires it server-side.</summary>
    public required string CredentialId { get; init; }
}
