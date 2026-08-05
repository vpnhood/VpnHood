namespace VpnHood.AppLib.Portal.Dto;

/// <summary>me.get response.</summary>
public class PortalMe
{
    public required string UserId { get; init; }
    public required PortalAccount Account { get; init; }
    public required string State { get; init; }
}
