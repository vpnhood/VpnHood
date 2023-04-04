namespace VpnHood.AccessServer.Dtos;

public class ServerProfileData
{
    public required ServerProfile ServerProfile { get; init; }
    public ServerProfileSummary? Summary { get; set; }
}
