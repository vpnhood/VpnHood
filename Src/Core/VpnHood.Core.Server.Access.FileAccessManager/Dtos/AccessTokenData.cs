namespace VpnHood.Core.Server.Access.Managers.FileAccessManagement.Dtos;

public class AccessTokenData
{
    public required AccessToken AccessToken { get; init; }
    public required AccessTokenUsage Usage { get; init; }
}