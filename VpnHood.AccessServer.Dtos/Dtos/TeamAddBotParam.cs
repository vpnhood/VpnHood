using System;

namespace VpnHood.AccessServer.Dtos;

public class TeamAddBotParam
{
    public required string Name { get; init; }
    public required Guid RoleId { get; init; }
}