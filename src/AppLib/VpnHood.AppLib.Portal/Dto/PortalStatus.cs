namespace VpnHood.AppLib.Portal.Dto;

/// <summary>GET /system/status response.</summary>
public class PortalStatus
{
    public required string Status { get; init; }

    /// <summary>The contract version the portal serves (openapi.json info.version).</summary>
    public required string Api { get; init; }

    /// <summary>Server time (UTC), for clock-skew checks.</summary>
    public DateTime? Time { get; init; }
}
