namespace VpnHood.AccessServer.Dtos;

public class ServerInstallBySshUserKeyParams
{
    public string HostName { get; set; }
    public int HostPort { get; set; } = 22;
    public string UserName { get; set; }
    public byte[] UserKey { get; set; }
    public string? UserKeyPassphrase { get; set; } 

    public ServerInstallBySshUserKeyParams(string hostName, string userName, byte[] userKey)
    {
        HostName = hostName;
        UserName = userName;
        UserKey = userKey;
    }
}