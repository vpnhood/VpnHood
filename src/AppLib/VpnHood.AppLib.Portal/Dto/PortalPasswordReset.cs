namespace VpnHood.AppLib.Portal.Dto;

/// <summary>
/// The portal website's page for a forgotten password, as that website builds it: navigation data
/// for the password form's escape hatch, opened in the system browser, never called.
/// </summary>
public class PortalPasswordReset
{
    public required Uri Url { get; init; }
}
