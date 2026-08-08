namespace VpnHood.AppLib.Portal.Dto;

/// <summary>GET /account response.</summary>
public class PortalAccountInfo
{
    public required string UserId { get; init; }
    public required PortalAccount Account { get; init; }
}
