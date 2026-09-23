namespace VpnHood.AppLib.Portal.Dto;

/// <summary>A WebAuthn options document from the portal — consumed by the device API verbatim.</summary>
public class PortalRestoreCredentialOptions
{
    public required string RequestJson { get; init; }
}
