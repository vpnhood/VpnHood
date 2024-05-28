namespace VpnHood.AccessServer.Dtos.ServerFarms;

public class ServerFarmCreateParams
{
    public string? ServerFarmName { get; set; }
    public Guid? ServerProfileId { get; set; }
    public Uri? TokenUrl { get; set; }
}