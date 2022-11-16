namespace VpnHood.AccessServer.Dtos;

public class ServerData
{
    public Server Server { get; set; } = null!;
    public AccessPoint[] AccessPoints { get; set; } = null!;
}