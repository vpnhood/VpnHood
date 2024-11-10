namespace VpnHood.Server.Access.Managers.FileAccessManagers.Dtos;

public class AccessTokenData
{
    public required AccessToken AccessToken { get; init; }
    public required AccessTokenUsage Usage { get; init; }
}
