namespace VpnHood.Core.Server.Access.Managers.FileAccessManagers.Dtos;

// the cached copy: the store replaces a part once its file is written, so a holder sees it then
public class AccessTokenData
{
    public required AccessToken AccessToken { get; set; }
    public required AccessTokenUsage Usage { get; set; }
}