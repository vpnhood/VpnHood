namespace VpnHood.AccessServer.Models;

public class ServerProfileModel
{
    public string ServerProfileName { get; set; } = default!;
    public Guid ServerProfileId { get; set; }
    public Guid ProjectId { get; set; }
    public string? ServerConfig { get; set; }
    public bool IsDefault { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime CreatedTime { get; set; }

    public virtual ProjectModel? Project { get; set; }
    public virtual ICollection<ServerFarmModel>? ServerFarms { get; set; }
}