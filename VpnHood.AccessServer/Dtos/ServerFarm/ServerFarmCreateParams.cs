namespace VpnHood.AccessServer.Dtos.ServerFarm;

public class ServerFarmCreateParams
{
    public string? ServerFarmName { get; set; }
    public Guid? ServerProfileId { get; set; }
    public bool UseHostName { get; set; }
    public Uri? TokenUrl { get; set; }
    public bool PushTokenToClient { get; init; }
}