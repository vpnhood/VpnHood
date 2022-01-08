using System;

namespace VpnHood.AccessServer.DTOs;

public class ServerUpdateParams
{
    public Patch<string>? ServerName { get; set; }
    public Patch<Guid?>? AccessPointGroupId { get; set; }
    public Patch<bool>? GenerateNewSecret { get; set; }
}