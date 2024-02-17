namespace VpnHood.AccessServer.Persistence.Models;

public class LetsEncryptAccount
{
    public int LetsEncryptAccountId { get; init; }
    public Guid ProjectId { get; set; }
    public required string AccountPem { get; set; }
    
    public virtual ProjectModel? Project { get; set; }
}