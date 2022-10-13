using System;

namespace VpnHood.AccessServer.DTOs;

public class AccessTokenUpdateParams
{
    public Patch<string>? AccessTokenName { get; set; }

    public Patch<Guid>? AccessPointGroupId { get; set; }

    public Patch<DateTime?>? EndTime { get; set; }

    public Patch<int>? Lifetime { get; set; }

    public Patch<int>? MaxDevice { get; set; }

    public Patch<long>? MaxTraffic { get; set; }

    public Patch<string>? Url { get; set; }
}