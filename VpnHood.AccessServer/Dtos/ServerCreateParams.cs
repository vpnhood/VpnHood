using System;

namespace VpnHood.AccessServer.Dtos;

public class ServerCreateParams
{
    public string? ServerName { get; set; }
    public Guid? AccessPointGroupId { get; set; }
}