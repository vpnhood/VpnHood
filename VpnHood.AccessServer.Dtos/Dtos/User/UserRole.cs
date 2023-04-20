using System;

namespace VpnHood.AccessServer.Dtos;

public class UserRole
{
    public required Guid UserId { get; init; }
    public required User? User { get; init; }
    public required Role Role { get; init; }
    public required string ResourceId { get; init; }
}