namespace VpnHood.AccessServer.Dtos.ServerProfile;

public class ServerProfileData
{
    public required ServerProfile ServerProfile { get; init; }
    public ServerProfileSummary? Summary { get; set; }
}
