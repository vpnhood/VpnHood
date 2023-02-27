namespace VpnHood.AccessServer.Models;

public class ServerProfileModel
{
    public Guid ServerProfileId { get; set; }
    public Guid ProjectId { get; set; }
    public string? ServerConfig { get; set; }

    public virtual ICollection<ServerFarmModel>? ServerFarms { get; set; }

}