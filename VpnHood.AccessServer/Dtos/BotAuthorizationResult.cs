using VpnHood.AccessServer.Dtos.UserDtos;

namespace VpnHood.AccessServer.Dtos;

public class BotAuthorizationResult
{
    public required UserRole UserRole { get; set; }
    public required string Authorization { get; init; }
}