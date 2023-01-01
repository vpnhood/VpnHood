namespace VpnHood.AccessServer.Models;

public class AccessTokenModel
{
    public Guid ProjectId { get; set; }
    public Guid AccessTokenId { get; set; }
    public string? AccessTokenName { get; set; }
    public int SupportCode { get; set; }
    public byte[] Secret { get; set; } = null!;
    public Guid AccessPointGroupId { get; set; }
    public long MaxTraffic { get; set; }
    public int MaxDevice { get; set; }
    public string? Url { get; set; }
    public bool IsPublic { get; set; }
    public bool IsEnabled { get; set; } = true;
    public int Lifetime { get; set; }
    public DateTime? ExpirationTime { get; set; }
    public DateTime? FirstUsedTime { get; set; }
    public DateTime? LastUsedTime { get; set; }
    public DateTime CreatedTime { get; set; }
    public DateTime ModifiedTime { get; set; }
    public bool IsDeleted { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual AccessPointGroupModel? AccessPointGroup { get; set; }
}