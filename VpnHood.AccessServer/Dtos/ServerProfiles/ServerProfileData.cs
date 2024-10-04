namespace VpnHood.AccessServer.Dtos.ServerProfiles;

public class ServerProfileData
{
    public required ServerProfile ServerProfile { get; init; }
    public ServerProfileSummary? Summary { get; set; }
}